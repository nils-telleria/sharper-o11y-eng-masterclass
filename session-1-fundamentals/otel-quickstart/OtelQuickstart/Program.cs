// OtelQuickstart is the Masterclass 1 live demo, delivered in three beats:
//
//  1. Auto-instrumentation alone. AddAspNetCoreInstrumentation gives you
//     POST /api/checkout with about a dozen attributes and no code of your own.
//  2. One more attribute. order.id is a fact about the request, so it widens
//     the activity that already exists rather than getting its own span.
//  3. One more span. Only once you want a duration measured separately from
//     the request's does a second activity earn its place.
//
// An attribute describes; a span measures. Beats 2 and 3 ship commented out so
// the demo starts from the auto-instrumentation baseline and each beat is one
// uncomment.
//
// Reused as the /api/checkout service in later sessions (e.g. Masterclass 4's SLIs).
//
// NOTE — one difference from the Go version: ASP.NET Core's OTel instrumentation
// DOES set http.route from the Minimal API route template, so the manual
// setRoute() call from the Go version is not needed here. That changes beat 1's
// attribute count but not the lesson: auto-instrumentation still misses business
// context like order.id.

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

const string ServiceName = "otel-quickstart";

// activitySource is only needed by beat 3. It sits here uncommented so that beat
// is a single uncomment in ProcessOrder; an unused ActivitySource causes no harm
// — it only allocates if a listener is registered.
var activitySource = new ActivitySource(ServiceName);

var builder = WebApplication.CreateBuilder(args);

// setupTracing wires the OTel SDK to export over OTLP gRPC to the local
// Collector (which holds the Honeycomb API key).
var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(ServiceName))
    .WithTracing(tracing => tracing
        .AddSource(ServiceName)
        // AddAspNetCoreInstrumentation replaces Go's otelhttp.NewHandler. It
        // hooks into the ASP.NET Core pipeline via diagnostics and gives you:
        // http.request.method, http.response.status_code, http.route, url.path,
        // url.scheme, server.address, server.port, network.protocol.version,
        // user_agent.original, and more — all for free.
        //
        // Unlike Go's otelhttp, ASP.NET Core instrumentation DOES set http.route
        // from the Minimal API route template, so no manual enrichment is needed
        // for that attribute.
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(endpoint);
            // WithInsecure equivalent: plaintext to the local Collector.
            o.Protocol = OtlpExportProtocol.Grpc;
        }));

// setupPropagation: ASP.NET Core OTel wires W3C TraceContext + Baggage by
// default via Propagators.DefaultTextMapPropagator, so no explicit setup is
// needed here — the equivalent of Go's setupPropagation() is automatic.

var app = builder.Build();

app.MapGet("/healthz", (HttpContext ctx) =>
{
    Results.Ok();
});

app.MapPost("/api/checkout", async (HttpContext ctx) =>
{
    var orderId = NewOrderId();

    // --- BEAT 2: one more attribute. Uncomment live. ---
    // order.id is a fact about this request, not a duration, so it widens the
    // activity AspNetCore instrumentation already created. Wrapping it in a
    // child span would add a node to the trace without adding a measurement,
    // and would split one request's context across two narrower events instead
    // of widening one.
    // Activity.Current?.SetTag("order.id", orderId);
    // --- END BEAT 2 ---

    ProcessOrder(activitySource, orderId);

    return Results.Created("/api/checkout", new { order_id = orderId });
});

app.Run($"http://localhost:8080");

// enrich adds a tag to the activity AspNetCore instrumentation already created
// for this request. This is the move Chapter 6 is about: widen the event you
// already have rather than starting a new one.
static void Enrich(string key, object value) =>
    Activity.Current?.SetTag(key, value);

// ProcessOrder is beat 3. A span here is justified where the attribute alone was
// not: we want processing time as its own measurement, separate from total
// request duration. That is the question a span answers, and the reason to pay
// for a second event.
//
// order.id goes on this span as well, because attributes do not inherit down a
// trace. Each span is its own event, so a span you want to filter or group by
// order has to carry order.id itself — otherwise querying order.id finds the
// request and not the work done inside it.
static void ProcessOrder(ActivitySource activitySource, string orderId)
{
    // --- BEAT 3: one more span. Uncomment live. ---
    // using var span = activitySource.StartActivity("process_order");
    // span?.SetTag("order.id", orderId);
    // --- END BEAT 3 ---

    SimulateOrderProcessing();
}

static void SimulateOrderProcessing() =>
    Thread.Sleep(TimeSpan.FromMilliseconds(20 + Random.Shared.Next(80)));

static string NewOrderId() =>
    $"ord_{Random.Shared.NextInt64():x}";

// Make Program accessible to integration tests via WebApplicationFactory<Program>.
public partial class Program { }
