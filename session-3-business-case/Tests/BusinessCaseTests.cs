using BusinessCase;

namespace Session3.Tests;

public class BusinessCaseTests
{
    // sample is a mid-sized org with round numbers, checkable by hand.
    private static Inputs Sample() => new()
    {
        Engineers = 100,
        LoadedCostPerEngineer = 260000, // $125/h at 2080h
        IncidentsPerMonth = 10,          // 120/yr
        MeanTimeToResolveHours = 4,
        RespondersPerIncident = 3,
        EscalationRate = 0.5,
        EscalationExtraResponders = 2,
        UnplannedWorkShare = 0.20,
        RevenuePerHourAtRisk = 0,
        ObservabilityAnnualSpend = 400000,
    };

    [Fact]
    public void HourlyRate()
    {
        Assert.Equal(125.0, Sample().HourlyRate);
    }

    [Fact]
    public void LineItemArithmetic()
    {
        var c = CaseBuilder.Build(Sample());
        var byLabel = c.CurrentState.ToDictionary(it => it.Label, it => it.Annual);

        // 120 incidents x 4h x 3 people x $125 = 180,000
        Assert.Equal(180000, byLabel["Incident response labour"]);
        // 120 x 0.5 x 2 extra x 4h x $125 = 60,000
        Assert.Equal(60000, byLabel["Escalation drag"]);
        // 100 engineers x $260k x 20% = 5,200,000
        Assert.Equal(5200000, byLabel["Unplanned work and rework"]);
        Assert.Equal(5440000.0, c.CurrentTotal);
    }

    [Fact]
    public void RevenueLineIsOptional()
    {
        var withoutRevenue = CaseBuilder.Build(Sample());
        Assert.DoesNotContain(withoutRevenue.CurrentState, it => it.Label == "Revenue exposure during degradation");

        var withRevenue = CaseBuilder.Build(Sample() with { RevenuePerHourAtRisk = 10000 });
        var rev = withRevenue.CurrentState.Single(it => it.Label == "Revenue exposure during degradation");
        // 120 incidents x 4h x $10,000 = 4,800,000
        Assert.Equal(4800000.0, rev.Annual);
    }

    [Fact]
    public void ScenarioSavingsSeparateTheLevers()
    {
        var inputs = Sample();

        var onlyMTTR = CaseBuilder.Build(inputs, [new() { Name = "mttr only", MTTRReduction = 0.5, UnplannedWorkReduction = 0 }]);
        var onlyUnplanned = CaseBuilder.Build(inputs, [new() { Name = "unplanned only", MTTRReduction = 0, UnplannedWorkReduction = 0.5 }]);
        var both = CaseBuilder.Build(inputs, [new() { Name = "both", MTTRReduction = 0.5, UnplannedWorkReduction = 0.5 }]);

        // MTTR-sensitive lines total 240,000; half is 120,000.
        Assert.Equal(120000.0, onlyMTTR.Scenarios[0].AnnualSaving);
        // Unplanned is 5,200,000; half is 2,600,000.
        Assert.Equal(2600000.0, onlyUnplanned.Scenarios[0].AnnualSaving);
        Assert.Equal(2720000.0, both.Scenarios[0].AnnualSaving);
    }

    [Fact]
    public void DefaultScenariosAreOrderedAndConservativeFirst()
    {
        var c = CaseBuilder.Build(Sample());
        Assert.Equal(3, c.Scenarios.Length);

        for (int i = 1; i < c.Scenarios.Length; i++)
            Assert.True(c.Scenarios[i].AnnualSaving > c.Scenarios[i - 1].AnnualSaving,
                $"scenario '{c.Scenarios[i].Name}' does not exceed '{c.Scenarios[i - 1].Name}'");

        var spread = c.Scenarios[2].AnnualSaving / c.Scenarios[0].AnnualSaving;
        Assert.True(spread >= 2, $"optimistic is only {spread:F1}x conservative; the range is too narrow");
    }

    [Fact]
    public void NetOfCanBeNegative()
    {
        var inputs = new Inputs
        {
            Engineers = 5,
            IncidentsPerMonth = 1,
            UnplannedWorkShare = 0.02,
            ObservabilityAnnualSpend = 500000,
        };
        var c = CaseBuilder.Build(inputs);
        Assert.True(c.NetOf(c.Scenarios[0]) < 0,
            "conservative net should be negative for a tiny org on a large contract");
    }

    [Fact]
    public void ZeroInputsDoNotPanic()
    {
        var c = CaseBuilder.Build(new Inputs());
        Assert.Equal(0, c.CurrentTotal);
        foreach (var s in c.Scenarios)
            Assert.False(double.IsNaN(s.AnnualSaving) || double.IsInfinity(s.AnnualSaving));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(7, "7")]
    [InlineData(999, "999")]
    [InlineData(1000, "1,000")]
    [InlineData(1234, "1,234")]
    [InlineData(999999, "999,999")]
    [InlineData(1000000, "1,000,000")]
    [InlineData(5440000, "5,440,000")]
    [InlineData(-104000, "-104,000")]
    [InlineData(-1000, "-1,000")]
    [InlineData(-7, "-7")]
    public void FormatMoney(double input, string expected)
    {
        Assert.Equal(expected, Money.Format(input));
    }
}
