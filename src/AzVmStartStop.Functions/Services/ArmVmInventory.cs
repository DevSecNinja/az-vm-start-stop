using System.Runtime.CompilerServices;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Resources;
using AzVmStartStop.Functions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzVmStartStop.Functions.Services;

/// <summary>
/// Azure Resource Manager backed <see cref="IVmInventory"/>. When no
/// subscription ids are configured, it scans every subscription accessible to
/// the function's managed identity; otherwise only the configured ones.
/// </summary>
public sealed class ArmVmInventory : IVmInventory
{
    private readonly ArmClient _armClient;
    private readonly AutoScheduleOptions _options;
    private readonly ILogger<ArmVmInventory> _logger;

    public ArmVmInventory(
        ArmClient armClient,
        IOptions<AutoScheduleOptions> options,
        ILogger<ArmVmInventory> logger)
    {
        _armClient = armClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IVmSubscriptionScope> GetSubscriptionsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_options.SubscriptionIds.Length == 0)
        {
            _logger.LogInformation(
                "No SubscriptionIds configured; scanning all subscriptions accessible to the managed identity.");

            await foreach (var subscription in _armClient.GetSubscriptions().GetAllAsync(cancellationToken))
            {
                yield return new ArmVmSubscriptionScope(subscription);
            }

            yield break;
        }

        _logger.LogInformation(
            "Using {Count} configured subscription(s): {SubscriptionIds}.",
            _options.SubscriptionIds.Length,
            string.Join(", ", _options.SubscriptionIds));

        foreach (var subscriptionId in _options.SubscriptionIds)
        {
            var id = SubscriptionResource.CreateResourceIdentifier(subscriptionId.Trim());
            yield return new ArmVmSubscriptionScope(_armClient.GetSubscriptionResource(id));
        }
    }

    private sealed class ArmVmSubscriptionScope : IVmSubscriptionScope
    {
        private readonly SubscriptionResource _subscription;

        public ArmVmSubscriptionScope(SubscriptionResource subscription) => _subscription = subscription;

        public string SubscriptionId =>
            _subscription.Id?.SubscriptionId ?? _subscription.Id?.ToString() ?? "(unknown)";

        public async IAsyncEnumerable<IVmScheduleTarget> GetVirtualMachinesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var vm in _subscription.GetVirtualMachinesAsync(cancellationToken: cancellationToken))
            {
                yield return new ArmVmScheduleTarget(vm);
            }
        }
    }

    private sealed class ArmVmScheduleTarget : IVmScheduleTarget
    {
        private readonly VirtualMachineResource _vm;

        public ArmVmScheduleTarget(VirtualMachineResource vm) => _vm = vm;

        public string Name => _vm.Id.Name;

        public string? ResourceGroup => _vm.Id.ResourceGroupName;

        public string? SubscriptionId => _vm.Id.SubscriptionId;

        public string Id => _vm.Id.ToString();

        public IReadOnlyDictionary<string, string>? Tags =>
            _vm.Data.Tags is { } tags ? new Dictionary<string, string>(tags) : null;

        public async Task<string?> GetPowerStateAsync(CancellationToken cancellationToken)
        {
            var instanceView = await _vm.InstanceViewAsync(cancellationToken);
            return VmPowerState.FromStatusCodes(instanceView.Value.Statuses.Select(s => s.Code));
        }

        public Task StartAsync(CancellationToken cancellationToken) =>
            _vm.PowerOnAsync(Azure.WaitUntil.Completed, cancellationToken);

        public Task DeallocateAsync(CancellationToken cancellationToken) =>
            _vm.DeallocateAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
    }
}
