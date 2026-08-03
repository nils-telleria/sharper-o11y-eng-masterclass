// SeedArbitraryQuestion backfills the Masterclass 3 dataset: a week of genuinely
// wide checkout events, used to run Chapter 28's Arbitrary Question Test live.

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using WideEvent;

const string ServiceName = "checkout-web";
const int MaxQueueSize = 8192;

var activitySource = new ActivitySource(ServiceName);
using var provider = SetupTracing();

var cfg = Config.Default with { Now = DateTimeOffset.UtcNow };
if (int.TryParse(Environment.GetEnvironmentVariable("SEED_COUNT"), out var cnt))
    cfg = cfg with { EventCount = cnt };
if (ParseDuration(Environment.GetEnvironmentVariable("SEED_WINDOW")) is TimeSpan w)
    cfg = cfg with { Window = w };

var events = EventGenerator.Generate(cfg, new Random());
const int chunk = 500;
int matches = 0;

for (int i = 0; i < events.Length; i++)
{
    Emit(activitySource, events[i]);
    if (events[i].MatchesArbitraryQuestion()) matches++;
    if ((i + 1) % chunk == 0) Flush(provider);
}

Flush(provider);
provider.Dispose();

Console.WriteLine($"""

seeded {events.Length} checkout events into dataset "{ServiceName}" over the last {cfg.Window}
  attributes per event   {EventAttributes.Count(events[0])}
  matching the question  {matches}

The Arbitrary Question Test (Chapter 28). Ask:

  "Show me all failed checkout attempts from mobile users in California
   using version {Constants.AnswerVersion} of the app during lunch hour over the past week."

In Honeycomb, that is a COUNT with these filters:

  error                  = true
  user_agent.device      = {Constants.AnswerDevice}          <- "mobile" is device=phone in the convention
  geo.region.iso_code    = {Constants.AnswerRegion}          <- California, ISO 3166-2
  user_agent.app_version = {Constants.AnswerVersion}
  and a {Constants.LunchHourStart}:00-{Constants.LunchHourEnd}:00 time-of-day filter

Expect roughly {matches} results. If it comes back empty, reseed before going live.

""");

static void Flush(TracerProvider provider)
{
    if (!provider.ForceFlush(30_000))
        throw new Exception("ForceFlush timed out (is the Collector up? see ../../../session-1-fundamentals/collector)");
}

static void Emit(ActivitySource source, Event e)
{
    using var activity = source.StartActivity(
        $"POST {e.Route}",
        ActivityKind.Server,
        default(ActivityContext),
        startTime: e.Start);

    if (activity == null) return;

    EventAttributes.Apply(activity, e);
    if (e.Errored)
        activity.SetStatus(ActivityStatusCode.Error, e.ErrorType);

    activity.SetEndTime((e.Start + e.Duration).UtcDateTime);
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

public static class SeedConstants
{
    public const int MaxQueueSize = 8192;
}
