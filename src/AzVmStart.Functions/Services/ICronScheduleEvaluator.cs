namespace AzVmStart.Functions.Services;

/// <summary>
/// Evaluates whether a cron-based schedule is due within a given UTC time window,
/// interpreting the cron expression in a specific time zone.
/// </summary>
public interface ICronScheduleEvaluator
{
    /// <summary>
    /// Returns <c>true</c> when the supplied 5-field cron expression has at least
    /// one occurrence within the half-open UTC window <c>(windowStartUtc, windowEndUtc]</c>,
    /// evaluated in the resolved time zone.
    /// </summary>
    bool IsDue(string cronExpression, string? timeZoneId, DateTimeOffset windowStartUtc, DateTimeOffset windowEndUtc);

    /// <summary>
    /// Resolves an IANA or Windows time zone id, falling back to the configured
    /// default and finally to UTC. Never throws.
    /// </summary>
    TimeZoneInfo ResolveTimeZone(string? timeZoneId);
}
