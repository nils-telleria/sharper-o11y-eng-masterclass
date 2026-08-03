// Fix: simply do NOT call AddSource for the vendor library's ActivitySource.
// When no TracerProvider is listening to "vendor-library-x", the vendor's
// Client.Do() calls StartActivity and gets null back — activities are never
// created, so they can't flood the trace.
//
// This is the C# equivalent of Go's noop.NewTracerProvider() escape hatch:
// instead of passing a noop provider TO the library, we just omit the
// library's source from our provider's listener list.

using System.Diagnostics;
using DemoTrace;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using VendorLib;

const string ServiceName = "instrumentation-traps-noisy-library-after";
const string VendorSourceName = "vendor-library-x";

var activitySource = new ActivitySource(ServiceName);

// FIXED: only AddSource for our own service — the vendor source is omitted, so
// its activities return null and are never created.
var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
    ?? "http://localhost:4317";
using var provider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(ServiceName))
    .AddSource(ServiceName)
    // No AddSource(VendorSourceName) — the vendor's activities are never created.
    .AddOtlpExporter(o => { o.Endpoint = new Uri(endpoint); o.Protocol = OtlpExportProtocol.Grpc; })
    .Build()!;

var client = new Client(VendorSourceName);

using var span = activitySource.StartActivity("handle_request");
client.Do();
span?.Stop();

Console.WriteLine("FIXED: this trace has 1 activity (handle_request); vendor internals were never created.");
provider.ForceFlush(5_000);
