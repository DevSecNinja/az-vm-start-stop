namespace AzVmStart.Functions.Services;

/// <summary>
/// Coordinates a single auto-start pass: scans in-scope VMs, evaluates their
/// <see cref="TagNames.AutoStart"/> schedule and starts those that are due.
/// </summary>
public interface IVmAutoStartService
{
    /// <summary>
    /// Runs one evaluation pass over the half-open UTC window
    /// <c>(windowStartUtc, windowEndUtc]</c>.
    /// </summary>
    Task<AutoStartRunSummary> RunAsync(
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken);
}

/// <summary>Aggregate outcome of an auto-start pass, primarily for logging/testing.</summary>
public sealed record AutoStartRunSummary(int Scanned, int Due, int Started, int Skipped, int Failed);
