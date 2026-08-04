// Package Scenario generates the Masterclass 2 canary-regression dataset.
//
// The scenario
//
// This reproduces the worked example from Chapter 2, Practice 5 (p.33):
//
//   "You can see that the new build is 40% slower for users on the enterprise
//    plan hitting the billing endpoint. That signal is available at 1%. By the
//    time it's moving aggregate metrics, it's already affecting a lot of people."
//
// A deploy happens partway through the window. After it, a small canary slice of
// traffic runs service.version 1.5.0 instead of 1.4.2. On the canary, and only
// for enterprise users, and only on /api/billing, requests are ~40% slower.
// Nothing else changes.
//
// Why these proportions
//
// The affected population is the product of three independent filters, so the
// numbers have to be chosen deliberately or the regression rounds away to
// nothing. With the defaults below and 10,000 traces over a 4h window with the
// deploy 90m ago:
//
//   post-deploy traces       10000 * (90/240)      ~= 3750
//   ... on /api/billing      * 0.30                ~= 1125
//   ... enterprise           * 0.45 (billing-only) ~=  506
//   ... on the canary        * 0.10                ~=   50
//
// ~50 slow requests against 10,000 total. That is enough to draw a BubbleUp
// box around on a heatmap, while still being only ~0.5% of all traffic — so
// P50 is flat and P99 is barely perturbed. That gap between "invisible in
// aggregate" and "obvious in BubbleUp" IS the lesson.

namespace Scenario;

// Config describes one generated dataset.
public record Config
{
    public int TraceCount { get; init; }
    public TimeSpan Window { get; init; }
    public TimeSpan DeployAgo { get; init; }
    public DateTimeOffset Now { get; init; }
    public string BaselineVersion { get; init; } = "";
    public string CanaryVersion { get; init; } = "";
    public double CanaryShare { get; init; }
    public double RegressionFactor { get; init; }

    public DateTimeOffset DeployTime => Now - DeployAgo;

    public static Config Default => new()
    {
        TraceCount = 10000,
        Window = TimeSpan.FromHours(4),
        DeployAgo = TimeSpan.FromMinutes(90),
        BaselineVersion = "1.4.2",
        CanaryVersion = "1.5.0",
        CanaryShare = 0.10,
        RegressionFactor = 1.4,
    };
}

public enum Route { Checkout, Billing }

// Request is one generated root request, fully resolved.
public record Request
{
    public DateTimeOffset Start { get; init; }
    public Route Route { get; init; }
    public string UserType { get; init; } = "";
    public int UserId { get; init; }
    public string Version { get; init; } = "";
    public bool Regressed { get; init; }
    public bool Errored { get; init; }
    public string ErrorType { get; init; } = "";
    public Step[] Steps { get; init; } = Array.Empty<Step>();

    public string RoutePath => Route == Route.Billing ? "/api/billing" : "/api/checkout";

    public int StatusCode() => Errored ? 402 : (Route == Route.Billing ? 200 : 201);

    public TimeSpan Duration => Steps.Aggregate(TimeSpan.Zero, (acc, s) => acc + s.Duration);
}

public record Step(string Name, TimeSpan Duration);

public static class ScenarioGenerator
{
    public static Request[] Generate(Config cfg, Random rng)
    {
        var deploy = cfg.DeployTime;
        var results = new List<Request>(cfg.TraceCount);

        for (int i = 0; i < cfg.TraceCount; i++)
        {
            var start = cfg.Now - TimeSpan.FromTicks((long)(rng.NextDouble() * cfg.Window.Ticks));
            var route = rng.NextDouble() < 0.30 ? Route.Billing : Route.Checkout;
            var userType = UserTypeFor(route, rng);
            var version = cfg.BaselineVersion;
            var onCanary = false;

            if (start > deploy && rng.NextDouble() < cfg.CanaryShare)
            {
                version = cfg.CanaryVersion;
                onCanary = true;
            }

            // The regression rule: canary build AND enterprise plan AND billing.
            // Any two of the three is not enough.
            var regressed = onCanary && route == Route.Billing && userType == "enterprise";
            var factor = regressed ? cfg.RegressionFactor : 1.0;
            var (errored, errorType) = FailureFor(route, rng);

            results.Add(new Request
            {
                Start = start,
                Route = route,
                UserType = userType,
                UserId = rng.Next(5000),
                Version = version,
                Regressed = regressed,
                Errored = errored,
                ErrorType = errorType,
                Steps = StepsFor(route, factor, rng),
            });
        }

        return results.ToArray();
    }

    private static (bool errored, string errorType) FailureFor(Route route, Random rng)
    {
        if (route == Route.Billing)
            return rng.NextDouble() < 0.02 ? (true, "payment_method_expired") : (false, "");
        return rng.NextDouble() < 0.07 ? (true, "payment_declined") : (false, "");
    }

    private static string UserTypeFor(Route route, Random rng)
    {
        var r = rng.NextDouble();
        if (route == Route.Billing)
            return r < 0.20 ? "free" : r < 0.55 ? "premium" : "enterprise";
        return r < 0.65 ? "free" : r < 0.92 ? "premium" : "enterprise";
    }

    private static Step[] StepsFor(Route route, double factor, Random rng)
    {
        Step[] steps;
        int slowStep;

        if (route == Route.Billing)
        {
            steps = new[]
            {
                new Step("auth", Jitter(5, 10, rng)),
                new Step("load_account", Jitter(18, 28, rng)),
                new Step("render_invoice", Jitter(50, 68, rng)),
                new Step("emit_receipt", Jitter(4, 8, rng)),
            };
            slowStep = 2;
        }
        else
        {
            steps = new[]
            {
                new Step("auth", Jitter(5, 15, rng)),
                new Step("inventory_check", Jitter(10, 30, rng)),
                new Step("payment", Jitter(20, 60, rng)),
                new Step("confirmation", Jitter(5, 10, rng)),
            };
            slowStep = -1;
        }

        if (factor > 1 && slowStep >= 0)
        {
            var baseTotal = steps.Aggregate(TimeSpan.Zero, (acc, s) => acc + s.Duration);
            var extra = TimeSpan.FromTicks((long)(baseTotal.Ticks * (factor - 1)));
            steps[slowStep] = steps[slowStep] with { Duration = steps[slowStep].Duration + extra };
        }

        return steps;
    }

    private static TimeSpan Jitter(int minMs, int maxMs, Random rng) =>
        TimeSpan.FromMilliseconds(minMs + rng.Next(maxMs - minMs));
}
