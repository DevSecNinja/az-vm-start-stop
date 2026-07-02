using AzVmStart.Functions.Options;
using AzVmStart.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzVmStart.Functions;

/// <summary>
/// Timer-triggered entry point that runs an auto-start pass on a schedule.
/// The timer cadence is configured via the <c>ScheduleExpression</c> app setting
/// (6-field NCRONTAB, e.g. <c>0 */5 * * * *</c> = every 5 minutes).
/// </summary>
public sealed class AutoStartFunction
{
    private readonly IVmAutoStartService _service;
    private readonly AutoStartOptions _options;
    private readonly ILogger<AutoStartFunction> _logger;

    public AutoStartFunction(
        IVmAutoStartService service,
        IOptions<AutoStartOptions> options,
        ILogger<AutoStartFunction> logger)
    {
        _service = service;
        _options = options.Value;
        _logger = logger;
    }

    [Function("AutoStart")]
    public async Task RunAsync(
        [TimerTrigger("%ScheduleExpression%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;

        // Use the previous scheduled run as the window start so that occurrences
        // are neither missed nor double-counted across invocations. On the first
        // run (no recorded previous), fall back to the configured window.
        var windowStartUtc = ResolveWindowStart(timer, nowUtc);

        _logger.LogInformation(
            "Auto-start triggered at {NowUtc:o}; evaluating window ({WindowStartUtc:o}, {NowUtc:o}].",
            nowUtc, windowStartUtc, nowUtc);

        await _service.RunAsync(windowStartUtc, nowUtc, cancellationToken);
    }

    private DateTimeOffset ResolveWindowStart(TimerInfo timer, DateTimeOffset nowUtc)
    {
        var last = timer.ScheduleStatus?.Last;
        if (last is { } lastRun && lastRun != default)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(lastRun, DateTimeKind.Utc));
        }

        return nowUtc.AddMinutes(-_options.ScheduleWindowMinutes);
    }
}
