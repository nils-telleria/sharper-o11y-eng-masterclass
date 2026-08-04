// DeliveryTests for seed-arbitrary-question. Mirrors the pattern from Sessions
// 1 and 2: proves spans actually arrive, not just that they are generated.

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using WideEvent;

namespace Session3.Tests;

public class SeedArbitraryQuestionDeliveryTests
{
    // seedCount must produce more spans than MaxQueueSize.
    private const int SeedCount = 12000;
    private const int WantSpansPerEvent = 1; // canonical-log shape: one span per event

    [Fact]
    public void SeedVolumeExceedsQueue()
    {
        var spans = SeedCount * WantSpansPerEvent;
        Assert.True(spans > 8192,
            $"test seeds {spans} spans against an 8192-span queue; " +
            "nothing would be dropped even without chunked flushing.");
    }

    [Fact]
    public void Seeder_DeliversEveryEvent()
    {
        var exported = new List<Activity>();

        using var provider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("checkout-web"))
            .AddSource("checkout-web")
            .AddInMemoryExporter(exported)
            .Build()!;

        var source = new ActivitySource("checkout-web");
        var cfg = Config.Default with { EventCount = SeedCount, Now = DateTimeOffset.UtcNow };
        var events = EventGenerator.Generate(cfg, new Random());

        const int chunk = 500;
        for (int i = 0; i < events.Length; i++)
        {
            EmitEvent(source, events[i]);
            if ((i + 1) % chunk == 0)
                Assert.True(provider.ForceFlush(30_000), "ForceFlush timed out");
        }

        Assert.True(provider.ForceFlush(30_000), "Final ForceFlush timed out");

        // One span per event, all roots.
        Assert.Equal(SeedCount, exported.Count);
        Assert.All(exported, a => Assert.True(a.ParentSpanId.Equals(default(ActivitySpanId))));
    }

    [Fact]
    public void Seeder_EmitsExpectedAttributes()
    {
        var exported = new List<Activity>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("checkout-web"))
            .AddSource("checkout-web")
            .AddInMemoryExporter(exported)
            .Build()!;

        var source = new ActivitySource("checkout-web");
        var cfg = Config.Default with { EventCount = 500, Now = DateTimeOffset.UtcNow };
        var events = EventGenerator.Generate(cfg, new Random(7));

        foreach (var e in events) EmitEvent(source, e);
        provider.ForceFlush(10_000);

        var seen = exported.SelectMany(a => a.TagObjects).Select(t => t.Key).ToHashSet();

        foreach (var key in new[]
        {
            "user_agent.device", "user_agent.app_version", "geo.region.iso_code",
            "error", "http.request.method", "http.response.status_code", "http.route",
            "url.path", "user.id", "user.type", "user.org.id", "service.version",
            "service.environment", "localization.currency", "ratelimit.remaining",
            "stats.postgres_query_count", "error.type", "exception.slug",
        })
        {
            Assert.True(seen.Contains(key), $"no activity carried \"{key}\"");
        }
    }

    [Fact]
    public void Seeder_QuestionIsAnswerable()
    {
        var exported = new List<Activity>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("checkout-web"))
            .AddSource("checkout-web")
            .AddInMemoryExporter(exported)
            .Build()!;

        var source = new ActivitySource("checkout-web");
        var cfg = Config.Default with { EventCount = SeedCount, Now = DateTimeOffset.UtcNow };
        var events = EventGenerator.Generate(cfg, new Random());

        const int chunk = 500;
        for (int i = 0; i < events.Length; i++)
        {
            EmitEvent(source, events[i]);
            if ((i + 1) % chunk == 0) provider.ForceFlush(30_000);
        }

        provider.ForceFlush(30_000);

        var matches = exported.Count(a =>
            (a.GetTagItem("error") as bool? ?? false)
            && (string?)a.GetTagItem("user_agent.device") == Constants.AnswerDevice
            && (string?)a.GetTagItem("geo.region.iso_code") == Constants.AnswerRegion
            && (string?)a.GetTagItem("user_agent.app_version") == Constants.AnswerVersion
            && int.TryParse(a.GetTagItem("request.local_hour")?.ToString(), out var h)
            && h >= Constants.LunchHourStart && h < Constants.LunchHourEnd);

        Assert.True(matches > 0,
            "no events on the wire answer the book's arbitrary question; the live demo would show an empty result");
    }

    private static void EmitEvent(ActivitySource source, Event e)
    {
        using var activity = source.StartActivity(
            $"POST {e.Route}", ActivityKind.Server, default(ActivityContext), startTime: e.Start);

        if (activity == null) return;

        EventAttributes.Apply(activity, e);
        if (e.Errored)
            activity.SetStatus(ActivityStatusCode.Error, e.ErrorType);

        activity.SetEndTime((e.Start + e.Duration).UtcDateTime);
    }
}
