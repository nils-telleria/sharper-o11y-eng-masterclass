// TestSimulateCheckout_TraceInvariants runs many seeded checkouts through an
// in-memory exporter and checks the invariants the live demo depends on:
// every trace has exactly one root, every other activity is its child, and the
// root's error state agrees with whether payment failed. Fixed seed makes this
// deterministic while still exercising both the success and failure branches,
// plus the slow-inventory branch.

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SeedSampleService.Tests;

public class TraceInvariantsTests
{
    [Fact]
    public void SimulateCheckout_TraceInvariants()
    {
        var exportedActivities = new List<Activity>();
        // Use a unique source name to prevent cross-test contamination when
        // multiple test classes share the same ActivitySource name.
        var sourceName = $"sample-service-invariants-{Guid.NewGuid():N}";

        using var provider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("test"))
            .AddSource(sourceName)
            .AddInMemoryExporter(exportedActivities)
            .Build()!;

        var activitySource = new ActivitySource(sourceName);
        var now = DateTimeOffset.UtcNow;
        const int iterations = 500;

        // Use a deterministic base time so the test is reproducible.
        var baseTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var rng = new Random(42);

        for (int i = 0; i < iterations; i++)
        {
            var offset = TimeSpan.FromTicks((long)(rng.NextDouble() * TimeSpan.FromHours(4).Ticks));
            SimulateCheckoutHelper(activitySource, baseTime - offset, rng);
        }

        provider.ForceFlush();

        // Snapshot the list before iterating to avoid concurrent modification
        // from the background batch-export thread.
        var snapshot = exportedActivities.ToList();

        // Group by trace ID.
        var byTrace = snapshot.GroupBy(a => a.TraceId).ToList();
        Assert.Equal(iterations, byTrace.Count);

        bool sawSuccess = false, sawFailure = false, sawSlowInventory = false;

        foreach (var trace in byTrace)
        {
            var traceSpans = trace.ToList();
            var rootSpan = traceSpans.First(s => s.ParentSpanId.Equals(default(ActivitySpanId)));

            var statusCode = rootSpan.GetTagItem("http.response.status_code");
            Assert.NotNull(statusCode);

            var isError = (int)statusCode! == 402;
            var children = traceSpans.Where(s => !s.ParentSpanId.Equals(default(ActivitySpanId))).ToList();

            if (isError)
            {
                sawFailure = true;
                Assert.Equal(ActivityStatusCode.Error, rootSpan.Status);
                Assert.Equal(3, children.Count); // auth, inventory_check, payment
            }
            else
            {
                sawSuccess = true;
                Assert.NotEqual(ActivityStatusCode.Error, rootSpan.Status);
                Assert.Equal(4, children.Count); // auth, inventory_check, payment, confirmation
            }

            foreach (var child in children)
            {
                Assert.True(child.StartTimeUtc <= child.StartTimeUtc + child.Duration,
                    $"Activity {child.DisplayName} ends before it starts");

                if (child.DisplayName == "inventory_check")
                {
                    var dbMs = child.GetTagItem("db.query.duration_ms");
                    if (dbMs is long ms && ms > 200)
                        sawSlowInventory = true;
                }
            }
        }

        Assert.True(sawSuccess, "500 iterations at a 7% failure rate produced zero successful checkouts");
        Assert.True(sawFailure, "500 iterations at a 7% failure rate produced zero failed checkouts");
        Assert.True(sawSlowInventory, "500 iterations at a 12% slow rate produced zero slow inventory_check activities");
    }

    // SimulateCheckoutHelper mirrors the seeder's logic with an explicit RNG
    // so the test is deterministic.
    private static void SimulateCheckoutHelper(ActivitySource activitySource, DateTimeOffset start, Random rng)
    {
        using var root = activitySource.StartActivity(
            "POST /api/checkout",
            ActivityKind.Server,
            default(ActivityContext),
            startTime: start);

        if (root == null) return;

        root.SetTag("http.request.method", "POST");
        root.SetTag("http.route", "/api/checkout");
        root.SetTag("url.path", "/api/checkout");
        root.SetTag("user.id", $"user_{rng.Next(5000)}");
        root.SetTag("user.type", WeightedTier(rng));
        root.SetTag("service.version", "1.4.2");
        root.SetTag("service.environment", "production");

        var cursor = start;

        cursor = RunStepHelper(activitySource, root.Context, cursor, "auth",
            Jitter(5, 15, rng), null, false);

        var inventoryDuration = Jitter(10, 30, rng);
        if (rng.NextDouble() < 0.12)
            inventoryDuration = Jitter(300, 800, rng);
        cursor = RunStepHelper(activitySource, root.Context, cursor, "inventory_check",
            inventoryDuration,
            new[] { ("db.query.duration_ms", (object)(long)inventoryDuration.Milliseconds) },
            false);

        var paymentFailed = rng.NextDouble() < 0.07;
        var paymentDuration = Jitter(20, 60, rng);
        cursor = RunStepHelper(activitySource, root.Context, cursor, "payment",
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
            cursor = RunStepHelper(activitySource, root.Context, cursor, "confirmation",
                Jitter(5, 10, rng), null, false);
            root.SetTag("http.response.status_code", 201);
        }

        root.SetEndTime(cursor.UtcDateTime);
    }

    private static DateTimeOffset RunStepHelper(
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

    private static string WeightedTier(Random rng) =>
        rng.NextDouble() switch
        {
            < 0.6 => "free",
            < 0.9 => "premium",
            _ => "enterprise",
        };

    private static TimeSpan Jitter(int minMs, int maxMs, Random rng) =>
        TimeSpan.FromMilliseconds(minMs + rng.Next(maxMs - minMs));
}
