namespace AzVmStartStop.Functions;

/// <summary>
/// Azure resource tag names that drive the auto-start/stop behaviour.
/// </summary>
public static class TagNames
{
    /// <summary>
    /// Presence of this tag enables auto-start for a VM. The value is a
    /// 5-field cron expression (minute hour day-of-month month day-of-week),
    /// evaluated in the VM's time zone, e.g. <c>0 7 * * 1-5</c>.
    /// </summary>
    public const string AutoStart = "AutoStart";

    /// <summary>
    /// Presence of this tag enables auto-stop (deallocate) for a VM. The value is
    /// a 5-field cron expression, evaluated in the VM's time zone,
    /// e.g. <c>0 19 * * 1-5</c>.
    /// </summary>
    public const string AutoStop = "AutoStop";

    /// <summary>
    /// Optional IANA (e.g. <c>Europe/Amsterdam</c>) or Windows
    /// (e.g. <c>W. Europe Standard Time</c>) time zone id used to interpret the
    /// <see cref="AutoStart"/> cron expression. Falls back to the configured
    /// default time zone when absent or invalid.
    /// </summary>
    public const string AutoStartTimeZone = "AutoStartTimeZone";

    /// <summary>
    /// Optional time zone id used to interpret the <see cref="AutoStop"/> cron
    /// expression. Falls back to the configured default time zone when absent or
    /// invalid.
    /// </summary>
    public const string AutoStopTimeZone = "AutoStopTimeZone";
}
