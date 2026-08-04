// business-case prints the one-page argument Masterclass 3's lab asks you to
// build, from your own numbers.
//
//   dotnet run -- \
//     --engineers 120 --loaded-cost 260000 \
//     --incidents-per-month 14 --mttr-hours 3.5 --responders 3 \
//     --escalation-rate 0.45 --escalation-extra 2 \
//     --unplanned-share 0.22 --observability-spend 350000
//
// Run with no arguments to see the defaults and a warning that they are
// placeholders.

using BusinessCase;

bool showSources = false;
var inputs = new Inputs();

for (int i = 0; i < args.Length; i++)
{
    inputs = args[i] switch
    {
        "--engineers" => inputs with { Engineers = int.Parse(args[++i]) },
        "--loaded-cost" => inputs with { LoadedCostPerEngineer = double.Parse(args[++i]) },
        "--incidents-per-month" => inputs with { IncidentsPerMonth = double.Parse(args[++i]) },
        "--mttr-hours" => inputs with { MeanTimeToResolveHours = double.Parse(args[++i]) },
        "--responders" => inputs with { RespondersPerIncident = double.Parse(args[++i]) },
        "--escalation-rate" => inputs with { EscalationRate = double.Parse(args[++i]) },
        "--escalation-extra" => inputs with { EscalationExtraResponders = double.Parse(args[++i]) },
        "--unplanned-share" => inputs with { UnplannedWorkShare = double.Parse(args[++i]) },
        "--revenue-per-hour" => inputs with { RevenuePerHourAtRisk = double.Parse(args[++i]) },
        "--observability-spend" => inputs with { ObservabilityAnnualSpend = double.Parse(args[++i]) },
        "--sources" => (showSources = true) ? inputs : inputs,
        _ => inputs,
    };
}

if (showSources) { PrintSources(); return; }

var usingPlaceholders = inputs.Engineers == 0;
if (usingPlaceholders)
    inputs = Placeholders();

var c = CaseBuilder.Build(inputs);
Print(c, usingPlaceholders);

static Inputs Placeholders() => new()
{
    Engineers = 100,
    LoadedCostPerEngineer = 260000,
    IncidentsPerMonth = 10,
    MeanTimeToResolveHours = 4,
    RespondersPerIncident = 3,
    EscalationRate = 0.5,
    EscalationExtraResponders = 2,
    UnplannedWorkShare = 0.20,
    ObservabilityAnnualSpend = 400000,
};

static void Print(Case c, bool usingPlaceholders)
{
    if (usingPlaceholders)
        Console.WriteLine("""

┌───────────────────────────────────────────────────────────────────────────┐
│  THESE ARE PLACEHOLDER NUMBERS. Do not present this.                      │
│  Rerun with your own --engineers, --incidents-per-month, --mttr-hours,    │
│  and --unplanned-share. The whole point of the exercise is that the       │
│  figures are yours and therefore hard to dismiss.                         │
└───────────────────────────────────────────────────────────────────────────┘
""");

    Console.WriteLine($"\nCOST OF THE CURRENT STATE\n{new string('=', 75)}");
    foreach (var it in c.CurrentState)
    {
        Console.WriteLine($"\n  {it.Label,-38} ${Money.Format(it.Annual)} / yr");
        Console.WriteLine($"  {it.Formula}");
    }
    Console.WriteLine($"\n  {"TOTAL",-38} ${Money.Format(c.CurrentTotal)} / yr");

    Console.WriteLine($"\n\nWHAT IMPROVEMENT WOULD BE WORTH\n{new string('=', 75)}");
    Console.WriteLine("""

  The percentages below are assumptions you are choosing, not findings. Say so
  out loud when you present this. A case that only works at its optimistic end
  is not a case.

""");
    Console.WriteLine($"  {"Scenario",-14} {"Assumes",-22} {"Annual saving",14} {"Net of spend",14}");
    Console.WriteLine($"  {new string('-', 68)}");
    foreach (var s in c.Scenarios)
    {
        var assumes = $"-{s.MTTRReduction * 100:F0}% MTTR, -{s.UnplannedWorkReduction * 100:F0}% rework";
        Console.WriteLine($"  {s.Name,-14} {assumes,-22} {"$" + Money.Format(s.AnnualSaving),14} {"$" + Money.Format(c.NetOf(s)),14}");
    }

    if (c.Inputs.ObservabilityAnnualSpend > 0)
        Console.WriteLine($"\n  Current observability spend: ${Money.Format(c.Inputs.ObservabilityAnnualSpend)} / yr");

    if (c.Inputs.RevenuePerHourAtRisk == 0)
        Console.WriteLine("""

  No revenue-at-risk figure was supplied, so this case rests on engineering
  cost alone. That is usually the stronger ground: it is auditable, and nobody
  has to agree with your model of customer behaviour to accept it.
""");

    Console.WriteLine($"\nWHAT THIS DOES NOT DO\n{new string('=', 75)}\n");
    Console.WriteLine("""
  It does not tell you whether observability caused an improvement. That is
  what the five tests in Chapter 28 are for, and why you rerun them after the
  work rather than declaring victory from a spreadsheet.

  Run 'dotnet run -- --sources' for the public figures Chapter 26 cites on the
  link between latency and revenue.
""");
}

static void PrintSources()
{
    Console.WriteLine("""

PUBLIC FIGURES CITED IN CHAPTER 26
===========================================================================

These are the numbers the book actually cites for the link between latency
and business outcomes. Use them as evidence that the relationship is real and
measurable at scale — not as a multiplier to apply to your own revenue.

  Amazon    every 100ms of added page load time cost ~1% in revenue
            Steven van Vessum, Conductor, June 14 2023

  Walmart   ~2% increase in conversions for every second shaved off load time
            Cedric Moore, Trueform, February 10 2025

  Staples   ~10% increase in conversions from a 1s home page improvement
            Martyna Kozlowska, Reffine

Chapter 26 is explicit that "turning this into monetary estimates is harder to
give guidance on." Quoting someone else's conversion lift as though it were
your forecast is how a business case loses a room. Cite these to establish
that latency has measurable financial consequences, then argue your own case
from your own numbers.

""");
}
