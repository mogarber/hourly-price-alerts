using HourlyPriceAlerts.Core;

namespace HourlyPriceAlerts.Tests;

public class QuietHoursTests
{
    private static readonly TimeZoneInfo Chicago = Fx.Zone();

    private static DateTimeOffset Utc(int month, int day, int hour, int minute)
        => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    private static bool Quiet(int start, int end, DateTimeOffset nowUtc)
        => QuietHoursCheck.IsQuiet(new QuietHours { StartMinute = start, EndMinute = end }, nowUtc, Chicago);

    [Fact]
    public void Null_quiet_hours_are_never_quiet()
        => Assert.False(QuietHoursCheck.IsQuiet(null, Utc(11, 2, 7, 0), Chicago));

    [Theory]
    [InlineData(0)]
    [InlineData(600)]
    [InlineData(1439)]
    public void Start_equal_to_end_means_no_quiet_hours(int minute)
    {
        // 2026-11-02 07:00Z is 01:00 in Chicago.
        Assert.False(Quiet(minute, minute, Utc(11, 2, 7, 0)));
        Assert.False(Quiet(60, 60, Utc(11, 2, 7, 0)));
    }

    [Theory]
    // Same-day window 09:00 to 17:00 (540 to 1020); Chicago is UTC-6 on 2026-11-02.
    [InlineData(14, 59, false)]   // 08:59
    [InlineData(15, 0, true)]     // 09:00, start inclusive
    [InlineData(22, 59, true)]    // 16:59
    [InlineData(23, 0, false)]    // 17:00, end exclusive
    public void Same_day_window(int hourUtc, int minute, bool expected)
        => Assert.Equal(expected, Quiet(540, 1020, Utc(11, 2, hourUtc, minute)));

    [Theory]
    // Wrapping window 22:00 to 07:00 (1320 to 420).
    [InlineData(3, 59, false)]    // 21:59 on the previous local day
    [InlineData(4, 0, true)]      // 22:00, start inclusive
    [InlineData(5, 59, true)]     // 23:59
    [InlineData(6, 0, true)]      // 00:00, across midnight
    [InlineData(12, 59, true)]    // 06:59
    [InlineData(13, 0, false)]    // 07:00, end exclusive
    [InlineData(18, 0, false)]    // noon
    public void Window_wrapping_past_midnight(int hourUtc, int minute, bool expected)
        => Assert.Equal(expected, Quiet(1320, 420, Utc(11, 2, hourUtc, minute)));

    [Fact]
    public void Window_ending_at_midnight_and_one_starting_at_midnight()
    {
        Assert.True(Quiet(1320, 0, Utc(11, 3, 5, 59)));    // 23:59, window 22:00 to 00:00
        Assert.False(Quiet(1320, 0, Utc(11, 3, 6, 0)));    // 00:00
        Assert.True(Quiet(0, 420, Utc(11, 3, 6, 0)));      // 00:00, window 00:00 to 07:00
    }

    // ---- DST changeover days in America/Chicago ----

    [Theory]
    // Spring forward: 2026-03-08, 02:00 CST jumps to 03:00 CDT at 08:00Z. Window 22:00 to 07:00.
    [InlineData(3, 8, 7, 30, true)]     // 01:30 CST
    [InlineData(3, 8, 8, 0, true)]      // 03:00 CDT
    [InlineData(3, 8, 11, 59, true)]    // 06:59 CDT
    [InlineData(3, 8, 12, 0, false)]    // 07:00 CDT: would be 06:00 CST on a normal day
    // The day before (no DST yet): 12:00Z is 06:00 CST, still quiet.
    [InlineData(3, 7, 12, 0, true)]
    // Fall back: 2026-11-01, 02:00 CDT returns to 01:00 CST at 07:00Z.
    [InlineData(11, 1, 5, 59, true)]    // 00:59 CDT
    [InlineData(11, 1, 6, 30, true)]    // 01:30 CDT
    [InlineData(11, 1, 7, 30, true)]    // 01:30 CST, the repeated hour
    [InlineData(11, 1, 12, 59, true)]   // 06:59 CST
    [InlineData(11, 1, 13, 0, false)]   // 07:00 CST
    [InlineData(11, 1, 12, 0, true)]    // 06:00 CST (would be 07:00 CDT and not quiet if the offset were wrong)
    public void Wrapping_window_follows_local_wall_time_on_dst_days(int month, int day, int hour, int minute, bool expected)
        => Assert.Equal(expected, Quiet(1320, 420, Utc(month, day, hour, minute)));

    [Theory]
    // Window 02:00 to 03:00. On the spring-forward day that local hour does not exist.
    [InlineData(3, 8, 7, 59, false)]    // 01:59 CST
    [InlineData(3, 8, 8, 0, false)]     // 03:00 CDT, end exclusive
    // Window 01:00 to 02:00 on the fall-back day: the hour happens twice.
    [InlineData(11, 1, 5, 59, false)]   // 00:59 CDT
    [InlineData(11, 1, 7, 59, false)]   // 01:59 CST
    [InlineData(11, 1, 8, 0, true)]     // 02:00 CST: after the fall back this hour exists once
    [InlineData(11, 1, 8, 59, true)]    // 02:59 CST
    public void Window_inside_the_skipped_hour_never_matches(int month, int day, int hour, int minute, bool expected)
        => Assert.Equal(expected, Quiet(120, 180, Utc(month, day, hour, minute)));

    [Theory]
    [InlineData(11, 1, 5, 59, false)]   // 00:59 CDT
    [InlineData(11, 1, 6, 0, true)]     // 01:00 CDT
    [InlineData(11, 1, 6, 59, true)]    // 01:59 CDT
    [InlineData(11, 1, 7, 0, true)]     // 01:00 CST (second time)
    [InlineData(11, 1, 7, 59, true)]    // 01:59 CST
    [InlineData(11, 1, 8, 0, false)]    // 02:00 CST
    public void Window_in_the_repeated_hour_matches_both_occurrences(int month, int day, int hour, int minute, bool expected)
        => Assert.Equal(expected, Quiet(60, 120, Utc(month, day, hour, minute)));

    [Fact]
    public void Different_time_zones_give_different_answers_for_the_same_instant()
    {
        var instant = Utc(11, 2, 7, 0);
        var window = new QuietHours { StartMinute = 1320, EndMinute = 420 };
        Assert.True(QuietHoursCheck.IsQuiet(window, instant, Fx.Zone("America/Chicago")));     // 01:00
        Assert.False(QuietHoursCheck.IsQuiet(window, instant, Fx.Zone("Europe/London")));      // 07:00
        Assert.False(QuietHoursCheck.IsQuiet(window, instant, Fx.Zone("Asia/Kolkata")));       // 12:30
    }
}
