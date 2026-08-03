// Trap: the TracerProvider is configured to listen to the vendor library's
// ActivitySource by name, so its four internal activities per call land in
// every trace right alongside your own — the "noisy library instrumentation
// floods traces" problem.
//
// In C#, TracerProvider.AddSource(name) opts in to receiving activities from
// that source. Not calling AddSource is the escape hatch (no noop provider
// swap needed, unlike Go).

using System.Diagnostics;
using DemoTrace;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using VendorLib;

const string ServiceName = "instrumentation-traps-noisy-library-before";
const string VendorSourceName = "vendor-library-x";

var activitySource = new ActivitySource(ServiceName);

// BUG: AddSource includes the vendor library's source, so every client.Do()
// call adds 4 activities (acquire_connection, serialize, network_write,
// network_read) to this trace that nobody debugging *this* service needs.
var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
    ?? "http://localhost:4317";
using var provider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(ServiceName))
    .AddSource(ServiceName)
    .AddSource(VendorSourceName)  // <-- the bug: we opted into the noisy vendor source
    .AddOtlpExporter(o => { o.Endpoint = new Uri(endpoint); o.Protocol = OtlpExportProtocol.Grpc; })
    .Build()!;

var client = new Client(VendorSourceName);

using var span = activitySource.StartActivity("handle_request");
client.Do();
span?.Stop();

Console.WriteLine("BUG: this trace has 5 activities (handle_request + 4 noisy vendor internals).");
provider.ForceFlush(5_000);
