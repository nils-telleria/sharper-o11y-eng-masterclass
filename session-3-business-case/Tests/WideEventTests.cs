using WideEvent;

namespace Session3.Tests;

public class WideEventTests
{
    private static (Config cfg, Event[] events) Generate()
    {
        var cfg = Config.Default with
        {
            Now = new DateTimeOffset(2026, 9, 2, 15, 0, 0, TimeSpan.Zero),
        };
        return (cfg, EventGenerator.Generate(cfg, new Random(11)));
    }

    // TestArbitraryQuestionHasAnAnswer is the load-bearing test: the demo is
    // showing the book's question live, so the dataset must contain enough
    // matching events to return a non-trivial answer.
    [Fact]
    public void ArbitraryQuestionHasAnAnswer()
    {
        var (_, events) = Generate();
        var matches = events.Count(e => e.MatchesArbitraryQuestion());

        Assert.True(matches >= 5, $"only {matches} events match the book's question; the live demo would show a near-empty result");

        var share = (double)matches / events.Length;
        Assert.True(share <= 0.01, $"{share * 100:F2}% of events match; the filters are not narrowing enough to be interesting");
    }

    [Fact]
    public void EventsAreActuallyWide()
    {
        var (_, events) = Generate();
        const int wantAtLeast = 25;
        // Mobile events have more attributes (app + version + error fields).
        var mobileEvent = events.First(e => e.Device == "phone");
        var count = EventAttributes.Count(mobileEvent);
        Assert.True(count >= wantAtLeast,
            $"event carries {count} attributes, want at least {wantAtLeast} for the wide-event claim to hold");
    }

    [Fact]
    public void ClientFieldsAreSelfConsistent()
    {
        var (_, events) = Generate();

        foreach (var e in events)
        {
            switch (e.Device)
            {
                case "computer":
                    Assert.True(e.App == "" && e.AppVersion == "",
                        $"desktop event reports app={e.App} version={e.AppVersion}");
                    break;
                case "phone":
                case "tablet":
                    Assert.False(e.App == "" || e.AppVersion == "",
                        $"{e.Device} event has no app or version");
                    if (e.App == "iOS")
                        Assert.Equal("iOS", e.OsName);
                    if (e.App == "android")
                        Assert.Equal("Android", e.OsName);
                    break;
                default:
                    Assert.Fail($"unexpected device '{e.Device}'");
                    break;
            }

            Assert.Equal(e.Errored, e.StatusCode == 402);
            if (e.Errored)
            {
                Assert.False(string.IsNullOrEmpty(e.ErrorType));
                Assert.False(string.IsNullOrEmpty(e.ErrorSlug));
            }
            else
            {
                Assert.Equal("", e.ErrorType);
                Assert.Equal("", e.ErrorSlug);
            }
        }
    }

    [Theory]
    [InlineData("errored", 0.02, 0.20)]
    [InlineData("device=phone", 0.30, 0.70)]
    [InlineData("region=US-CA", 0.15, 0.45)]
    [InlineData("app_version=2.3.1", 0.10, 0.45)]
    [InlineData("lunch hour", 0.10, 0.45)]
    public void EveryFilterActuallyNarrows(string name, double wantAtLeast, double wantBelow)
    {
        var (_, events) = Generate();
        var total = (double)events.Length;

        int n = name switch
        {
            "errored" => events.Count(e => e.Errored),
            "device=phone" => events.Count(e => e.Device == Constants.AnswerDevice),
            "region=US-CA" => events.Count(e => e.RegionIso == Constants.AnswerRegion),
            "app_version=2.3.1" => events.Count(e => e.AppVersion == Constants.AnswerVersion),
            "lunch hour" => events.Count(e => e.LocalHour >= Constants.LunchHourStart && e.LocalHour < Constants.LunchHourEnd),
            _ => throw new ArgumentException(name),
        };

        var share = n / total;
        Assert.True(share <= wantBelow, $"{name} matches {share * 100:F0}% of events; too broad");
        Assert.True(share >= wantAtLeast, $"{name} matches only {share * 100:F1}% of events; too rare");
    }

    [Fact]
    public void GenerateWithinWindow()
    {
        var (cfg, events) = Generate();
        var oldest = cfg.Now - cfg.Window;

        foreach (var e in events)
        {
            Assert.True(e.Start >= oldest && e.Start <= cfg.Now,
                $"event at {e.Start} falls outside the {cfg.Window} window ending {cfg.Now}");
        }
    }
}
