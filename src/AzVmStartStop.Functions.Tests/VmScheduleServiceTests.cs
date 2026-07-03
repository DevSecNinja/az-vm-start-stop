using AzVmStartStop.Functions.Options;
using AzVmStartStop.Functions.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AzVmStartStop.Functions.Tests;

public sealed class VmScheduleServiceTests
{
    private static readonly DateTimeOffset WindowStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 1, 1, 0, 5, 0, TimeSpan.Zero);

    // Tag values understood by FakeCronEvaluator: "due" => in window, anything else => not.
    private const string Due = "due";
    private const string NotDue = "notdue";

    private static VmScheduleService CreateService(FakeInventory inventory, bool dryRun = false, int timeoutSeconds = 45)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AutoScheduleOptions { DryRun = dryRun, OperationTimeoutSeconds = timeoutSeconds });
        return new VmScheduleService(
            inventory,
            new FakeCronEvaluator(),
            options,
            NullLogger<VmScheduleService>.Instance);
    }

    [Fact]
    public async Task RunAsync_StartsDueStoppedVms_AcrossMultipleSubscriptions()
    {
        var vm1 = new FakeVm("vm1", powerState: "deallocated", tags: new() { [TagNames.AutoStart] = Due });
        var vm2 = new FakeVm("vm2", powerState: "deallocated", tags: new() { [TagNames.AutoStart] = Due });
        var inventory = new FakeInventory(
            new FakeSubscription("sub-a", vm1),
            new FakeSubscription("sub-b", vm2));

        var summary = await CreateService(inventory).RunAsync(WindowStart, WindowEnd, CancellationToken.None);

        Assert.Equal(2, summary.Scanned);
        Assert.Equal(2, summary.Started);
        Assert.Equal(0, summary.Stopped);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(1, vm1.StartCount);
        Assert.Equal(1, vm2.StartCount);
    }

    [Fact]
    public async Task RunAsync_DeallocatesDueRunningVm()
    {
        var vm = new FakeVm("vm", powerState: "running", tags: new() { [TagNames.AutoStop] = Due });
        var inventory = new FakeInventory(new FakeSubscription("sub", vm));

        var summary = await CreateService(inventory).RunAsync(WindowStart, WindowEnd, CancellationToken.None);

        Assert.Equal(1, summary.Stopped);
        Assert.Equal(0, summary.Started);
        Assert.Equal(1, vm.DeallocateCount);
    }

    [Fact]
    public async Task RunAsync_SkipsStartWhenAlreadyRunning()
    {
        var vm = new FakeVm("vm", powerState: "running", tags: new() { [TagNames.AutoStart] = Due });
        var inventory = new FakeInventory(new FakeSubscription("sub", vm));

        var summary = await CreateService(inventory).RunAsync(WindowStart, WindowEnd, CancellationToken.None);

        Assert.Equal(1, summary.Skipped);
        Assert.Equal(0, summary.Started);
        Assert.Equal(0, vm.StartCount);
    }

    [Fact]
    public async Task RunAsync_SkipsWhenStartAndStopBothDue()
    {
        var vm = new FakeVm("vm", powerState: "deallocated",
            tags: new() { [TagNames.AutoStart] = Due, [TagNames.AutoStop] = Due });
        var inventory = new FakeInventory(new FakeSubscription("sub", vm));

        var summary = await CreateService(inventory).RunAsync(WindowStart, WindowEnd, CancellationToken.None);

        Assert.Equal(1, summary.Skipped);
        Assert.Equal(0, summary.Started);
        Assert.Equal(0, summary.Stopped);
        Assert.Equal(0, vm.StartCount);
        Assert.Equal(0, vm.DeallocateCount);
    }

    [Fact]
    public async Task RunAsync_DryRun_CountsButDoesNotAct()
    {
        var vm = new FakeVm("vm", powerState: "deallocated", tags: new() { [TagNames.AutoStart] = Due });
        var inventory = new FakeInventory(new FakeSubscription("sub", vm));

        var summary = await CreateService(inventory, dryRun: true).RunAsync(WindowStart, WindowEnd, CancellationToken.None);

        Assert.Equal(1, summary.Started);
        Assert.Equal(0, vm.StartCount);
    }

    [Fact]
    public async Task RunAsync_IgnoresVmsWithNoDueSchedule()
    {
        var vm = new FakeVm("vm", powerState: "deallocated", tags: new() { [TagNames.AutoStart] = NotDue });
        var inventory = new FakeInventory(new FakeSubscription("sub", vm));

        var summary = await CreateService(inventory).RunAsync(WindowStart, WindowEnd, CancellationToken.None);

        Assert.Equal(1, summary.Scanned);
        Assert.Equal(0, summary.Started);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(0, vm.StartCount);
    }

    [Fact]
    public async Task RunAsync_CountsVmOperationFailure_AndContinues()
    {
        var failing = new FakeVm("bad", powerState: "deallocated",
            tags: new() { [TagNames.AutoStart] = Due }, throwOnStart: true);
        var healthy = new FakeVm("good", powerState: "deallocated", tags: new() { [TagNames.AutoStart] = Due });
        var inventory = new FakeInventory(new FakeSubscription("sub", failing, healthy));

        var summary = await CreateService(inventory).RunAsync(WindowStart, WindowEnd, CancellationToken.None);

        Assert.Equal(2, summary.Scanned);
        Assert.Equal(1, summary.Started);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, healthy.StartCount);
    }

    [Fact]
    public async Task RunAsync_SubscriptionListingFailure_IsCounted_AndOtherSubscriptionsProcessed()
    {
        var vm = new FakeVm("vm", powerState: "deallocated", tags: new() { [TagNames.AutoStart] = Due });
        var inventory = new FakeInventory(
            new FakeSubscription("bad-sub", throwOnList: true),
            new FakeSubscription("good-sub", vm));

        var summary = await CreateService(inventory).RunAsync(WindowStart, WindowEnd, CancellationToken.None);

        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Started);
        Assert.Equal(1, vm.StartCount);
    }

    [Fact]
    public async Task RunAsync_StartTimeout_StillCountsStarted_AndDoesNotFail()
    {
        var vm = new FakeVm("vm", powerState: "deallocated",
            tags: new() { [TagNames.AutoStart] = Due }, hangUntilCancelled: true);
        var inventory = new FakeInventory(new FakeSubscription("sub", vm));

        var summary = await CreateService(inventory, timeoutSeconds: 1).RunAsync(WindowStart, WindowEnd, CancellationToken.None);

        Assert.Equal(1, summary.Started);
        Assert.Equal(0, summary.Failed);
    }

    private sealed class FakeCronEvaluator : ICronScheduleEvaluator
    {
        public bool IsDue(string cronExpression, string? timeZoneId, DateTimeOffset windowStartUtc, DateTimeOffset windowEndUtc)
            => string.Equals(cronExpression, Due, StringComparison.OrdinalIgnoreCase);

        public TimeZoneInfo ResolveTimeZone(string? timeZoneId) => TimeZoneInfo.Utc;
    }

    private sealed class FakeInventory : IVmInventory
    {
        private readonly IReadOnlyList<FakeSubscription> _subscriptions;

        public FakeInventory(params FakeSubscription[] subscriptions) => _subscriptions = subscriptions;

        public async IAsyncEnumerable<IVmSubscriptionScope> GetSubscriptionsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var subscription in _subscriptions)
            {
                await Task.Yield();
                yield return subscription;
            }
        }
    }

    private sealed class FakeSubscription : IVmSubscriptionScope
    {
        private readonly IReadOnlyList<FakeVm> _vms;
        private readonly bool _throwOnList;

        public FakeSubscription(string subscriptionId, params FakeVm[] vms)
            : this(subscriptionId, throwOnList: false, vms)
        {
        }

        public FakeSubscription(string subscriptionId, bool throwOnList, params FakeVm[] vms)
        {
            SubscriptionId = subscriptionId;
            _throwOnList = throwOnList;
            _vms = vms;
        }

        public string SubscriptionId { get; }

        public async IAsyncEnumerable<IVmScheduleTarget> GetVirtualMachinesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (_throwOnList)
            {
                throw new InvalidOperationException("Simulated listing failure (e.g. missing permissions).");
            }

            foreach (var vm in _vms)
            {
                await Task.Yield();
                yield return vm;
            }
        }
    }

    private sealed class FakeVm : IVmScheduleTarget
    {
        private readonly string? _powerState;
        private readonly bool _throwOnStart;
        private readonly bool _hangUntilCancelled;

        public FakeVm(
            string name,
            string? powerState,
            Dictionary<string, string>? tags = null,
            bool throwOnStart = false,
            bool hangUntilCancelled = false)
        {
            Name = name;
            _powerState = powerState;
            Tags = tags;
            _throwOnStart = throwOnStart;
            _hangUntilCancelled = hangUntilCancelled;
        }

        public string Name { get; }

        public string? ResourceGroup => "rg";

        public string? SubscriptionId => "sub";

        public string Id => $"/vm/{Name}";

        public IReadOnlyDictionary<string, string>? Tags { get; }

        public int StartCount { get; private set; }

        public int DeallocateCount { get; private set; }

        public Task<string?> GetPowerStateAsync(CancellationToken cancellationToken) => Task.FromResult(_powerState);

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_throwOnStart)
            {
                throw new InvalidOperationException("Simulated start failure.");
            }

            if (_hangUntilCancelled)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            StartCount++;
        }

        public Task DeallocateAsync(CancellationToken cancellationToken)
        {
            DeallocateCount++;
            return Task.CompletedTask;
        }
    }
}
