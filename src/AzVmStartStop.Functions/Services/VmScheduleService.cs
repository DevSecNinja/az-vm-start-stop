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

        var runId = Guid.NewGuid().ToString("N");
        using var runScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["RunId"] = runId,
            ["WindowStartUtc"] = windowStartUtc,
            ["WindowEndUtc"] = windowEndUtc,
            ["DryRun"] = _options.DryRun,
        });

        _logger.LogInformation(
            "Starting schedule pass (RunId={RunId}) over window ({WindowStartUtc:o}, {WindowEndUtc:o}]; DryRun={DryRun}.",
            runId, windowStartUtc, windowEndUtc, _options.DryRun);

        await foreach (var subscription in GetSubscriptionsAsync(cancellationToken))
        {
            var subscriptionId = subscription.Id?.SubscriptionId ?? subscription.Id?.ToString() ?? "(unknown)";
            _logger.LogInformation("Scanning subscription '{SubscriptionId}' for virtual machines.", subscriptionId);

            try
            {
                await foreach (var vm in subscription.GetVirtualMachinesAsync(cancellationToken: cancellationToken))
                {
                    scanned++;
                    var name = vm.Id.Name;

                    using var vmScope = _logger.BeginScope(new Dictionary<string, object>
                    {
                        ["VmName"] = name,
                        ["ResourceGroup"] = vm.Id.ResourceGroupName ?? string.Empty,
                        ["SubscriptionId"] = vm.Id.SubscriptionId ?? string.Empty,
                        ["VmId"] = vm.Id.ToString(),
                    });

                    var tags = vm.Data.Tags;
                    var tagKeys = tags is { Count: > 0 } ? string.Join(", ", tags.Keys) : "(none)";

                    if (tags is null || tags.Count == 0)
                    {
                        _logger.LogInformation("Scanned VM '{VmName}'; it has no tags, so nothing to do.", name);
                        continue;
                    }

                    tags.TryGetValue(TagNames.AutoStart, out var autoStartCron);
                    tags.TryGetValue(TagNames.AutoStop, out var autoStopCron);
                    tags.TryGetValue(TagNames.AutoStartTimeZone, out var autoStartTz);
                    tags.TryGetValue(TagNames.AutoStopTimeZone, out var autoStopTz);

                    _logger.LogInformation(
                        "Scanned VM '{VmName}'; tags=[{TagKeys}]; AutoStart='{AutoStart}' (tz='{AutoStartTz}'), " +
                        "AutoStop='{AutoStop}' (tz='{AutoStopTz}'). Evaluating due schedules next.",
                        name,
                        tagKeys,
                        autoStartCron ?? "(absent)",
                        autoStartTz ?? "(default)",
                        autoStopCron ?? "(absent)",
                        autoStopTz ?? "(default)");

                    var startDue = IsDue(tags, TagNames.AutoStart, TagNames.AutoStartTimeZone, windowStartUtc, windowEndUtc);
                    var stopDue = IsDue(tags, TagNames.AutoStop, TagNames.AutoStopTimeZone, windowStartUtc, windowEndUtc);

                    if (!startDue && !stopDue)
                    {
                        _logger.LogInformation(
                            "VM '{VmName}' has no AutoStart/AutoStop occurrence due in this window; nothing to do.",
                            name);
                        continue;
                    }

                    if (startDue && stopDue)
                    {
                        _logger.LogWarning(
                            "VM '{VmName}' has both AutoStart and AutoStop due in the same window; skipping to avoid conflicting actions.",
                            name);
                        skipped++;
                        continue;
                    }

                    var action = startDue ? "Start" : "Stop";
                    using var actionScope = _logger.BeginScope(new Dictionary<string, object>
                    {
                        ["Action"] = action,
                    });

                    try
                    {
                        var powerState = await GetPowerStateAsync(vm, cancellationToken);
                        _logger.LogInformation(
                            "VM '{VmName}' is due to {Action}; current power state is '{PowerState}'.",
                            name, action, powerState ?? "unknown");

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
                        _logger.LogError(ex, "Failed to {Action} VM '{VmName}'.", action, name);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(
                    ex,
                    "Failed to list or process virtual machines in subscription '{SubscriptionId}'. " +
                    "This is often a permissions issue on the function's managed identity.",
                    subscriptionId);
            }
        }

        _logger.LogInformation(
            "Schedule pass complete (RunId={RunId}). Scanned={Scanned} Started={Started} Stopped={Stopped} Skipped={Skipped} Failed={Failed}.",
            runId, scanned, started, stopped, skipped, failed);

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
            _logger.LogInformation(
                "No SubscriptionIds configured; scanning all subscriptions accessible to the managed identity.");

            await foreach (var subscription in _armClient.GetSubscriptions().GetAllAsync(cancellationToken))
            {
                yield return subscription;
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
            yield return _armClient.GetSubscriptionResource(id);
        }
    }

    private static async Task<string?> GetPowerStateAsync(VirtualMachineResource vm, CancellationToken cancellationToken)
    {
        var instanceView = await vm.InstanceViewAsync(cancellationToken);
        return VmPowerState.FromStatusCodes(instanceView.Value.Statuses.Select(s => s.Code));
    }
}
