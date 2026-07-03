using AzVmStartStop.Functions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCrontab;

namespace AzVmStartStop.Functions.Services;

/// <inheritdoc />
public sealed class CronScheduleEvaluator : ICronScheduleEvaluator
{
    private readonly AutoScheduleOptions _options;
    private readonly ILogger<CronScheduleEvaluator> _logger;

    public CronScheduleEvaluator(IOptions<AutoScheduleOptions> options, ILogger<CronScheduleEvaluator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsDue(string cronExpression, string? timeZoneId, DateTimeOffset windowStartUtc, DateTimeOffset windowEndUtc)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            return false;
        }

        if (windowEndUtc <= windowStartUtc)
        {
            _logger.LogWarning(
                "Cannot evaluate cron '{CronExpression}': window end {WindowEndUtc:o} is not after window start {WindowStartUtc:o}.",
                cronExpression.Trim(), windowEndUtc, windowStartUtc);
            return false;
        }

        var parseResult = CrontabSchedule.TryParse(cronExpression.Trim());
        if (parseResult is null)
        {
            _logger.LogWarning("Ignoring invalid cron expression '{CronExpression}'.", cronExpression);
            return false;
        }

        var timeZone = ResolveTimeZone(timeZoneId);

        // Interpret the cron expression in the VM's local time so that, e.g.,
        // "0 7 * * 1-5" means 07:00 local regardless of DST offset.
        var startLocal = TimeZoneInfo.ConvertTime(windowStartUtc, timeZone).DateTime;
        var endLocal = TimeZoneInfo.ConvertTime(windowEndUtc, timeZone).DateTime;

        // NCrontab's GetNextOccurrence is exclusive of the base time, so this
        // yields the first occurrence strictly after the window start. If it
        // falls on/before the window end, at least one occurrence is due.
        var next = parseResult.GetNextOccurrence(startLocal);
        var due = next <= endLocal;

        _logger.LogInformation(
            "Evaluated cron '{CronExpression}' in time zone '{TimeZoneId}' (UTC offset {UtcOffset}): " +
            "local window ({StartLocal:yyyy-MM-dd HH:mm:ss}, {EndLocal:yyyy-MM-dd HH:mm:ss}], " +
            "next occurrence {NextOccurrence:yyyy-MM-dd HH:mm:ss} => due={Due}.",
            cronExpression.Trim(),
            timeZone.Id,
            timeZone.GetUtcOffset(windowEndUtc),
            startLocal,
            endLocal,
            next,
            due);

        return due;
    }

    /// <inheritdoc />
    public TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId) && TryFind(timeZoneId!, out var requested))
        {
            return requested;
        }

        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            _logger.LogWarning(
                "Unknown time zone '{TimeZoneId}'; falling back to default '{DefaultTimeZone}'.",
                timeZoneId,
                _options.DefaultTimeZone);
        }

        if (TryFind(_options.DefaultTimeZone, out var fallback))
        {
            return fallback;
        }

        _logger.LogWarning(
            "Default time zone '{DefaultTimeZone}' is invalid; falling back to UTC.",
            _options.DefaultTimeZone);
        return TimeZoneInfo.Utc;
    }

    private static bool TryFind(string id, out TimeZoneInfo timeZone)
    {
        // .NET 8 resolves both IANA and Windows ids across platforms via ICU.
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
