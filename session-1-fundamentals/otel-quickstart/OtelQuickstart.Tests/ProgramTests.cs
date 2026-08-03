// Tests for otel-quickstart. Each test drives real HTTP requests through the
// full ASP.NET Core pipeline and inspects the activities (spans) that were
// exported.
//
// The exporter is shared across the fixture via IClassFixture; each test records
// the count before its request and takes only the slice it appended, so tests
// must run sequentially (the xUnit default within a class).
//
// Structure mirrors the Go test file closely: TestCheckoutTraceShape pins the
// properties that must hold at every beat, and TestInboundTraceparentIsJoined
// covers the propagation path that fails silently if no propagator is set up.

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OtelQuickstart.Tests;

// One WebApplicationFactory per class; the in-memory exporter accumulates
// across all tests in the class.
public class ProgramTests : IClassFixture<OtelQuickstartFactory>
{
    private readonly OtelQuickstartFactory _factory;

    public ProgramTests(OtelQuickstartFactory factory) => _factory = factory;

    // SpansFor drives one request through the handler and returns only the
    // activities that request produced. Tests are sequential so the
    // before/after slice boundary is stable.
    private List<Activity> SpansFor(HttpRequestMessage req, HttpStatusCode wantStatus)
    {
        var before = _factory.ExportedActivities.Count;
        using var client = _factory.CreateClient();
        var resp = client.SendAsync(req).GetAwaiter().GetResult();
        Assert.Equal(wantStatus, resp.StatusCode);

        var spans = _factory.ExportedActivities.Skip(before).ToList();
        Assert.NotEmpty(spans);
        return spans;
    }

    // TestCheckoutTraceShape pins the properties that must hold at every beat
    // of the demo, including with beats 2 and 3 still commented out as
    // committed.
    //
    // Activity count deliberately is not asserted: beat 3 legitimately adds
    // one. What must never change is where order.id lives. An attribute
    // describing the request belongs on the request's own activity, so if it
    // ever migrates onto a child this fails.
    [Fact]
    public void TestCheckoutTraceShape()
    {
        var spans = SpansFor(
            new HttpRequestMessage(HttpMethod.Post, "/api/checkout"),
            HttpStatusCode.Created);

        var roots = spans.Where(s => !s.ParentId.HasValue || s.ParentId == default).ToList();
        var children = spans.Where(s => s.ParentId.HasValue && s.ParentId != default).ToList();

        // One root, always. A request with no inbound traceparent starts its
        // own trace; if the request activity ever gained a parent here, every
        // trace in the dataset would render with phantom missing spans.
        Assert.Single(roots);
        var root = roots[0];

        // ASP.NET Core instrumentation names the activity after the route
        // template, e.g. "POST /api/checkout". Guards against a change that
        // reverts to a generic operation name.
        Assert.Equal("POST /api/checkout", root.DisplayName);

        // http.route is set automatically by ASP.NET Core instrumentation
        // (unlike Go's otelhttp). It is still highlighted in the session notes
        // as an important attribute; this test asserts it lands correctly.
        Assert.Equal("/api/checkout", root.GetTagItem("http.route") as string);

        var rootOrderId = root.GetTagItem("order.id") as string;

        // Children are allowed (beat 3), but each must hang off the request
        // activity and carry order.id itself. Attributes do not inherit down a
        // trace.
        foreach (var child in children)
        {
            Assert.Equal(root.Id, child.ParentId);

            var childOrderId = child.GetTagItem("order.id") as string;

            if (childOrderId != null && rootOrderId == null)
            {
                Assert.Fail(
                    $"Activity '{child.DisplayName}' carries order.id but the request activity does not; " +
                    "the attribute describes the request, so it belongs there first");
            }
            if (rootOrderId != null && childOrderId == null)
            {
                Assert.Fail(
                    $"Activity '{child.DisplayName}' is missing order.id; attributes do not inherit, " +
                    "so this activity cannot be sliced by order");
            }
            if (childOrderId != null && childOrderId != rootOrderId)
            {
                Assert.Fail(
                    $"Activity '{child.DisplayName}' has order.id '{childOrderId}', " +
                    $"want '{rootOrderId}' to match the request activity");
            }
        }
    }

    // TestInboundTraceparentIsJoined covers a failure that is invisible in the
    // resulting data: with no propagator registered, an inbound traceparent is
    // dropped and this service silently starts a second, disconnected trace
    // rather than continuing the caller's.
    [Fact]
    public void TestInboundTraceparentIsJoined()
    {
        const string UpstreamTrace = "4bf92f3577b34da6a3ce929d0e0e4736";
        const string UpstreamSpan = "00f067aa0ba902b7";

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout");
        req.Headers.Add("traceparent", $"00-{UpstreamTrace}-{UpstreamSpan}-01");

        var spans = SpansFor(req, HttpStatusCode.Created);

        var requestSpan = spans.FirstOrDefault(s => s.DisplayName == "POST /api/checkout");
        Assert.NotNull(requestSpan);

        Assert.Equal(UpstreamTrace, requestSpan.TraceId.ToString());
        Assert.Equal(UpstreamSpan, requestSpan.ParentSpanId.ToString());
        Assert.True(requestSpan.HasRemoteParent,
            "Parent should be marked as remote; it came from the traceparent header");
    }
}

// OtelQuickstartFactory wires WebApplicationFactory with an in-memory exporter
// so tests can inspect activities without running the Collector.
public class OtelQuickstartFactory : WebApplicationFactory<Program>
{
    public List<Activity> ExportedActivities { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Add a SimpleExporter (sync, no queue) so activities land in the
            // list immediately when they end — equivalent to Go's
            // sdktrace.WithSyncer(exporter).
            services.ConfigureOpenTelemetryTracerProvider(tracing =>
                tracing.AddInMemoryExporter(ExportedActivities));
        });
    }
}
