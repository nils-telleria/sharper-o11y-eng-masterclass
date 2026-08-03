using Scenario;

namespace SeedCanaryRegression.Tests;

public class ScenarioTests
{
    private static (Config cfg, Request[] reqs) Generate()
    {
        var cfg = Config.Default with
        {
            Now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero),
        };
        return (cfg, ScenarioGenerator.Generate(cfg, new Random(42)));
    }

    // TestGenerate_AffectedPopulationIsDemoSized is the load-bearing test.
    // The demo only works inside a narrow band: enough slow requests to draw
    // a BubbleUp box around, few enough that aggregate percentiles stay quiet.
    [Fact]
    public void Generate_AffectedPopulationIsDemoSized()
    {
        var (_, reqs) = Generate();
        var regressed = reqs.Count(r => r.Regressed);

        Assert.True(regressed >= 25, $"only {regressed} regressed requests; too few to isolate on a heatmap");

        var share = (double)regressed / reqs.Length;
        Assert.True(share <= 0.02,
            $"regressed share {share:F3} exceeds 2%; the regression stops being an aggregate blind spot");
    }

    // TestGenerate_InvisibleInAggregate asserts the actual pedagogical claim: the
    // median across all traffic is unmoved by the regression. This is what lets
    // the session say "your dashboard would not have caught this."
    [Fact]
    public void Generate_InvisibleInAggregate()
    {
        var (cfg, reqs) = Generate();
        var deploy = cfg.DeployTime;

        var before = reqs.Where(r => r.Start <= deploy).Select(r => r.Duration).ToList();
        var after = reqs.Where(r => r.Start > deploy).Select(r => r.Duration).ToList();

        var p50Before = Percentile(before, 0.50);
        var p50After = Percentile(after, 0.50);
        var drift = RelDelta(p50Before, p50After);

        Assert.True(drift <= 0.05,
            $"overall P50 moved {drift * 100:F1}% across the deploy ({p50Before} -> {p50After}); " +
            "regression is leaking into the aggregate");

        // The affected cohort must show a clear signal.
        var cohortBefore = reqs
            .Where(r => r.Route == Route.Billing && r.UserType == "enterprise" && r.Version == cfg.BaselineVersion)
            .Select(r => r.Duration).ToList();
        var cohortAfter = reqs
            .Where(r => r.Route == Route.Billing && r.UserType == "enterprise" && r.Version == cfg.CanaryVersion)
            .Select(r => r.Duration).ToList();

        if (cohortBefore.Count > 0 && cohortAfter.Count > 0)
        {
            var cohortDrift = RelDelta(Percentile(cohortBefore, 0.50), Percentile(cohortAfter, 0.50));
            Assert.True(cohortDrift >= 0.15,
                $"enterprise-on-billing P50 only moved {cohortDrift * 100:F1}% between builds; too subtle to demo");
        }
    }

    // TestGenerate_RequestIsFortyPercentSlower checks the book's actual number.
    [Fact]
    public void Generate_RequestIsFortyPercentSlower()
    {
        var (cfg, reqs) = Generate();

        var regressed = reqs.Where(r => r.Route == Route.Billing && r.UserType == "enterprise" && r.Regressed)
            .Select(r => r.Duration).ToList();
        var healthy = reqs.Where(r => r.Route == Route.Billing && r.UserType == "enterprise" && !r.Regressed && r.Version == cfg.BaselineVersion)
            .Select(r => r.Duration).ToList();

        if (regressed.Count == 0 || healthy.Count == 0) return;

        var got = RelDelta(Percentile(healthy, 0.50), Percentile(regressed, 0.50));
        var want = cfg.RegressionFactor - 1;

        Assert.InRange(got, want * 0.8, want * 1.2);
    }

    private static TimeSpan Percentile(List<TimeSpan> ds, double p)
    {
        if (ds.Count == 0) return TimeSpan.Zero;
        var sorted = ds.OrderBy(x => x).ToList();
        return sorted[(int)(p * (sorted.Count - 1))];
    }

    private static double RelDelta(TimeSpan a, TimeSpan b)
    {
        if (a == TimeSpan.Zero) return 0;
        return Math.Abs((b - a).TotalMilliseconds / a.TotalMilliseconds);
    }
}
