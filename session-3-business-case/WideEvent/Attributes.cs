// Attributes is the canonical mapping from an Event to the OTel tag key-value
// pairs that reach Honeycomb. It lives here rather than in the seeder so the
// width of the event — the whole premise of the Arbitrary Question Test — is
// testable without standing up an exporter.

using System.Diagnostics;

namespace WideEvent;

public static class EventAttributes
{
    // Apply writes all of an event's attributes onto an Activity.
    public static void Apply(Activity activity, Event e)
    {
        // Request and execution flow (Table 6-8, 6-11)
        activity.SetTag("http.request.method", "POST");
        activity.SetTag("http.route", e.Route);
        activity.SetTag("url.path", e.UrlPath);
        activity.SetTag("http.response.status_code", e.StatusCode);
        activity.SetTag("db.query.duration_ms", e.DurationDb);
        activity.SetTag("request.local_hour", e.LocalHour);

        // User and business context (Table 6-15)
        activity.SetTag("user.id", e.UserId);
        activity.SetTag("user.type", e.UserType);
        activity.SetTag("user.org.id", e.UserOrgId);
        activity.SetTag("user.auth_method", e.AuthMethod);

        // Client (Tables 6-9, 6-10)
        activity.SetTag("user_agent.device", e.Device);
        activity.SetTag("user_agent.OS", e.OsName);

        // Geography (OTel semconv, Development stability)
        activity.SetTag("geo.country.iso_code", e.CountryIso);
        activity.SetTag("geo.region.iso_code", e.RegionIso);

        // Localization (Table 6-18)
        activity.SetTag("localization.language", e.Language);
        activity.SetTag("localization.currency", e.Currency);

        // Service and code context (Tables 6-1, 6-5, 6-6)
        activity.SetTag("service.version", e.ServiceVersion);
        activity.SetTag("service.environment", e.ServiceEnv);
        activity.SetTag("service.build.git_hash", e.BuildGitHash);
        activity.SetTag("service.build.deployment.age_minutes", e.DeployAgeMin);
        activity.SetTag("feature_flag.new_checkout_flow", e.FeatureFlagNew);

        // Rate limits (Table 6-16)
        activity.SetTag("ratelimit.limit", e.RateLimitLimit);
        activity.SetTag("ratelimit.remaining", e.RateLimitRemaining);

        // Async request summaries (Table 6-13)
        activity.SetTag("stats.postgres_query_count", e.PostgresQueryCount);
        activity.SetTag("stats.redis_query_count", e.RedisQueryCount);

        // Infrastructure (Table 6-2, 6-3)
        activity.SetTag("instance.type", e.InstanceType);
        activity.SetTag("cloud.region", e.CloudRegion);

        // Only mobile clients report an app and version.
        if (e.App != "")
        {
            activity.SetTag("user_agent.app", e.App);
            activity.SetTag("user_agent.app_version", e.AppVersion);
        }

        // Errors (Table 6-14). error is a literal boolean.
        if (e.Errored)
        {
            activity.SetTag("error", true);
            activity.SetTag("error.type", e.ErrorType);
            activity.SetTag("exception.slug", e.ErrorSlug);
        }
    }

    // Count returns the number of attributes Apply would set for a given event.
    public static int Count(Event e)
    {
        // Base attributes always present.
        int count = 24;
        // App + version (mobile only)
        if (e.App != "") count += 2;
        // Error fields
        if (e.Errored) count += 3;
        return count;
    }
}
