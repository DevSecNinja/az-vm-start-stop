using AzVmStartStop.Functions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzVmStartStop.Functions.Services;

/// <inheritdoc />
public sealed class VmScheduleService : IVmScheduleService
{
    private readonly IVmInventory _inventory;
    private readonly ICronScheduleEvaluator _evaluator;
    private readonly AutoScheduleOptions _options;
    private readonly TimeSpan _operationTimeout;
    private readonly ILogger<VmScheduleService> _logger;

    public VmScheduleService(
        IVmInventory inventory,
        ICronScheduleEvaluator evaluator,
        IOptions<AutoScheduleOptions> options,
        ILogger<VmScheduleService> logger)
    {
        _inventory = inventory;
        _evaluator = evaluator;
        _options = options.Value;
        _operationTimeout = TimeSpan.FromSeconds(_options.OperationTimeoutSeconds);
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
            ["BuildSha"] = BuildInfo.CommitSha,
            ["WindowStartUtc"] = windowStartUtc,
            ["WindowEndUtc"] = windowEndUtc,
            ["DryRun"] = _options.DryRun,
        });

        _logger.LogInformation(
            "Starting schedule pass (RunId={RunId}, BuildSha={BuildSha}) over window ({WindowStartUtc:o}, {WindowEndUtc:o}]; DryRun={DryRun}.",
            runId, BuildInfo.CommitSha, windowStartUtc, windowEndUtc, _options.DryRun);

        await foreach (var subscription in _inventory.GetSubscriptionsAsync(cancellationToken))
        {
            var subscriptionId = subscription.SubscriptionId;
            _logger.LogInformation("Scanning subscription '{SubscriptionId}' for virtual machines.", subscriptionId);

            try
            {
                await foreach (var vm in subscription.GetVirtualMachinesAsync(cancellationToken))
                {
                    scanned++;
                    var name = vm.Name;

                    using var vmScope = _logger.BeginScope(new Dictionary<string, object>
                    {
                        ["VmName"] = name,
                        ["ResourceGroup"] = vm.ResourceGroup ?? string.Empty,
                        ["SubscriptionId"] = vm.SubscriptionId ?? string.Empty,
                        ["VmId"] = vm.Id,
                    });

                    var tags = vm.Tags;
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
                        var powerState = await vm.GetPowerStateAsync(cancellationToken);
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
                            if (await ExecuteWithTimeoutAsync(name, "start", vm.StartAsync, cancellationToken))
                            {
                                _logger.LogInformation("VM '{VmName}' has started.", name);
                            }

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
                            if (await ExecuteWithTimeoutAsync(name, "deallocate", vm.DeallocateAsync, cancellationToken))
                            {
                                _logger.LogInformation("VM '{VmName}' has stopped (deallocated).", name);
                            }

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
            "Schedule pass complete (RunId={RunId}, BuildSha={BuildSha}). Scanned={Scanned} Started={Started} Stopped={Stopped} Skipped={Skipped} Failed={Failed}.",
            runId, BuildInfo.CommitSha, scanned, started, stopped, skipped, failed);

        return new ScheduleRunSummary(scanned, started, stopped, skipped, failed);
    }

    /// <summary>
    /// Runs a start/deallocate operation, waiting up to <see cref="_operationTimeout"/>
    /// for it to complete. Returns <c>true</c> when the operation confirmed
    /// completion, or <c>false</c> when the wait timed out (the operation still
    /// continues in Azure). Real failures and host cancellation propagate.
    /// </summary>
    private async Task<bool> ExecuteWithTimeoutAsync(
        string vmName,
        string action,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_operationTimeout);

        try
        {
            await operation(timeoutCts.Token);
            return true;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "VM '{VmName}' {Action} was requested but did not confirm completion within {TimeoutSeconds}s; " +
                "it may still be transitioning in Azure.",
                vmName, action, _operationTimeout.TotalSeconds);
            return false;
        }
    }

    private bool IsDue(
        IReadOnlyDictionary<string, string> tags,
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
}
