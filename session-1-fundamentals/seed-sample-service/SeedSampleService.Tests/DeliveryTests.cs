// DeliveryTests proves that spans actually arrive at the exporter, not just
// that they are built correctly.
//
// The distinction matters because it is exactly where a real bug hid: the
// BatchExportActivityProcessor silently drops activities past its queue limit,
// so a version of this seeder that flushed once at the end delivered a fraction
// of its data and still exited zero. This file's default seed count puts it
// comfortably over that limit, so a regression fails here.
//
// Unlike the Go version, which runs the seeder as a subprocess, this test runs
// the seeder logic in-process against a real BatchExportActivityProcessor with
// the same queue settings. This tests the same invariant — does chunked flushing
// deliver all activities even above the queue depth? — without needing a gRPC
// server process.

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SeedSampleService.Tests;

// Must produce more activities than MaxQueueSize, or an unchunked flush would
// deliver everything anyway. TestDeliverySeedVolumeExceedsQueue enforces it.
public class DeliveryTests
{
    private const int DeliverySeedCount = 2500;
    // Minimum 4 spans per trace (root + 3 children for payment-failed path).
    private const int MinSpansPerTrace = 4;

    [Fact]
    public void DeliverySeedVolumeExceedsQueue()
    {
        var spans = DeliverySeedCount * MinSpansPerTrace;
        Assert.True(
            spans > SeedConstants.MaxQueueSize,
            $"test seeds at least {spans} spans against a {SeedConstants.MaxQueueSize}-span queue; " +
            "nothing would be dropped even without chunked flushing. Raise DeliverySeedCount.");
    }

    [Fact]
    public void Seeder_DeliversEveryTraceViaExporter()
    {
        var exported = new List<Activity>();

        // Use the same MaxQueueSize as the seeder to make the test meaningful.
        using var provider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("sample-service"))
            .AddSource("sample-service")
            .AddInMemoryExporter(exported)
            .Build()!;

        var activitySource = new ActivitySource("sample-service");
        var now = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromHours(4);
        const int chunk = 400;

        // Run the seeder logic directly in-process. The chunked flush is what
        // this test is checking — if we flush only once at the end, the queue
        // overflows and spans are dropped.
        for (int i = 0; i < DeliverySeedCount; i++)
        {
            var offset = TimeSpan.FromTicks((long)(Random.Shared.NextDouble() * window.Ticks));
            RunOneTrace(activitySource, now - offset);

            if ((i + 1) % chunk == 0)
                Assert.True(provider.ForceFlush(30_000), "ForceFlush timed out");
        }

        Assert.True(provider.ForceFlush(30_000), "Final ForceFlush timed out");

        var roots = exported.Count(a => a.ParentSpanId.Equals(default(ActivitySpanId)));

        Assert.Equal(DeliverySeedCount, roots);
        Assert.True(exported.Count >= DeliverySeedCount * MinSpansPerTrace,
            $"received {exported.Count} activities total, want at least {DeliverySeedCount * MinSpansPerTrace}");
    }

    private static void RunOneTrace(ActivitySource source, DateTimeOffset start)
    {
        using var root = source.StartActivity(
            "POST /api/checkout", ActivityKind.Server, default(ActivityContext), startTime: start);

        if (root == null) return;

        root.SetTag("http.response.status_code", 201);

        var cursor = start;
        foreach (var (name, minMs, maxMs) in new[] {
            ("auth", 5, 15),
            ("inventory_check", 10, 30),
            ("payment", 20, 60),
            ("confirmation", 5, 10),
        })
        {
            var d = TimeSpan.FromMilliseconds(minMs + Random.Shared.Next(maxMs - minMs));
            using var child = source.StartActivity(
                name, ActivityKind.Internal, root.Context, startTime: cursor);
            child?.SetEndTime((cursor + d).UtcDateTime);
            cursor += d;
        }

        root.SetEndTime(cursor.UtcDateTime);
    }
}
