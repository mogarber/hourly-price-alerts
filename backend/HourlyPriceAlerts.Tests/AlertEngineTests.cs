using HourlyPriceAlerts.Core;

namespace HourlyPriceAlerts.Tests;

public class AlertEngineTests
{
    private static readonly RuleState Armed = RuleState.New;

    private static EvaluationResult Eval(
        Rule rule, RuleState state, IReadOnlyList<Sample> samples, DateTimeOffset now, string zone = Fx.Chicago)
        => AlertEngine.Evaluate(rule, state, samples, now, Fx.Zone(zone));

    private static void AssertUnchanged(EvaluationResult result, RuleState original)
    {
        Assert.Equal(original, result.NewState);
        Assert.False(result.StateChanged);
        Assert.Null(result.Notification);
    }

    // ---- Step 1 and 2: disabled, stale, empty ----

    [Fact]
    public void Disabled_rule_is_returned_unchanged_even_when_the_condition_holds()
    {
        var rule = Fx.Rule(enabled: false, threshold: 5m);
        var result = Eval(rule, Armed, Fx.Run(14, 5, 50m), Fx.At(14, 6));
        AssertUnchanged(result, Armed);
    }

    [Fact]
    public void No_samples_means_unchanged()
        => AssertUnchanged(Eval(Fx.Rule(), Armed, [], Fx.At(14, 6)), Armed);

    [Fact]
    public void Stale_data_is_ignored_when_the_latest_sample_is_over_20_minutes_old()
    {
        var samples = Fx.Run(14, 5, 50m);
        AssertUnchanged(Eval(Fx.Rule(), Armed, samples, Fx.At(14, 25, 1)), Armed);
    }

    [Fact]
    public void Exactly_20_minutes_old_is_not_stale()
    {
        var samples = Fx.Run(14, 5, 50m);
        Assert.NotNull(Eval(Fx.Rule(), Armed, samples, Fx.At(14, 25)).Notification);
    }

    [Fact]
    public void Stale_guard_also_blocks_rearming()
    {
        var disarmed = new RuleState(false, Fx.At(10, 0));
        AssertUnchanged(Eval(Fx.Rule(), disarmed, Fx.Run(14, 5, 0m), Fx.At(15, 0)), disarmed);
    }

    // ---- Step 3: signals and minimum sample counts ----

    [Fact]
    public void Five_minute_threshold_uses_the_latest_sample_even_with_no_history()
    {
        var result = Eval(Fx.Rule(threshold: 10m), Armed, Fx.Run(14, 5, 10.0m), Fx.At(14, 6));
        Assert.NotNull(result.Notification);
        Assert.Equal(10.0m, result.Notification!.ValueCents);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Hour_average_threshold_needs_three_samples_in_the_current_hour(int count, bool fires)
    {
        var samples = Fx.Run(14, 5, Enumerable.Repeat(20m, count).ToArray());
        var result = Eval(Fx.Rule(signal: RuleSignal.HourAverage, threshold: 10m), Armed, samples, samples[^1].Stamp.AddMinutes(1));
        Assert.Equal(fires, result.Notification is not null);
    }

    [Fact]
    public void Hour_average_only_counts_the_bucket_of_the_latest_sample()
    {
        // Three high samples in the hour ending 14:00 must not count towards the hour ending 15:00.
        var samples = Fx.Run(13, 45, 50m, 50m, 50m, 50m); // 13:45 13:50 13:55 14:00
        samples.AddRange(Fx.Run(14, 5, 1m, 1m));
        var result = Eval(Fx.Rule(signal: RuleSignal.HourAverage, threshold: 10m), Armed, samples, Fx.At(14, 11));
        AssertUnchanged(result, Armed);
    }

    [Fact]
    public void Sample_on_the_hour_makes_the_closing_hour_current()
    {
        var samples = Fx.Run(13, 5, Enumerable.Repeat(12m, 12).ToArray()); // 13:05 .. 14:00
        var result = Eval(Fx.Rule(signal: RuleSignal.HourAverage, threshold: 10m), Armed, samples, Fx.At(14, 1));
        Assert.Equal(12.0m, result.Notification!.ValueCents);
    }

    [Fact]
    public void Hour_average_is_rounded_before_comparing()
    {
        // Mean 9.95 rounds (away from zero) to 10.0, which meets a 10.0 threshold.
        var samples = Fx.Run(14, 5, 9.9m, 10.0m, 9.95m, 9.95m); // mean 9.95
        var result = Eval(Fx.Rule(signal: RuleSignal.HourAverage, threshold: 10m), Armed, samples, Fx.At(14, 21));
        Assert.Equal(10.0m, result.Notification!.ValueCents);
    }

    [Fact]
    public void Hour_average_just_under_the_rounding_midpoint_does_not_fire()
    {
        var samples = Fx.Run(14, 5, 9.9m, 10.0m, 9.94m, 9.94m); // mean 9.945 -> 9.9
        var result = Eval(Fx.Rule(signal: RuleSignal.HourAverage, threshold: 10m), Armed, samples, Fx.At(14, 21));
        AssertUnchanged(result, Armed);
    }

    [Fact]
    public void Five_minute_price_is_rounded_before_comparing()
    {
        var result = Eval(Fx.Rule(threshold: 10m), Armed, Fx.Run(14, 5, 9.95m), Fx.At(14, 6));
        Assert.Equal(10.0m, result.Notification!.ValueCents);
    }

    // ---- Above and below ----

    [Fact]
    public void Above_fires_when_value_equals_threshold_and_disarms()
    {
        var now = Fx.At(14, 6);
        var result = Eval(Fx.Rule(threshold: 10m), Armed, Fx.Run(14, 5, 10.0m), now);

        Assert.Equal(new RuleState(false, now), result.NewState);
        Assert.True(result.StateChanged);
        Assert.NotNull(result.Notification);
    }

    [Fact]
    public void Above_does_not_fire_just_below_threshold()
        => AssertUnchanged(Eval(Fx.Rule(threshold: 10m), Armed, Fx.Run(14, 5, 9.9m), Fx.At(14, 6)), Armed);

    [Fact]
    public void Below_fires_when_value_equals_threshold()
    {
        var result = Eval(Fx.Rule(RuleKind.Below, threshold: 2m), Armed, Fx.Run(14, 5, 2.0m), Fx.At(14, 6));
        Assert.NotNull(result.Notification);
    }

    [Fact]
    public void Below_does_not_fire_just_above_threshold()
        => AssertUnchanged(Eval(Fx.Rule(RuleKind.Below, threshold: 2m), Armed, Fx.Run(14, 5, 2.1m), Fx.At(14, 6)), Armed);

    [Fact]
    public void Below_works_with_negative_thresholds()
    {
        var rule = Fx.Rule(RuleKind.Below, threshold: -1.0m);
        var result = Eval(rule, Armed, Fx.Run(14, 5, -1.4m), Fx.At(14, 6));
        Assert.Equal("Price below -1.0¢", result.Notification!.Title);
    }

    // ---- Step 4 and 5: hysteresis and re-arming ----

    [Theory]
    // signal, threshold, value, rearms
    [InlineData(RuleSignal.HourAverage, "10.0", "9.6", false)]   // t - 0.4: stays disarmed
    [InlineData(RuleSignal.HourAverage, "10.0", "9.5", true)]    // t - 0.5: re-arms
    [InlineData(RuleSignal.HourAverage, "10.0", "12.0", false)]  // still above: nothing happens
    [InlineData(RuleSignal.FiveMinute, "10.0", "9.1", false)]    // t - 0.9
    [InlineData(RuleSignal.FiveMinute, "10.0", "9.0", true)]     // t - 1.0
    public void Above_rearms_only_at_threshold_minus_hysteresis(RuleSignal signal, string t, string v, bool rearms)
    {
        var fired = Fx.At(13, 0);
        var disarmed = new RuleState(false, fired);
        var rule = Fx.Rule(signal: signal, threshold: decimal.Parse(t));
        var samples = Fx.Run(14, 5, decimal.Parse(v), decimal.Parse(v), decimal.Parse(v));

        var result = Eval(rule, disarmed, samples, Fx.At(14, 16));

        Assert.Null(result.Notification);
        Assert.Equal(rearms, result.StateChanged);
        Assert.Equal(rearms, result.NewState.Armed);
        Assert.Equal(fired, result.NewState.LastFiredAt);
    }

    [Theory]
    [InlineData(RuleSignal.HourAverage, "2.0", "2.4", false)]
    [InlineData(RuleSignal.HourAverage, "2.0", "2.5", true)]
    [InlineData(RuleSignal.HourAverage, "2.0", "1.0", false)]
    [InlineData(RuleSignal.FiveMinute, "2.0", "2.9", false)]
    [InlineData(RuleSignal.FiveMinute, "2.0", "3.0", true)]
    public void Below_rearms_only_at_threshold_plus_hysteresis(RuleSignal signal, string t, string v, bool rearms)
    {
        var disarmed = new RuleState(false, Fx.At(13, 0));
        var rule = Fx.Rule(RuleKind.Below, signal, decimal.Parse(t));
        var samples = Fx.Run(14, 5, decimal.Parse(v), decimal.Parse(v), decimal.Parse(v));

        var result = Eval(rule, disarmed, samples, Fx.At(14, 16));

        Assert.Null(result.Notification);
        Assert.Equal(rearms, result.NewState.Armed);
        Assert.Equal(rearms, result.StateChanged);
    }

    [Fact]
    public void Rearming_never_notifies_on_that_tick_even_if_cooldown_and_quiet_hours_allow()
    {
        var disarmed = new RuleState(false, Fx.At(1, 0));
        var result = Eval(Fx.Rule(threshold: 10m, cooldown: 0), disarmed, Fx.Run(14, 5, 5m), Fx.At(14, 6));

        Assert.Null(result.Notification);
        Assert.True(result.NewState.Armed);
        Assert.True(result.StateChanged);
    }

    [Fact]
    public void Rearmed_rule_can_fire_on_a_later_tick()
    {
        var rule = Fx.Rule(threshold: 10m, cooldown: 0);
        var step1 = Eval(rule, new RuleState(false, Fx.At(1, 0)), Fx.Run(14, 5, 5m), Fx.At(14, 6));
        var step2 = Eval(rule, step1.NewState, Fx.Run(14, 10, 11m), Fx.At(14, 11));

        Assert.NotNull(step2.Notification);
        Assert.False(step2.NewState.Armed);
    }

    [Fact]
    public void Disarmed_rule_with_condition_still_met_stays_silent_and_unchanged()
    {
        var disarmed = new RuleState(false, Fx.At(14, 0));
        AssertUnchanged(Eval(Fx.Rule(threshold: 10m, cooldown: 0), disarmed, Fx.Run(14, 5, 50m), Fx.At(14, 6)), disarmed);
    }

    [Fact]
    public void Armed_rule_whose_condition_is_not_met_is_unchanged()
        => AssertUnchanged(Eval(Fx.Rule(threshold: 10m), Armed, Fx.Run(14, 5, 3m), Fx.At(14, 6)), Armed);

    // ---- Step 6: cooldown ----

    [Fact]
    public void Cooldown_holds_without_changing_state_and_keeps_the_rule_armed()
    {
        var armed = new RuleState(true, Fx.At(13, 30));
        var result = Eval(Fx.Rule(threshold: 10m, cooldown: 60), armed, Fx.Run(14, 5, 50m), Fx.At(14, 6));
        AssertUnchanged(result, armed);
    }

    [Fact]
    public void Held_alert_fires_once_the_cooldown_is_over_if_still_true()
    {
        var armed = new RuleState(true, Fx.At(13, 0));
        var rule = Fx.Rule(threshold: 10m, cooldown: 60);

        AssertUnchanged(Eval(rule, armed, Fx.Run(13, 55, 50m), Fx.At(13, 56)), armed);   // 56 minutes since
        var result = Eval(rule, armed, Fx.Run(14, 0, 50m), Fx.At(14, 0));                 // exactly 60 minutes
        Assert.NotNull(result.Notification);
        Assert.Equal(new RuleState(false, Fx.At(14, 0)), result.NewState);
    }

    [Fact]
    public void Cooldown_of_zero_never_holds()
    {
        var armed = new RuleState(true, Fx.At(14, 6));
        Assert.NotNull(Eval(Fx.Rule(threshold: 10m, cooldown: 0), armed, Fx.Run(14, 5, 50m), Fx.At(14, 6)).Notification);
    }

    [Fact]
    public void Rule_that_never_fired_ignores_cooldown()
        => Assert.NotNull(Eval(Fx.Rule(threshold: 10m, cooldown: 1440), Armed, Fx.Run(14, 5, 50m), Fx.At(14, 6)).Notification);

    // ---- Step 6: quiet hours ----

    [Fact]
    public void Quiet_hours_hold_without_changing_state()
    {
        // 07:00Z on Nov 2 is 01:00 in Chicago, inside 22:00 to 07:00.
        var rule = Fx.Rule(threshold: 10m, quiet: new QuietHours { StartMinute = 1320, EndMinute = 420 });
        AssertUnchanged(Eval(rule, Armed, Fx.Run(7, 0, 50m), Fx.At(7, 1)), Armed);
    }

    [Fact]
    public void Quiet_hours_end_is_exclusive_and_the_held_alert_then_fires()
    {
        var rule = Fx.Rule(threshold: 10m, quiet: new QuietHours { StartMinute = 1320, EndMinute = 420 });

        AssertUnchanged(Eval(rule, Armed, Fx.Run(12, 55, 50m), Fx.At(12, 59)), Armed);    // 06:59 local
        var result = Eval(rule, Armed, Fx.Run(13, 0, 50m), Fx.At(13, 0));                 // 07:00 local
        Assert.NotNull(result.Notification);
    }

    [Fact]
    public void Quiet_hours_use_the_device_time_zone()
    {
        var rule = Fx.Rule(threshold: 10m, quiet: new QuietHours { StartMinute = 1320, EndMinute = 420 });
        var samples = Fx.Run(7, 0, 50m);

        Assert.Null(Eval(rule, Armed, samples, Fx.At(7, 1), "America/Chicago").Notification);        // 01:01
        Assert.NotNull(Eval(rule, Armed, samples, Fx.At(7, 1), "Asia/Kolkata").Notification);         // 12:31
    }

    [Fact]
    public void Quiet_hours_with_equal_start_and_end_mean_no_quiet_hours()
    {
        var rule = Fx.Rule(threshold: 10m, quiet: new QuietHours { StartMinute = 60, EndMinute = 60 });
        Assert.NotNull(Eval(rule, Armed, Fx.Run(7, 0, 50m), Fx.At(7, 1)).Notification);   // 01:01 local
    }

    [Fact]
    public void Quiet_hours_across_a_dst_change_use_local_wall_time()
    {
        // Spring forward in Chicago: 2026-03-08 02:00 CST becomes 03:00 CDT (08:00Z).
        var rule = Fx.Rule(threshold: 10m, quiet: new QuietHours { StartMinute = 1320, EndMinute = 420 });
        var before = new DateTimeOffset(2026, 3, 8, 11, 55, 0, TimeSpan.Zero);   // 06:55 CDT
        var after = new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.Zero);     // 07:00 CDT

        var held = Eval(rule, Armed, [new Sample(before, 50m)], before.AddMinutes(4));
        var fired = Eval(rule, Armed, [new Sample(after, 50m)], after);

        Assert.Null(held.Notification);
        Assert.NotNull(fired.Notification);
    }

    // ---- Move rules ----

    private static List<Sample> FiveMinuteMoveSamples(params decimal[] prices) => Fx.Run(13, 50, prices);

    [Fact]
    public void Five_minute_move_up_uses_latest_minus_minimum_of_the_window_and_matches_the_spec_text()
    {
        // 13:50..14:20; latest 12.0; min in [13:50, 14:20) is 4.0 -> up 8.0.
        var samples = FiveMinuteMoveSamples(4.0m, 5.0m, 6.0m, 7.0m, 8.0m, 9.0m, 12.0m);
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 5m, Direction.Either, window: 30);

        var result = Eval(rule, Armed, samples, Fx.At(14, 21));

        Assert.Equal("Price up 8.0¢", result.Notification!.Title);
        Assert.Equal("5-minute price is 12.0¢, up 8.0¢ in the last 30 minutes.", result.Notification.Body);
        Assert.Equal(12.0m, result.Notification.ValueCents);
    }

    [Fact]
    public void Five_minute_move_down_uses_maximum_minus_latest()
    {
        var samples = FiveMinuteMoveSamples(20m, 18m, 15m, 12m, 10m, 8m, 5m);
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 5m, Direction.Down, window: 30);

        var result = Eval(rule, Armed, samples, Fx.At(14, 21));

        Assert.Equal("Price down 15.0¢", result.Notification!.Title);
    }

    [Fact]
    public void Window_start_is_inclusive_and_older_samples_are_ignored()
    {
        // Latest 14:20 = 10.0. The 13:45 sample (-50) is outside a 30 minute window; the 13:50 sample (6.0) is inside.
        var samples = new List<Sample> { new(Fx.At(13, 45), -50m), new(Fx.At(13, 50), 6.0m), new(Fx.At(14, 20), 10.0m) };
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 4m, Direction.Up, window: 30);

        var result = Eval(rule, Armed, samples, Fx.At(14, 21));

        Assert.Equal("Price up 4.0¢", result.Notification!.Title);
    }

    [Fact]
    public void Window_end_is_exclusive_so_the_latest_sample_is_not_compared_with_itself()
    {
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 1m, Direction.Either, window: 30);
        // Only the latest sample exists: no sample in [latest - window, latest), so the signal is unavailable.
        AssertUnchanged(Eval(rule, Armed, Fx.Run(14, 20, 9m), Fx.At(14, 21)), Armed);
    }

    [Fact]
    public void Five_minute_move_needs_at_least_one_sample_in_the_window()
    {
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 1m, Direction.Either, window: 5);
        var samples = new List<Sample> { new(Fx.At(14, 0), 0m), new(Fx.At(14, 20), 50m) };   // gap
        AssertUnchanged(Eval(rule, Armed, samples, Fx.At(14, 21)), Armed);
    }

    [Fact]
    public void Either_with_equal_up_and_down_magnitudes_goes_up()
    {
        var samples = FiveMinuteMoveSamples(0.0m, 10.0m, 5.0m);   // up 5.0, down 5.0
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 5m, Direction.Either, window: 30);

        var result = Eval(rule, Armed, samples, Fx.At(14, 1));

        Assert.Equal("Price up 5.0¢", result.Notification!.Title);
    }

    [Fact]
    public void Either_picks_the_larger_magnitude()
    {
        var samples = FiveMinuteMoveSamples(0.0m, 10.0m, 3.0m);   // up 3.0, down 7.0
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 5m, Direction.Either, window: 30);

        Assert.Equal("Price down 7.0¢", Eval(rule, Armed, samples, Fx.At(14, 1)).Notification!.Title);
    }

    [Fact]
    public void Up_rule_ignores_down_moves_and_down_rule_ignores_up_moves()
    {
        var drop = FiveMinuteMoveSamples(10m, 2m);
        var rise = FiveMinuteMoveSamples(2m, 10m);
        var up = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 5m, Direction.Up, window: 30);
        var down = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 5m, Direction.Down, window: 30);

        Assert.Null(Eval(up, Armed, drop, Fx.At(13, 56)).Notification);
        Assert.NotNull(Eval(down, Armed, drop, Fx.At(13, 56)).Notification);
        Assert.Null(Eval(down, Armed, rise, Fx.At(13, 56)).Notification);
        Assert.NotNull(Eval(up, Armed, rise, Fx.At(13, 56)).Notification);
    }

    [Fact]
    public void Move_threshold_is_met_when_magnitude_equals_it()
    {
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 5.0m, Direction.Up, window: 30);
        Assert.NotNull(Eval(rule, Armed, FiveMinuteMoveSamples(1.0m, 6.0m), Fx.At(13, 56)).Notification);
        Assert.Null(Eval(rule, Armed, FiveMinuteMoveSamples(1.0m, 5.9m), Fx.At(13, 56)).Notification);
    }

    [Theory]
    [InlineData("1.9", true)]    // below t/2 = 2.0: re-arms
    [InlineData("2.0", false)]   // not below t/2
    [InlineData("3.9", false)]   // between t/2 and t: stays disarmed
    [InlineData("-1.0", true)]   // the price moved the other way
    public void Move_rearms_when_magnitude_falls_below_half_the_threshold(string magnitude, bool rearms)
    {
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 4m, Direction.Up, window: 30);
        var disarmed = new RuleState(false, Fx.At(10, 0));
        var samples = FiveMinuteMoveSamples(5m, 5m + decimal.Parse(magnitude));

        var result = Eval(rule, disarmed, samples, Fx.At(13, 56));

        Assert.Null(result.Notification);
        Assert.Equal(rearms, result.NewState.Armed);
        Assert.Equal(rearms, result.StateChanged);
    }

    private static List<Sample> HourSamples(decimal previousHour, int previousCount, decimal currentHour, int currentCount)
    {
        // The previous hour is the one ending 14:00; the current hour starts at 14:05.
        var samples = Fx.Run(14, 0, Enumerable.Repeat(previousHour, 1).ToArray()).ToList();   // 14:00 closes the previous hour
        samples.InsertRange(0, Fx.Run(13, 5, Enumerable.Repeat(previousHour, previousCount - 1).ToArray()));
        samples.AddRange(Fx.Run(14, 5, Enumerable.Repeat(currentHour, currentCount).ToArray()));
        return samples;
    }

    [Fact]
    public void Hour_average_move_up_compares_with_the_previous_hour_and_matches_the_spec_text()
    {
        var samples = HourSamples(5.9m, 12, 9.1m, 3);
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.HourAverage, 3m, Direction.Either);

        var result = Eval(rule, Armed, samples, Fx.At(14, 16));

        Assert.Equal("Price up 3.2¢", result.Notification!.Title);
        Assert.Equal("Hourly average is 9.1¢, up 3.2¢ from last hour.", result.Notification.Body);
        Assert.Equal(9.1m, result.Notification.ValueCents);
    }

    [Fact]
    public void Hour_average_move_down_matches_the_spec_text()
    {
        var samples = HourSamples(5.3m, 12, 2.1m, 3);
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.HourAverage, 3m, Direction.Down);

        var result = Eval(rule, Armed, samples, Fx.At(14, 16));

        Assert.Equal("Price down 3.2¢", result.Notification!.Title);
        Assert.Equal("Hourly average is 2.1¢, down 3.2¢ from last hour.", result.Notification.Body);
    }

    [Fact]
    public void Hour_average_move_respects_direction()
    {
        var rising = HourSamples(5.0m, 12, 9.0m, 3);
        var up = Fx.Rule(RuleKind.Move, RuleSignal.HourAverage, 3m, Direction.Up);
        var down = Fx.Rule(RuleKind.Move, RuleSignal.HourAverage, 3m, Direction.Down);

        Assert.NotNull(Eval(up, Armed, rising, Fx.At(14, 16)).Notification);
        Assert.Null(Eval(down, Armed, rising, Fx.At(14, 16)).Notification);
    }

    [Fact]
    public void Hour_average_move_threshold_is_inclusive()
    {
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.HourAverage, 3.2m, Direction.Up);
        Assert.NotNull(Eval(rule, Armed, HourSamples(5.9m, 12, 9.1m, 3), Fx.At(14, 16)).Notification);
        Assert.Null(Eval(rule, Armed, HourSamples(5.9m, 12, 9.0m, 3), Fx.At(14, 16)).Notification);
    }

    [Theory]
    [InlineData(5, 3, false)]   // previous hour needs 6
    [InlineData(6, 3, true)]
    [InlineData(12, 2, false)]  // current hour needs 3
    [InlineData(12, 3, true)]
    public void Hour_average_move_needs_6_previous_and_3_current_samples(int previous, int current, bool fires)
    {
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.HourAverage, 1m, Direction.Up);
        var samples = HourSamples(1m, previous, 20m, current);

        var result = Eval(rule, Armed, samples, samples[^1].Stamp.AddMinutes(1));

        Assert.Equal(fires, result.Notification is not null);
    }

    [Fact]
    public void Hour_average_move_ignores_window_minutes()
    {
        var withWindow = Fx.Rule(RuleKind.Move, RuleSignal.HourAverage, 3m, Direction.Up, window: 15);
        var without = withWindow with { WindowMinutes = null };
        var samples = HourSamples(5.9m, 12, 9.1m, 3);

        Assert.Equal(Eval(without, Armed, samples, Fx.At(14, 16)), Eval(withWindow, Armed, samples, Fx.At(14, 16)));
    }

    [Fact]
    public void Hour_average_move_subtracts_the_rounded_means()
    {
        // Previous mean 5.04 -> 5.0, current mean 8.46 -> 8.5: delta 3.5, as the user would compute from the screen.
        var samples = HourSamples(5.04m, 12, 8.46m, 3);
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.HourAverage, 3.5m, Direction.Up);

        Assert.Equal("Price up 3.5¢", Eval(rule, Armed, samples, Fx.At(14, 16)).Notification!.Title);
    }

    // ---- stateChanged accuracy ----

    [Fact]
    public void State_changed_is_true_only_when_armed_or_last_fired_changed()
    {
        var rule = Fx.Rule(threshold: 10m, cooldown: 0);

        Assert.True(Eval(rule, Armed, Fx.Run(14, 5, 50m), Fx.At(14, 6)).StateChanged);                          // fired
        Assert.True(Eval(rule, new RuleState(false, Fx.At(1, 0)), Fx.Run(14, 5, 1m), Fx.At(14, 6)).StateChanged); // re-armed
        Assert.False(Eval(rule, Armed, Fx.Run(14, 5, 1m), Fx.At(14, 6)).StateChanged);                          // not met
        Assert.False(Eval(rule, new RuleState(false, Fx.At(1, 0)), Fx.Run(14, 5, 9.5m), Fx.At(14, 6)).StateChanged); // disarmed, no re-arm
        Assert.False(Eval(rule with { Enabled = false }, Armed, Fx.Run(14, 5, 50m), Fx.At(14, 6)).StateChanged);
    }

    [Fact]
    public void State_changed_matches_a_real_difference_between_old_and_new_state()
    {
        var rule = Fx.Rule(threshold: 10m, cooldown: 60);
        var scenarios = new (RuleState State, decimal Price, DateTimeOffset Now)[]
        {
            (Armed, 50m, Fx.At(14, 6)),
            (Armed, 1m, Fx.At(14, 6)),
            (new RuleState(true, Fx.At(14, 0)), 50m, Fx.At(14, 6)),      // cooldown hold
            (new RuleState(false, Fx.At(14, 0)), 1m, Fx.At(14, 6)),      // re-arm
            (new RuleState(false, Fx.At(14, 0)), 9.5m, Fx.At(14, 6)),    // nothing
            (new RuleState(false, Fx.At(14, 0)), 50m, Fx.At(14, 6)),     // nothing
        };

        foreach (var (state, price, now) in scenarios)
        {
            var result = Eval(rule, state, Fx.Run(14, 5, price), now);
            Assert.Equal(result.NewState != state, result.StateChanged);
        }
    }

    [Fact]
    public void Evaluate_does_not_read_the_system_clock()
    {
        // The same inputs give the same output regardless of when the test runs.
        var samples = Fx.Run(14, 5, 50m);
        var a = Eval(Fx.Rule(), Armed, samples, Fx.At(14, 6));
        var b = Eval(Fx.Rule(), Armed, samples, Fx.At(14, 6));
        Assert.Equal(a, b);
    }
}
