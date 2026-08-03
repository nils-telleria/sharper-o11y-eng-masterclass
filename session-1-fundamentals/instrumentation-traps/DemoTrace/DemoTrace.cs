// Package DemoTrace holds the OTel SDK bootstrapping shared by the
// instrumentation-traps examples, so each before/after pair can stay focused
// on the one thing it's demonstrating.

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DemoTrace;

public static class DemoTracing
{
    // Setup wires a real TracerProvider (exporting to the local Collector) as
    // the SDK provider and registers W3C trace-context propagation, then
    // returns a TracerProvider to dispose (which flushes and releases resources).
    //
    // W3C TraceContext is the default propagator in .NET OTel, so no explicit
    // propagator setup is needed — the equivalent of Go's
    // otel.SetTextMapPropagator(propagation.NewCompositeTextMapPropagator(...))
    // is handled automatically.
    public static TracerProvider Setup(string serviceName)
    {
        var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? "http://localhost:4317";

        return Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
            .AddSource(serviceName)
            .AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(endpoint);
                // Plaintext to the local Collector; never straight to Honeycomb.
                o.Protocol = OtlpExportProtocol.Grpc;
            })
            .Build()!;
    }
}
