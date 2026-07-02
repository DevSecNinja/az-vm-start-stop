namespace AzVmStartStop.Functions.Services;

/// <summary>
/// Helpers for interpreting Azure VM instance-view power states and deciding
/// whether a start or stop (deallocate) action is applicable.
/// </summary>
public static class VmPowerState
{
    /// <summary>Prefix of power-state status codes, e.g. <c>PowerState/running</c>.</summary>
    public const string StatusCodePrefix = "PowerState/";

    /// <summary>
    /// Extracts the normalized (lower-case) power state from a set of instance-view
    /// status codes, or <c>null</c> when no power-state code is present.
    /// </summary>
    public static string? FromStatusCodes(IEnumerable<string?> statusCodes)
    {
        foreach (var code in statusCodes)
        {
            if (code is not null && code.StartsWith(StatusCodePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return code[StatusCodePrefix.Length..].ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// Returns <c>true</c> when a VM in the given power state should be started.
    /// Starts unless it is already running or starting (unknown state is treated
    /// as startable, since starting a running VM is a safe no-op).
    /// </summary>
    public static bool ShouldStart(string? powerState) =>
        powerState is not ("running" or "starting");

    /// <summary>
    /// Returns <c>true</c> when a VM in the given power state should be stopped
    /// (deallocated). Only running VMs are stopped; unknown or transitional
    /// states are left untouched.
    /// </summary>
    public static bool ShouldStop(string? powerState) =>
        powerState is "running";
}
