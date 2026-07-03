namespace AzVmStartStop.Functions.Services;

/// <summary>
/// Provides the subscriptions (and their virtual machines) that a schedule pass
/// should evaluate. Abstracts the Azure Resource Manager SDK so the schedule
/// orchestration in <see cref="VmScheduleService"/> can be unit tested.
/// </summary>
public interface IVmInventory
{
    /// <summary>
    /// Enumerates the subscriptions in scope. When no subscriptions are
    /// configured, this is every subscription accessible to the identity.
    /// </summary>
    IAsyncEnumerable<IVmSubscriptionScope> GetSubscriptionsAsync(CancellationToken cancellationToken);
}

/// <summary>A single subscription in scope and its virtual machines.</summary>
public interface IVmSubscriptionScope
{
    /// <summary>The subscription id (or a best-effort identifier for logging).</summary>
    string SubscriptionId { get; }

    /// <summary>
    /// Enumerates the virtual machines in the subscription. Enumeration may
    /// throw (e.g. on missing permissions); callers handle that per subscription.
    /// </summary>
    IAsyncEnumerable<IVmScheduleTarget> GetVirtualMachinesAsync(CancellationToken cancellationToken);
}

/// <summary>A virtual machine that the scheduler can inspect and act on.</summary>
public interface IVmScheduleTarget
{
    string Name { get; }

    string? ResourceGroup { get; }

    string? SubscriptionId { get; }

    string Id { get; }

    /// <summary>The resource tags, or <c>null</c> when the VM has none.</summary>
    IReadOnlyDictionary<string, string>? Tags { get; }

    /// <summary>The normalized (lower-case) power state, or <c>null</c> when unknown.</summary>
    Task<string?> GetPowerStateAsync(CancellationToken cancellationToken);

    /// <summary>Starts (powers on) the virtual machine.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Deallocates (stops) the virtual machine.</summary>
    Task DeallocateAsync(CancellationToken cancellationToken);
}
