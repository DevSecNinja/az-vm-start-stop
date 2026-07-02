using AzVmStartStop.Functions.Services;
using Xunit;

namespace AzVmStartStop.Functions.Tests;

public sealed class VmPowerStateTests
{
    [Theory]
    [InlineData("PowerState/running", "running")]
    [InlineData("PowerState/deallocated", "deallocated")]
    [InlineData("PowerState/stopped", "stopped")]
    [InlineData("PowerState/starting", "starting")]
    [InlineData("POWERSTATE/RUNNING", "running")]
    public void FromStatusCodes_ExtractsNormalizedPowerState(string code, string expected)
    {
        var state = VmPowerState.FromStatusCodes(new[] { "ProvisioningState/succeeded", code });
        Assert.Equal(expected, state);
    }

    [Fact]
    public void FromStatusCodes_ReturnsNull_WhenNoPowerStateCode()
    {
        var state = VmPowerState.FromStatusCodes(new[] { "ProvisioningState/succeeded", null });
        Assert.Null(state);
    }

    [Theory]
    [InlineData("deallocated", true)]
    [InlineData("stopped", true)]
    [InlineData(null, true)]
    [InlineData("running", false)]
    [InlineData("starting", false)]
    public void ShouldStart_StartsUnlessRunningOrStarting(string? state, bool expected)
    {
        Assert.Equal(expected, VmPowerState.ShouldStart(state));
    }

    [Theory]
    [InlineData("running", true)]
    [InlineData("deallocated", false)]
    [InlineData("stopped", false)]
    [InlineData("starting", false)]
    [InlineData(null, false)]
    public void ShouldStop_OnlyStopsRunning(string? state, bool expected)
    {
        Assert.Equal(expected, VmPowerState.ShouldStop(state));
    }
}
