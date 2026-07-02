using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Resources;
using AzVmStartStop.Functions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzVmStartStop.Functions.Services;

/// <inheritdoc />
public sealed class VmScheduleService : IVmScheduleService
{
    private readonly ArmClient _armClient;
    private readonly ICronScheduleEvaluator _evaluator;
    private readonly AutoScheduleOptions _options;
    private readonly ILogger<VmScheduleService> _logger;

    public VmScheduleService(
        ArmClient armClient,
        ICronScheduleEvaluator evaluator,
        IOptions<AutoScheduleOptions> options,
        ILogger<VmScheduleService> logger)
    {
        _armClient = armClient;
        _evaluator = evaluator;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ScheduleRunSummary> RunAsync(
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken)
    {
        int scanned = 0, started = 0, stopped = 0, skipped = 0, failed = 0;

        await foreach (var subscription in GetSubscriptionsAsync(cancellationToken))
        {
            await foreach (var vm in subscription.GetVirtualMachinesAsync(cancellationToken: cancellationToken))
            {
                scanned++;
                var tags = vm.Data.Tags;
                if (tags is null)
                {
                    continue;
                }

                var startDue = IsDue(tags, TagNames.AutoStart, TagNames.AutoStartTimeZone, windowStartUtc, windowEndUtc);
                var stopDue = IsDue(tags, TagNames.AutoStop, TagNames.AutoStopTimeZone, windowStartUtc, windowEndUtc);

                if (!startDue && !stopDue)
                {
                    continue;
                }

                var name = vm.Id.Name;

                if (startDue && stopDue)
                {
                    _logger.LogWarning(
                        "VM '{VmName}' has both AutoStart and AutoStop due in the same window; skipping to avoid conflicting actions.",
                        name);
                    skipped++;
                    continue;
                }

                try
                {
                    var powerState = await GetPowerStateAsync(vm, cancellationToken);

                    if (startDue)
                    {
                        if (!VmPowerState.ShouldStart(powerState))
                        {
                            _logger.LogInformation("VM '{VmName}' is due to start but is '{PowerState}'; skipping.", name, powerState);
                            skipped++;
                            continue;
                        }

                        if (_options.DryRun)
                        {
                            _logger.LogInformation("[DryRun] Would start VM '{VmName}'.", name);
                            started++;
                            continue;
                        }

                        _logger.LogInformation("Starting VM '{VmName}'.", name);
                        await vm.PowerOnAsync(Azure.WaitUntil.Started, cancellationToken);
                        started++;
                    }
                    else // stopDue
                    {
                        if (!VmPowerState.ShouldStop(powerState))
                        {
                            _logger.LogInformation("VM '{VmName}' is due to stop but is '{PowerState}'; skipping.", name, powerState);
                            skipped++;
                            continue;
                        }

                        if (_options.DryRun)
                        {
                            _logger.LogInformation("[DryRun] Would deallocate VM '{VmName}'.", name);
                            stopped++;
                            continue;
                        }

                        _logger.LogInformation("Deallocating VM '{VmName}'.", name);
                        await vm.DeallocateAsync(Azure.WaitUntil.Started, cancellationToken: cancellationToken);
                        stopped++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Failed to process VM '{VmName}'.", name);
                }
            }
        }

        _logger.LogInformation(
            "Schedule pass complete. Scanned={Scanned} Started={Started} Stopped={Stopped} Skipped={Skipped} Failed={Failed}.",
            scanned, started, stopped, skipped, failed);

        return new ScheduleRunSummary(scanned, started, stopped, skipped, failed);
    }

    private bool IsDue(
        IDictionary<string, string> tags,
        string cronTag,
        string timeZoneTag,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        if (!tags.TryGetValue(cronTag, out var cron) || string.IsNullOrWhiteSpace(cron))
        {
            return false;
        }

        tags.TryGetValue(timeZoneTag, out var timeZoneId);
        return _evaluator.IsDue(cron, timeZoneId, windowStartUtc, windowEndUtc);
    }

    private async IAsyncEnumerable<SubscriptionResource> GetSubscriptionsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_options.SubscriptionIds.Length == 0)
        {
            yield return await _armClient.GetDefaultSubscriptionAsync(cancellationToken);
            yield break;
        }

        foreach (var subscriptionId in _options.SubscriptionIds)
        {
            var id = SubscriptionResource.CreateResourceIdentifier(subscriptionId.Trim());
            yield return _armClient.GetSubscriptionResource(id);
        }
    }

    private static async Task<string?> GetPowerStateAsync(VirtualMachineResource vm, CancellationToken cancellationToken)
    {
        var instanceView = await vm.InstanceViewAsync(cancellationToken);
        return VmPowerState.FromStatusCodes(instanceView.Value.Statuses.Select(s => s.Code));
    }
}
