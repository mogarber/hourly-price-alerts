namespace HourlyPriceAlerts.Core;

/// <summary>The pure alert engine. No I/O and no clock: everything it needs is passed in.</summary>
public static class AlertEngine
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(20);

    private const int MinCurrentHourSamples = 3;
    private const int MinPreviousHourSamples = 6;

    /// <summary>
    /// <paramref name="samples"/> must be sorted ascending with one sample per stamp, as <see cref="FeedParser"/> returns them.
    /// </summary>
    public static EvaluationResult Evaluate(
        Rule rule,
        RuleState state,
        IReadOnlyList<Sample> samples,
        DateTimeOffset nowUtc,
        TimeZoneInfo timeZone)
    {
        var unchanged = new EvaluationResult(state, false, null);

        // 1. Disabled rule.
        if (!rule.Enabled)
        {
            return unchanged with { Outcome = EvaluationOutcome.Disabled };
        }

        // 2. Stale guard.
        if (samples.Count == 0)
        {
            return unchanged with { Outcome = EvaluationOutcome.Stale };
        }

        var latest = samples[^1];
        if (nowUtc - latest.Stamp > StaleAfter)
        {
            return unchanged with { Outcome = EvaluationOutcome.Stale };
        }

        // 3. Signal.
        var reading = ReadSignal(rule, samples, latest);
        if (reading is null)
        {
            return unchanged with { Outcome = EvaluationOutcome.NoSignal };
        }

        // 4. Hysteresis.
        var (met, rearm) = Conditions(rule, reading);

        // 5. Not armed: re-arm if allowed, never notify on the same tick.
        if (!state.Armed)
        {
            return rearm
                ? new EvaluationResult(state with { Armed = true }, true, null, EvaluationOutcome.Rearmed)
                : unchanged with { Outcome = EvaluationOutcome.NotArmed };
        }

        if (!met)
        {
            return unchanged;
        }

        // 6. Armed and met: hold during cooldown and quiet hours.
        var inCooldown = state.LastFiredAt is { } last
            && nowUtc - last < TimeSpan.FromMinutes(rule.CooldownMinutes);
        if (inCooldown)
        {
            return unchanged with { Outcome = EvaluationOutcome.Cooldown };
        }

        if (QuietHoursCheck.IsQuiet(rule.QuietHours, nowUtc, timeZone))
        {
            return unchanged with { Outcome = EvaluationOutcome.QuietHours };
        }

        var (title, body) = rule.Kind == RuleKind.Move
            ? NotificationText.Move(rule, reading.Value, reading.Magnitude, reading.Up)
            : NotificationText.Threshold(rule, reading.Value, latest.Stamp, timeZone);

        return new EvaluationResult(
            new RuleState(false, nowUtc),
            true,
            new Notification(title, body, reading.Value),
            EvaluationOutcome.Fired);
    }

    /// <summary>
    /// Value is the price being reported (rounded). For move rules Magnitude is the size of the move in the rule's
    /// direction (it can be zero or negative) and Up says which way the notification should read.
    /// </summary>
    private sealed record Reading(decimal Value, decimal Magnitude, bool Up);

    private static Reading? ReadSignal(Rule rule, IReadOnlyList<Sample> samples, Sample latest)
    {
        if (rule.Kind != RuleKind.Move)
        {
            if (rule.Signal == RuleSignal.FiveMinute)
            {
                return new Reading(PriceMath.Round(latest.Price), 0m, false);
            }

            var current = PriceMath.HourMean(samples, PriceMath.HourEnd(latest.Stamp));
            return current.Count >= MinCurrentHourSamples ? new Reading(current.Mean, 0m, false) : null;
        }

        var direction = rule.Direction ?? Direction.Either;

        if (rule.Signal == RuleSignal.HourAverage)
        {
            var end = PriceMath.HourEnd(latest.Stamp);
            var current = PriceMath.HourMean(samples, end);
            var previous = PriceMath.HourMean(samples, end - TimeSpan.FromHours(1));
            if (current.Count < MinCurrentHourSamples || previous.Count < MinPreviousHourSamples)
            {
                return null;
            }

            // Both means are rounded first, so the delta matches what the user sees.
            var delta = current.Mean - previous.Mean;
            return Pick(direction, current.Mean, up: delta, down: -delta);
        }

        var windowStart = latest.Stamp - TimeSpan.FromMinutes(rule.WindowMinutes ?? 0);
        var window = samples.Where(s => s.Stamp >= windowStart && s.Stamp < latest.Stamp).ToList();
        if (window.Count < 1)
        {
            return null;
        }

        var price = PriceMath.Round(latest.Price);
        var upMagnitude = price - PriceMath.Round(window.Min(s => s.Price));
        var downMagnitude = PriceMath.Round(window.Max(s => s.Price)) - price;
        return Pick(direction, price, upMagnitude, downMagnitude);
    }

    /// <summary>A rule that watches one direction ignores moves the other way. Either uses the larger, a tie goes up.</summary>
    private static Reading Pick(Direction direction, decimal value, decimal up, decimal down) => direction switch
    {
        Direction.Up => new Reading(value, up, true),
        Direction.Down => new Reading(value, down, false),
        _ => up >= down ? new Reading(value, up, true) : new Reading(value, down, false),
    };

    private static (bool Met, bool Rearm) Conditions(Rule rule, Reading reading)
    {
        var t = rule.ThresholdCents;
        var h = rule.Signal == RuleSignal.HourAverage ? 0.5m : 1.0m;
        var v = reading.Value;

        return rule.Kind switch
        {
            RuleKind.Above => (v >= t, v <= t - h),
            RuleKind.Below => (v <= t, v >= t + h),
            _ => (reading.Magnitude >= t, reading.Magnitude < t / 2),
        };
    }
}
