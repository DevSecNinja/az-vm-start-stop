using System.ComponentModel.DataAnnotations;

namespace AzVmStart.Functions.Options;

/// <summary>
/// Bound from the <c>AutoStart</c> configuration section / app settings.
/// </summary>
public sealed class AutoStartOptions
{
    public const string SectionName = "AutoStart";

    /// <summary>
    /// Time zone used to interpret a VM's cron expression when the VM has no
    /// <see cref="TagNames.AutoStartTimeZone"/> tag. Defaults to Amsterdam time.
    /// </summary>
    [Required]
    public string DefaultTimeZone { get; set; } = "Europe/Amsterdam";

    /// <summary>
    /// Fallback look-back window (in minutes) used to detect due cron
    /// occurrences on the first invocation, when the timer has no recorded
    /// previous run. Should match the timer cadence.
    /// </summary>
    [Range(1, 1440)]
    public int ScheduleWindowMinutes { get; set; } = 5;

    /// <summary>
    /// Optional list of subscription ids to scan. When empty, the default
    /// subscription of the function's managed identity is used.
    /// </summary>
    public string[] SubscriptionIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// When true, evaluates and logs which VMs would start but performs no
    /// start operation. Useful for validating tags safely.
    /// </summary>
    public bool DryRun { get; set; }
}
