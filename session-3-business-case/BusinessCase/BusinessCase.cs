// BusinessCase turns an organisation's own numbers into the one-page argument
// Masterclass 3's lab asks for.
//
// Why this is not an ROI calculator
//
// Chapter 26 gives cited public figures for the link between latency and revenue,
// and then says plainly that "turning this into monetary estimates is harder to
// give guidance on." So this package does three things instead:
//
//   - Computes the cost of the current state from numbers a leader can look up.
//   - Shows the arithmetic for every line.
//   - Reports a range across three explicitly-labelled improvement assumptions.

namespace BusinessCase;

// FormatMoney renders a whole-dollar amount with thousands separators.
public static class Money
{
    public static string Format(double v)
    {
        var neg = v < 0;
        if (neg) v = -v;
        var digits = ((long)v).ToString();
        var sb = new System.Text.StringBuilder();
        if (neg) sb.Append('-');
        for (int i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append(',');
            sb.Append(digits[i]);
        }
        return sb.ToString();
    }
}

// Inputs are the numbers a leader supplies.
public record Inputs
{
    public int Engineers { get; init; }
    public double LoadedCostPerEngineer { get; init; }
    public double IncidentsPerMonth { get; init; }
    public double MeanTimeToResolveHours { get; init; }
    public double RespondersPerIncident { get; init; }
    public double EscalationRate { get; init; }
    public double EscalationExtraResponders { get; init; }
    public double UnplannedWorkShare { get; init; }
    public double RevenuePerHourAtRisk { get; init; }
    public double ObservabilityAnnualSpend { get; init; }

    // WorkingHoursPerYear: 40h × 52w, conservative (ignores leave).
    public const double WorkingHoursPerYear = 2080;

    public double HourlyRate =>
        LoadedCostPerEngineer == 0 ? 0 : LoadedCostPerEngineer / WorkingHoursPerYear;

    public double IncidentsPerYear => IncidentsPerMonth * 12;
}

public record LineItem(string Label, string Formula, double Annual);

public record Scenario
{
    public string Name { get; init; } = "";
    public double MTTRReduction { get; init; }
    public double UnplannedWorkReduction { get; init; }
    public double AnnualSaving { get; init; }
}

public record Case(Inputs Inputs, LineItem[] CurrentState, double CurrentTotal, Scenario[] Scenarios)
{
    public double NetOf(Scenario s) => s.AnnualSaving - Inputs.ObservabilityAnnualSpend;
}

public static class CaseBuilder
{
    public static Scenario[] DefaultScenarios() =>
    [
        new() { Name = "Conservative", MTTRReduction = 0.15, UnplannedWorkReduction = 0.05 },
        new() { Name = "Moderate",     MTTRReduction = 0.35, UnplannedWorkReduction = 0.15 },
        new() { Name = "Optimistic",   MTTRReduction = 0.60, UnplannedWorkReduction = 0.25 },
    ];

    public static Case Build(Inputs inputs, Scenario[]? scenarios = null)
    {
        scenarios ??= DefaultScenarios();

        var hourly = inputs.HourlyRate;
        var incidents = inputs.IncidentsPerYear;

        var responderHours = incidents * inputs.MeanTimeToResolveHours * inputs.RespondersPerIncident;
        var escalationHours = incidents * inputs.EscalationRate * inputs.EscalationExtraResponders * inputs.MeanTimeToResolveHours;

        var items = new List<LineItem>
        {
            new("Incident response labour",
                $"{incidents:F0} incidents/yr x {inputs.MeanTimeToResolveHours:F1}h x {inputs.RespondersPerIncident:F1} responders x ${Money.Format(hourly)}/h",
                responderHours * hourly),
            new("Escalation drag",
                $"{incidents:F0} incidents/yr x {inputs.EscalationRate * 100:F0}% escalated x {inputs.EscalationExtraResponders:F1} extra people x {inputs.MeanTimeToResolveHours:F1}h x ${Money.Format(hourly)}/h",
                escalationHours * hourly),
            new("Unplanned work and rework",
                $"{inputs.Engineers} engineers x ${Money.Format(inputs.LoadedCostPerEngineer)} loaded x {inputs.UnplannedWorkShare * 100:F0}% of time",
                inputs.Engineers * inputs.LoadedCostPerEngineer * inputs.UnplannedWorkShare),
        };

        if (inputs.RevenuePerHourAtRisk > 0)
            items.Add(new("Revenue exposure during degradation",
                $"{incidents:F0} incidents/yr x {inputs.MeanTimeToResolveHours:F1}h x ${Money.Format(inputs.RevenuePerHourAtRisk)}/h at risk",
                incidents * inputs.MeanTimeToResolveHours * inputs.RevenuePerHourAtRisk));

        var total = items.Sum(it => it.Annual);

        // Only time-to-resolve-sensitive lines shrink with MTTR improvements.
        var mttrSensitive = items
            .Where(it => it.Label is "Incident response labour" or "Escalation drag" or "Revenue exposure during degradation")
            .Sum(it => it.Annual);
        var unplanned = inputs.Engineers * inputs.LoadedCostPerEngineer * inputs.UnplannedWorkShare;

        var outScenarios = scenarios.Select(s => s with
        {
            AnnualSaving = mttrSensitive * s.MTTRReduction + unplanned * s.UnplannedWorkReduction,
        }).ToArray();

        return new Case(inputs, items.ToArray(), total, outScenarios);
    }
}
