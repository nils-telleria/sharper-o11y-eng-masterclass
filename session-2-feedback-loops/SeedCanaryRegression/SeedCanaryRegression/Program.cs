// SeedCanaryRegression backfills the Masterclass 2 dataset: a canary deploy
// that is ~40% slower, but only for enterprise users on the billing endpoint.
// See Scenario for the scenario and why its proportions are what they are.
//
// Run this before the session, not during it. It writes into the same
// sample-service dataset Masterclass 1 uses, so the deploy marker and the
// before/after queries land on data attendees already recognise.
//
// On completion it prints the exact deploy timestamp, which the Honeycomb
// deploy marker must match — see ../../honeycomb-setup.

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scenario;

const string ServiceName = "sample-service";

// MaxQueueSize is the BatchExportActivityProcessor queue depth. Named rather
// than inlined because the delivery test needs it.
const int MaxQueueSize = 8192;

var activitySource = new ActivitySource(ServiceName);

using var provider = SetupTracing();

var cfg = Scenario.Config.Default with { Now = DateTimeOffset.UtcNow };
if (int.TryParse(Environment.GetEnvironmentVariable("SEED_COUNT"), out var c))
    cfg = cfg with { TraceCount = c };
if (ParseDuration(Environment.GetEnvironmentVariable("SEED_WINDOW")) is TimeSpan w)
    cfg = cfg with { Window = w };
if (ParseDuration(Environment.GetEnvironmentVariable("SEED_DEPLOY_AGO")) is TimeSpan d)
    cfg = cfg with { DeployAgo = d };

var requests = ScenarioGenerator.Generate(cfg, new Random());
const int chunk = 400;
int regressed = 0;

for (int i = 0; i < requests.Length; i++)
{
    Emit(activitySource, requests[i]);
    if (requests[i].Regressed) regressed++;
    if ((i + 1) % chunk == 0) Flush(provider);
}

Flush(provider);
provider.Dispose();

var deploy = cfg.DeployTime;
Console.WriteLine($"""

seeded {requests.Length} requests into dataset "{ServiceName}" over the last {cfg.Window}
  canary build     {cfg.CanaryVersion}  ({regressed} requests regressed, {100.0 * regressed / requests.Length:F2}% of traffic)
  baseline build   {cfg.BaselineVersion}
  deploy time      {deploy:O}

Next: create the deploy marker at that exact time, or the before/after
boundary will not line up with the data.

  cd ../../honeycomb-setup/scripts
  HONEYCOMB_API_KEY=<config-permission key> ./create-deploy-marker.sh {deploy.ToUnixTimeSeconds()}

""");

static void Flush(TracerProvider provider)
{
    if (!provider.ForceFlush(30_000))
        throw new Exception("ForceFlush timed out (is the Collector up? see ../../session-1-fundamentals/collector)");
}

static void Emit(ActivitySource activitySource, Request req)
{
    using var root = activitySource.StartActivity(
        $"POST {req.RoutePath}",
        ActivityKind.Server,
        default(ActivityContext),
        startTime: req.Start);

    if (root == null) return;

    root.SetTag("http.request.method", "POST");
    root.SetTag("http.route", req.RoutePath);
    root.SetTag("url.path", req.RoutePath);
    root.SetTag("http.response.status_code", req.StatusCode());
    root.SetTag("user.id", $"user_{req.UserId}");
    root.SetTag("user.type", req.UserType);
    root.SetTag("service.version", req.Version);
    root.SetTag("service.environment", "production");

    if (req.Errored)
    {
        root.SetTag("error", true);
        root.SetTag("error.type", req.ErrorType);
        root.SetStatus(ActivityStatusCode.Error, req.ErrorType);
    }

    var cursor = req.Start;
    foreach (var step in req.Steps)
    {
        var end = cursor + step.Duration;
        using var child = activitySource.StartActivity(
            step.Name, ActivityKind.Internal, root.Context, startTime: cursor);
        child?.SetEndTime(end.UtcDateTime);
        cursor = end;
    }

    root.SetEndTime(cursor.UtcDateTime);
}

static TracerProvider SetupTracing()
{
    var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
        ?? "http://localhost:4317";

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

static TimeSpan? ParseDuration(string? raw)
{
    if (raw == null) return null;
    if (raw.EndsWith("h") && double.TryParse(raw[..^1], out var h)) return TimeSpan.FromHours(h);
    if (raw.EndsWith("m") && double.TryParse(raw[..^1], out var m)) return TimeSpan.FromMinutes(m);
    if (raw.EndsWith("s") && double.TryParse(raw[..^1], out var s)) return TimeSpan.FromSeconds(s);
    return null;
}

// Expose MaxQueueSize for delivery tests.
public static class SeedConstants
{
    public const int MaxQueueSize = 8192;
}
