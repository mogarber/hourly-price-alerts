using System.Globalization;

namespace HourlyPriceAlerts.Core;

/// <summary>Builds notification titles and bodies. Invariant culture, 1 decimal, ASCII minus, times as "h:mm tt".</summary>
public static class NotificationText
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string Cents(decimal value) => PriceMath.Round(value).ToString("0.0", Culture) + "¢";

    public static string SignalName(RuleSignal signal)
        => signal == RuleSignal.HourAverage ? "Hourly average" : "5-minute price";

    /// <summary>Text for an above or below rule. <paramref name="latestStamp"/> is the latest sample's stamp.</summary>
    public static (string Title, string Body) Threshold(Rule rule, decimal value, DateTimeOffset latestStamp, TimeZoneInfo timeZone)
    {
        var word = rule.Kind == RuleKind.Above ? "above" : "below";
        var local = TimeZoneInfo.ConvertTime(latestStamp, timeZone);
        return (
            $"Price {word} {Cents(rule.ThresholdCents)}",
            $"{SignalName(rule.Signal)} is {Cents(value)} as of {local.ToString("h:mm tt", Culture)}.");
    }

    /// <summary>Text for a move rule. <paramref name="magnitude"/> is the size of the move, always shown unsigned.</summary>
    public static (string Title, string Body) Move(Rule rule, decimal value, decimal magnitude, bool up)
    {
        var word = up ? "up" : "down";
        var amount = Cents(magnitude);
        var suffix = rule.Signal == RuleSignal.HourAverage
            ? "from last hour"
            : $"in the last {rule.WindowMinutes} minutes";
        return (
            $"Price {word} {amount}",
            $"{SignalName(rule.Signal)} is {Cents(value)}, {word} {amount} {suffix}.");
    }
}
