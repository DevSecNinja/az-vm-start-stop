using AzVmStart.Functions.Options;
using AzVmStart.Functions.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AzVmStart.Functions.Tests;

public sealed class CronScheduleEvaluatorTests
{
    private static CronScheduleEvaluator CreateEvaluator(string defaultTz = "Europe/Amsterdam")
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AutoStartOptions { DefaultTimeZone = defaultTz });
        return new CronScheduleEvaluator(options, NullLogger<CronScheduleEvaluator>.Instance);
    }

    [Fact]
    public void IsDue_ReturnsTrue_WhenOccurrenceFallsInsideWindow_AmsterdamWinter()
    {
        var evaluator = CreateEvaluator();

        // 07:00 Amsterdam (CET, UTC+1) on Mon 2024-01-08 == 06:00 UTC.
        var start = new DateTimeOffset(2024, 1, 8, 5, 58, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 8, 6, 3, 0, TimeSpan.Zero);

        Assert.True(evaluator.IsDue("0 7 * * 1-5", timeZoneId: null, start, end));
    }

    [Fact]
    public void IsDue_ReturnsTrue_WhenOccurrenceFallsInsideWindow_AmsterdamSummer()
    {
        var evaluator = CreateEvaluator();

        // 07:00 Amsterdam (CEST, UTC+2) on Mon 2024-07-08 == 05:00 UTC.
        var start = new DateTimeOffset(2024, 7, 8, 4, 58, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 7, 8, 5, 3, 0, TimeSpan.Zero);

        Assert.True(evaluator.IsDue("0 7 * * 1-5", timeZoneId: null, start, end));
    }

    [Fact]
    public void IsDue_ReturnsFalse_WhenOutsideWindow()
    {
        var evaluator = CreateEvaluator();

        // Window well before 07:00 Amsterdam == 06:00 UTC (winter).
        var start = new DateTimeOffset(2024, 1, 8, 4, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 8, 4, 5, 0, TimeSpan.Zero);

        Assert.False(evaluator.IsDue("0 7 * * 1-5", timeZoneId: null, start, end));
    }

    [Fact]
    public void IsDue_ReturnsFalse_OnWeekend_ForWeekdayCron()
    {
        var evaluator = CreateEvaluator();

        // Sunday 2024-01-07, 07:00 Amsterdam == 06:00 UTC. Cron is Mon-Fri only.
        var start = new DateTimeOffset(2024, 1, 7, 5, 58, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 7, 6, 3, 0, TimeSpan.Zero);

        Assert.False(evaluator.IsDue("0 7 * * 1-5", timeZoneId: null, start, end));
    }

    [Fact]
    public void IsDue_RespectsPerVmTimeZone_OverDefault()
    {
        var evaluator = CreateEvaluator(defaultTz: "Europe/Amsterdam");

        // 07:00 UTC window; cron interpreted in UTC via the per-VM tag.
        var start = new DateTimeOffset(2024, 1, 8, 6, 58, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 8, 7, 3, 0, TimeSpan.Zero);

        Assert.True(evaluator.IsDue("0 7 * * 1-5", timeZoneId: "UTC", start, end));
        // The same instant is NOT due when interpreted in Amsterdam (would be 08:00 local).
        Assert.False(evaluator.IsDue("0 7 * * 1-5", timeZoneId: "Europe/Amsterdam", start, end));
    }

    [Theory]
    [InlineData("not-a-cron")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsDue_ReturnsFalse_ForInvalidOrEmptyCron(string cron)
    {
        var evaluator = CreateEvaluator();
        var start = new DateTimeOffset(2024, 1, 8, 5, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 8, 8, 0, 0, TimeSpan.Zero);

        Assert.False(evaluator.IsDue(cron, timeZoneId: null, start, end));
    }

    [Fact]
    public void IsDue_ReturnsFalse_ForEmptyOrInvertedWindow()
    {
        var evaluator = CreateEvaluator();
        var instant = new DateTimeOffset(2024, 1, 8, 6, 0, 0, TimeSpan.Zero);

        Assert.False(evaluator.IsDue("* * * * *", timeZoneId: null, instant, instant));
        Assert.False(evaluator.IsDue("* * * * *", timeZoneId: null, instant, instant.AddMinutes(-5)));
    }

    [Fact]
    public void ResolveTimeZone_FallsBackToDefault_ForUnknownId()
    {
        var evaluator = CreateEvaluator(defaultTz: "Europe/Amsterdam");
        var tz = evaluator.ResolveTimeZone("Totally/Bogus");

        var amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        Assert.Equal(amsterdam.Id, tz.Id);
    }

    [Fact]
    public void ResolveTimeZone_AcceptsWindowsAndIanaIds()
    {
        var evaluator = CreateEvaluator();

        Assert.NotNull(evaluator.ResolveTimeZone("W. Europe Standard Time"));
        Assert.NotNull(evaluator.ResolveTimeZone("Europe/Amsterdam"));
    }
}
