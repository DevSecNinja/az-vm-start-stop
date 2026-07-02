namespace AzVmStartStop.Functions.Services;

/// <summary>
/// Coordinates a single schedule pass: scans in-scope VMs and, based on their
/// <see cref="TagNames.AutoStart"/> and <see cref="TagNames.AutoStop"/> tags,
/// starts VMs that are due to start and deallocates VMs that are due to stop.
/// </summary>
public interface IVmScheduleService
{
    /// <summary>
    /// Runs one evaluation pass over the half-open UTC window
    /// <c>(windowStartUtc, windowEndUtc]</c>.
    /// </summary>
    Task<ScheduleRunSummary> RunAsync(
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken);
}

/// <summary>Aggregate outcome of a schedule pass, primarily for logging/testing.</summary>
public sealed record ScheduleRunSummary(
    int Scanned,
    int Started,
    int Stopped,
    int Skipped,
    int Failed);
