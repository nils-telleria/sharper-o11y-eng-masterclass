// WideEvent generates the Masterclass 3 dataset: genuinely wide checkout events
// for the Arbitrary Question Test.
//
// Chapter 28's test gives a specific question:
//
//   "Show me all failed checkout attempts from mobile users in California
//    using version 2.3.1 of the app during lunch hour over the past week."
//
// Events here carry ~30 attributes drawn from Chapter 6's tables, of which the
// question happens to use four. The claim being demonstrated is not "we have the
// right fields," it is "we kept everything, so the question is answerable without
// having predicted it."

namespace WideEvent;

// LunchHourStart/End bound "lunch hour" in the user's local time.
public static class Constants
{
    public const int LunchHourStart = 12;
    public const int LunchHourEnd = 14;
    public const string AnswerVersion = "2.3.1";
    public const string AnswerDevice = "phone";
    public const string AnswerRegion = "US-CA";
}

public record Config
{
    public int EventCount { get; init; }
    public TimeSpan Window { get; init; }
    public DateTimeOffset Now { get; init; }

    public static Config Default => new()
    {
        EventCount = 20000,
        Window = TimeSpan.FromDays(7),
        Now = DateTimeOffset.UtcNow,
    };
}

// Event is one checkout attempt, fully resolved.
public record Event
{
    public DateTimeOffset Start { get; init; }
    public TimeSpan Duration { get; init; }
    public int LocalHour { get; init; }

    // Outcome
    public int StatusCode { get; init; }
    public bool Errored { get; init; }
    public string ErrorType { get; init; } = "";
    public string ErrorSlug { get; init; } = "";

    // Request and execution
    public string Route { get; init; } = "";
    public string UrlPath { get; init; } = "";
    public string Method { get; init; } = "";
    public long DurationDb { get; init; }

    // Identity and business context
    public string UserId { get; init; } = "";
    public string UserType { get; init; } = "";
    public string UserOrgId { get; init; } = "";
    public string AuthMethod { get; init; } = "";

    // Client
    public string Device { get; init; } = "";
    public string App { get; init; } = "";
    public string AppVersion { get; init; } = "";
    public string OsName { get; init; } = "";

    // Geography
    public string CountryIso { get; init; } = "";
    public string RegionIso { get; init; } = "";

    // Localization
    public string Language { get; init; } = "";
    public string Currency { get; init; } = "";

    // Service and code context
    public string ServiceVersion { get; init; } = "";
    public string ServiceEnv { get; init; } = "";
    public string BuildGitHash { get; init; } = "";
    public long DeployAgeMin { get; init; }
    public bool FeatureFlagNew { get; init; }

    // Operational
    public long RateLimitLimit { get; init; }
    public long RateLimitRemaining { get; init; }
    public long PostgresQueryCount { get; init; }
    public long RedisQueryCount { get; init; }
    public string InstanceType { get; init; } = "";
    public string CloudRegion { get; init; } = "";

    public bool MatchesArbitraryQuestion() =>
        Errored
        && Device == Constants.AnswerDevice
        && RegionIso == Constants.AnswerRegion
        && AppVersion == Constants.AnswerVersion
        && LocalHour >= Constants.LunchHourStart
        && LocalHour < Constants.LunchHourEnd;
}

public static class EventGenerator
{
    public static Event[] Generate(Config cfg, Random rng) =>
        Enumerable.Range(0, cfg.EventCount).Select(_ => GenerateOne(cfg, rng)).ToArray();

    private static Event GenerateOne(Config cfg, Random rng)
    {
        var start = StartTime(cfg, rng);
        var device = Weighted(rng, [("phone", 0.55), ("computer", 0.35), ("tablet", 0.10)]);
        var (app, appVersion, osName) = ClientFor(device, rng);
        var (country, region) = GeoFor(rng);
        var userType = Weighted(rng, [("free", 0.60), ("premium", 0.30), ("enterprise", 0.10)]);
        var (errored, errType, errSlug) = FailureFor(rng);
        var dbDuration = (long)(20 + rng.Next(120));
        var duration = TimeSpan.FromMilliseconds(60 + rng.Next(400));

        return new Event
        {
            Start = start,
            LocalHour = start.Hour,
            Duration = duration,
            StatusCode = errored ? 402 : 201,
            Errored = errored,
            ErrorType = errType,
            ErrorSlug = errSlug,
            Route = "/api/checkout",
            UrlPath = "/api/checkout",
            Method = "POST",
            DurationDb = dbDuration,
            UserId = $"user_{rng.Next(20000)}",
            UserType = userType,
            UserOrgId = $"org_{rng.Next(800)}",
            AuthMethod = Weighted(rng, [("token", 0.6), ("sso-github", 0.25), ("basic-auth", 0.15)]),
            Device = device,
            App = app,
            AppVersion = appVersion,
            OsName = osName,
            CountryIso = country,
            RegionIso = region,
            Language = Weighted(rng, [("en-US", 0.7), ("es-MX", 0.15), ("fr-CA", 0.1), ("de-DE", 0.05)]),
            Currency = Weighted(rng, [("USD", 0.8), ("CAD", 0.1), ("EUR", 0.1)]),
            ServiceVersion = "1.4.2",
            ServiceEnv = "production",
            BuildGitHash = "6f6466b0e693470729b669f3745358df29f97e8d",
            DeployAgeMin = (long)(30 + rng.Next(4000)),
            FeatureFlagNew = rng.NextDouble() < 0.25,
            RateLimitLimit = 200000,
            RateLimitRemaining = (long)rng.Next(200000),
            PostgresQueryCount = (long)(3 + rng.Next(12)),
            RedisQueryCount = (long)(1 + rng.Next(8)),
            InstanceType = Weighted(rng, [("m6i.xlarge", 0.6), ("m7g.xlarge", 0.4)]),
            CloudRegion = Weighted(rng, [("us-east-1", 0.5), ("us-west-2", 0.3), ("eu-west-1", 0.2)]),
        };
    }

    private static DateTimeOffset StartTime(Config cfg, Random rng)
    {
        var base_ = cfg.Now - TimeSpan.FromTicks((long)(rng.NextDouble() * cfg.Window.Ticks));
        if (rng.NextDouble() >= 0.20) return base_;

        var hour = Constants.LunchHourStart + rng.Next(Constants.LunchHourEnd - Constants.LunchHourStart);
        var lunch = new DateTimeOffset(base_.Year, base_.Month, base_.Day, hour, rng.Next(60), rng.Next(60), 0, base_.Offset);

        var oldest = cfg.Now - cfg.Window;
        if (lunch < oldest) lunch = lunch.AddDays(1);
        else if (lunch > cfg.Now) lunch = lunch.AddDays(-1);
        if (lunch < oldest || lunch > cfg.Now) return base_;
        return lunch;
    }

    private static (string app, string appVersion, string osName) ClientFor(string device, Random rng)
    {
        if (device == "computer")
            return ("", "", Weighted(rng, [("macOS", 0.5), ("Windows", 0.4), ("Linux", 0.1)]));

        var app = Weighted(rng, [("iOS", 0.6), ("android", 0.4)]);
        var appVersion = Weighted(rng, [("2.3.1", 0.35), ("2.3.0", 0.25), ("2.2.4", 0.20), ("2.4.0-beta", 0.20)]);
        return app == "iOS" ? (app, appVersion, "iOS") : (app, appVersion, "Android");
    }

    private static (string country, string region) GeoFor(Random rng) =>
        rng.NextDouble() switch
        {
            < 0.28 => ("US", "US-CA"),
            < 0.44 => ("US", "US-NY"),
            < 0.56 => ("US", "US-TX"),
            < 0.66 => ("US", "US-WA"),
            < 0.74 => ("US", "US-IL"),
            < 0.86 => ("CA", "CA-ON"),
            < 0.94 => ("GB", "GB-ENG"),
            _ => ("DE", "DE-BE"),
        };

    private static (bool errored, string errType, string errSlug) FailureFor(Random rng) =>
        rng.NextDouble() switch
        {
            < 0.055 => (true, "payment_declined", "err-stripe-card-declined"),
            < 0.075 => (true, "payment_method_expired", "err-stripe-card-expired"),
            < 0.085 => (true, "inventory_unavailable", "err-inventory-oversold"),
            _ => (false, "", ""),
        };

    private static string Weighted(Random rng, (string value, double weight)[] choices)
    {
        var r = rng.NextDouble();
        double cum = 0;
        foreach (var (value, weight) in choices)
        {
            cum += weight;
            if (r < cum) return value;
        }
        return choices[^1].value;
    }
}
