// DeliveryTests proves spans actually arrive at the exporter, not just that
// they are built correctly. The distinction matters because it is exactly where
// a real bug hid: the BatchExportActivityProcessor silently drops activities
// past its queue limit, so a seeder that flushed once at the end delivered a
// fraction of its data and still exited zero.

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scenario;

namespace SeedCanaryRegression.Tests;

public class DeliveryTests
{
    private const int SeedCount = 2500;
    private const int WantSpansPerTrace = 5; // root + 4 steps

    [Fact]
    public void SeedVolumeExceedsQueue()
    {
        var spans = SeedCount * WantSpansPerTrace;
        Assert.True(spans > SeedConstants.MaxQueueSize,
            $"test seeds {spans} spans against a {SeedConstants.MaxQueueSize}-span queue; " +
            "nothing would be dropped even without chunked flushing, so the delivery test proves nothing.");
    }

    [Fact]
    public void Seeder_DeliversEveryTrace()
    {
        var exported = new List<Activity>();

        using var provider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("sample-service"))
            .AddSource("sample-service")
            .AddInMemoryExporter(exported)
            .Build()!;

        var activitySource = new ActivitySource("sample-service");
        var cfg = Config.Default with
        {
            TraceCount = SeedCount,
            Now = DateTimeOffset.UtcNow,
        };

        var requests = ScenarioGenerator.Generate(cfg, new Random());
        const int chunk = 400;

        for (int i = 0; i < requests.Length; i++)
        {
            EmitRequest(activitySource, requests[i]);
            if ((i + 1) % chunk == 0)
                Assert.True(provider.ForceFlush(30_000), "ForceFlush timed out");
        }

        Assert.True(provider.ForceFlush(30_000), "Final ForceFlush timed out");

        var roots = exported.Count(a => a.ParentSpanId.Equals(default(ActivitySpanId)));
        Assert.Equal(SeedCount, roots);
        Assert.Equal(SeedCount * WantSpansPerTrace, exported.Count);
    }

    [Fact]
    public void Seeder_EmitsExpectedAttributes()
    {
        var exported = new List<Activity>();

        using var provider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("sample-service"))
            .AddSource("sample-service")
            .AddInMemoryExporter(exported)
            .Build()!;

        var activitySource = new ActivitySource("sample-service");
        var cfg = Config.Default with { TraceCount = 100, Now = DateTimeOffset.UtcNow };
        var requests = ScenarioGenerator.Generate(cfg, new Random(99));

        foreach (var req in requests) EmitRequest(activitySource, requests[0]);
        provider.ForceFlush(10_000);

        var roots = exported.Where(a => a.ParentSpanId.Equals(default(ActivitySpanId))).ToList();
        var seen = roots.SelectMany(r => r.TagObjects).Select(t => t.Key).ToHashSet();

        foreach (var key in new[]
        {
            "http.request.method", "http.response.status_code", "http.route", "url.path",
            "user.id", "user.type", "service.version", "service.environment",
        })
        {
            Assert.True(seen.Contains(key), $"no root activity carried \"{key}\"");
        }

        // Stale names from the Go version — must be absent.
        foreach (var stale in new[] { "http.method", "http.status_code", "customer.tier", "deployment.environment" })
            Assert.False(seen.Contains(stale), $"root activities carry \"{stale}\", which the book's Chapter 6 tables do not use");
    }

    [Fact]
    public void Seeder_RegressionIsVisible()
    {
        var exported = new List<Activity>();

        using var provider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("sample-service"))
            .AddSource("sample-service")
            .AddInMemoryExporter(exported)
            .Build()!;

        var activitySource = new ActivitySource("sample-service");
        var cfg = Config.Default with { TraceCount = SeedCount, Now = DateTimeOffset.UtcNow };
        var requests = ScenarioGenerator.Generate(cfg, new Random());

        const int chunk = 400;
        for (int i = 0; i < requests.Length; i++)
        {
            EmitRequest(activitySource, requests[i]);
            if ((i + 1) % chunk == 0) provider.ForceFlush(30_000);
        }

        provider.ForceFlush(30_000);

        var roots = exported.Where(a => a.ParentSpanId.Equals(default(ActivitySpanId))).ToList();
        var canary = roots
            .Where(a => (string?)a.GetTagItem("http.route") == "/api/billing"
                     && (string?)a.GetTagItem("user.type") == "enterprise"
                     && (string?)a.GetTagItem("service.version") == cfg.CanaryVersion)
            .Select(a => (a.Duration)).ToList();

        var baseline = roots
            .Where(a => (string?)a.GetTagItem("http.route") == "/api/billing"
                     && (string?)a.GetTagItem("user.type") == "enterprise"
                     && (string?)a.GetTagItem("service.version") == cfg.BaselineVersion)
            .Select(a => a.Duration).ToList();

        Assert.NotEmpty(canary);
        Assert.NotEmpty(baseline);

        var canaryP50 = Percentile(canary, 0.50);
        var baselineP50 = Percentile(baseline, 0.50);
        var ratio = canaryP50.TotalMilliseconds / baselineP50.TotalMilliseconds;

        Assert.True(ratio >= 1.2,
            $"canary P50 is only {ratio:F2}x baseline for the affected cohort ({canaryP50} vs {baselineP50}); too subtle to demo");
    }

    private static void EmitRequest(ActivitySource source, Request req)
    {
        using var root = source.StartActivity(
            $"POST {req.RoutePath}", ActivityKind.Server, default(ActivityContext), startTime: req.Start);

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
            using var child = source.StartActivity(
                step.Name, ActivityKind.Internal, root.Context, startTime: cursor);
            child?.SetEndTime((cursor + step.Duration).UtcDateTime);
            cursor += step.Duration;
        }

        root.SetEndTime(cursor.UtcDateTime);
    }

    private static TimeSpan Percentile(List<TimeSpan> ds, double p)
    {
        if (ds.Count == 0) return TimeSpan.Zero;
        var sorted = ds.OrderBy(x => x).ToList();
        return sorted[(int)(p * (sorted.Count - 1))];
    }
}
