using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Resources;
using AzVmStart.Functions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzVmStart.Functions.Services;

/// <inheritdoc />
public sealed class VmAutoStartService : IVmAutoStartService
{
    private const string PowerStatePrefix = "PowerState/";

    private readonly ArmClient _armClient;
    private readonly ICronScheduleEvaluator _evaluator;
    private readonly AutoStartOptions _options;
    private readonly ILogger<VmAutoStartService> _logger;

    public VmAutoStartService(
        ArmClient armClient,
        ICronScheduleEvaluator evaluator,
        IOptions<AutoStartOptions> options,
        ILogger<VmAutoStartService> logger)
    {
        _armClient = armClient;
        _evaluator = evaluator;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AutoStartRunSummary> RunAsync(
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken)
    {
        int scanned = 0, due = 0, started = 0, skipped = 0, failed = 0;

        await foreach (var subscription in GetSubscriptionsAsync(cancellationToken))
        {
            await foreach (var vm in subscription.GetVirtualMachinesAsync(cancellationToken: cancellationToken))
            {
                scanned++;

                var tags = vm.Data.Tags;
                if (tags is null || !tags.TryGetValue(TagNames.AutoStart, out var cron) || string.IsNullOrWhiteSpace(cron))
                {
                    continue;
                }

                tags.TryGetValue(TagNames.AutoStartTimeZone, out var timeZoneId);

                if (!_evaluator.IsDue(cron, timeZoneId, windowStartUtc, windowEndUtc))
                {
                    continue;
                }

                due++;
                var name = vm.Id.Name;

                try
                {
                    if (await IsRunningOrStartingAsync(vm, cancellationToken))
                    {
                        _logger.LogInformation("VM '{VmName}' is due but already running/starting; skipping.", name);
                        skipped++;
                        continue;
                    }

                    if (_options.DryRun)
                    {
                        _logger.LogInformation(
                            "[DryRun] Would start VM '{VmName}' (cron '{Cron}', tz '{TimeZone}').",
                            name, cron, timeZoneId ?? _options.DefaultTimeZone);
                        started++;
                        continue;
                    }

                    _logger.LogInformation(
                        "Starting VM '{VmName}' (cron '{Cron}', tz '{TimeZone}').",
                        name, cron, timeZoneId ?? _options.DefaultTimeZone);

                    await vm.PowerOnAsync(Azure.WaitUntil.Started, cancellationToken);
                    started++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Failed to start VM '{VmName}'.", name);
                }
            }
        }

        _logger.LogInformation(
            "Auto-start pass complete. Scanned={Scanned} Due={Due} Started={Started} Skipped={Skipped} Failed={Failed}.",
            scanned, due, started, skipped, failed);

        return new AutoStartRunSummary(scanned, due, started, skipped, failed);
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

    private static async Task<bool> IsRunningOrStartingAsync(VirtualMachineResource vm, CancellationToken cancellationToken)
    {
        var instanceView = await vm.InstanceViewAsync(cancellationToken);
        foreach (var status in instanceView.Value.Statuses)
        {
            var code = status.Code;
            if (code is null || !code.StartsWith(PowerStatePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var state = code[PowerStatePrefix.Length..];
            return state.Equals("running", StringComparison.OrdinalIgnoreCase)
                || state.Equals("starting", StringComparison.OrdinalIgnoreCase);
        }

        // No power state reported: treat as not running so we attempt a start.
        return false;
    }
}
