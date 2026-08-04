// SeedSampleService backfills the "sample-service" Honeycomb dataset used in
// Masterclass 1's "one dataset, three ways" demo. Run this once, well before
// you go live — it emits historical-timestamped checkout traces
// (auth -> inventory_check -> payment -> confirmation) with a realistic error
// rate and a slow subset, so the count/rate view, the filtered search, and the
// trace waterfall all have something worth looking at.

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

const string ServiceName = "sample-service";

// MaxQueueSize is the BatchExportActivityProcessor queue depth. Named rather
// than inlined because DeliveryTests.cs asserts against it: a test seeding
// fewer spans than this cannot detect a dropped-span regression at all.
const int MaxQueueSize = 8192;

var activitySource = new ActivitySource(ServiceName);

using var provider = SetupTracing();

var count = EnvInt("SEED_COUNT", 2500);
var window = EnvDuration("SEED_WINDOW", TimeSpan.FromHours(4));
var now = DateTimeOffset.UtcNow;

// Flush every chunk rather than once at the end. A backfill produces activities
// far faster than the exporter ships them, and the BatchExportActivityProcessor
// silently drops whatever overflows its queue — so a single trailing flush
// quietly loses most of the data at higher SEED_COUNT values.
const int Chunk = 400;

for (int i = 0; i < count; i++)
{
    var offset = TimeSpan.FromTicks((long)(Random.Shared.NextDouble() * window.Ticks));
    SimulateCheckout(activitySource, now - offset);

    if ((i + 1) % Chunk == 0)
        Flush(provider);
}

Flush(provider);
Console.WriteLine($"seeded {count} checkout traces across the last {window} into dataset \"{ServiceName}\"");

// Flush blocks until the exporter has shipped everything queued so far, so the
// caller can safely generate the next chunk.
static void Flush(TracerProvider provider)
{
    if (!provider.ForceFlush(30_000))
        throw new Exception("ForceFlush timed out (is the Collector up? see ../collector)");
}

// SimulateCheckout builds one checkout trace starting at start, with attributes
// drawn from the slide's four categories: identity (user.id, user.type),
// request/execution (http.request.method, http.route, url.path), outcome
// (http.response.status_code, error, error.type), and service/code context
// (service.version, service.environment).
static void SimulateCheckout(ActivitySource activitySource, DateTimeOffset start)
{
    using var root = activitySource.StartActivity(
        "POST /api/checkout",
        ActivityKind.Server,
        default(ActivityContext),
        startTime: start);

    if (root == null) return; // no listener

    root.SetTag("http.request.method", "POST");
    root.SetTag("http.route", "/api/checkout");
    root.SetTag("url.path", "/api/checkout");
    root.SetTag("user.id", $"user_{Random.Shared.Next(5000)}");
    root.SetTag("user.type", WeightedTier());
    root.SetTag("service.version", "1.4.2");
    // service.environment rather than deployment.environment: it's what the
    // book's Table 6-1 uses, and OTel has renamed its own experimental
    // deployment.environment to deployment.environment.name since.
    root.SetTag("service.environment", "production");

    var cursor = start;

    cursor = RunStep(activitySource, root.Context, cursor, "auth",
        Jitter(5, 15), null, false);

    var inventoryDuration = Jitter(10, 30);
    if (Random.Shared.NextDouble() < 0.12) // cold cache: the slow subset the demo's waterfall relies on
        inventoryDuration = Jitter(300, 800);
    cursor = RunStep(activitySource, root.Context, cursor, "inventory_check",
        inventoryDuration,
        new[] { ("db.query.duration_ms", (object)(long)inventoryDuration.Milliseconds) },
        false);

    var paymentFailed = Random.Shared.NextDouble() < 0.07;
    var paymentDuration = Jitter(20, 60);
    cursor = RunStep(activitySource, root.Context, cursor, "payment",
        paymentDuration,
        new[] { ("db.query.duration_ms", (object)(long)paymentDuration.Milliseconds) },
        paymentFailed);

    if (paymentFailed)
    {
        root.SetTag("http.response.status_code", 402);
        root.SetTag("error", true);
        root.SetTag("error.type", "payment_declined");
        root.SetStatus(ActivityStatusCode.Error, "payment_declined");
    }
    else
    {
        cursor = RunStep(activitySource, root.Context, cursor, "confirmation",
            Jitter(5, 10), null, false);
        root.SetTag("http.response.status_code", 201);
    }

    root.SetEndTime(cursor.UtcDateTime);
}

// RunStep starts and ends a child activity at explicit historical timestamps,
// returning the timestamp the next step should start at.
static DateTimeOffset RunStep(
    ActivitySource activitySource,
    ActivityContext parent,
    DateTimeOffset start,
    string name,
    TimeSpan duration,
    (string key, object value)[]? attrs,
    bool failed)
{
    var end = start + duration;

    using var span = activitySource.StartActivity(
        name, ActivityKind.Internal, parent, startTime: start);

    if (span != null)
    {
        if (attrs != null)
            foreach (var (key, value) in attrs)
                span.SetTag(key, value);
        if (failed)
            span.SetStatus(ActivityStatusCode.Error, $"{name} failed");
        span.SetEndTime(end.UtcDateTime);
    }

    return end;
}

static string WeightedTier() =>
    Random.Shared.NextDouble() switch
    {
        < 0.6 => "free",
        < 0.9 => "premium",
        _ => "enterprise",
    };

static TimeSpan Jitter(int minMs, int maxMs) =>
    TimeSpan.FromMilliseconds(minMs + Random.Shared.Next(maxMs - minMs));

static int EnvInt(string key, int def) =>
    int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;

static TimeSpan EnvDuration(string key, TimeSpan def)
{
    var raw = Environment.GetEnvironmentVariable(key);
    if (raw == null) return def;
    // Parse Go duration strings like "4h", "30m", "90s"
    if (raw.EndsWith("h") && double.TryParse(raw[..^1], out var h)) return TimeSpan.FromHours(h);
    if (raw.EndsWith("m") && double.TryParse(raw[..^1], out var m)) return TimeSpan.FromMinutes(m);
    if (raw.EndsWith("s") && double.TryParse(raw[..^1], out var s)) return TimeSpan.FromSeconds(s);
    return def;
}

static TracerProvider SetupTracing()
{
    var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";

    return Sdk.CreateTracerProviderBuilder()
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(ServiceName))
        .AddSource(ServiceName)
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(endpoint);
            o.Protocol = OtlpExportProtocol.Grpc;
        })
        .Build()!;
}

// Expose MaxQueueSize for DeliveryTests.
public static class SeedConstants
{
    public const int MaxQueueSize = 8192;
}
