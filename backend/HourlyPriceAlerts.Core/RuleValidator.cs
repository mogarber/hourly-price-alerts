using System.Globalization;
using System.Text.RegularExpressions;

namespace HourlyPriceAlerts.Core;

public static partial class RuleValidator
{
    public const int MaxRulesPerDevice = 10;

    /// <summary>A lowercase UUID in the 8-4-4-4-12 form, used for install ids and rule ids.</summary>
    public static bool IsLowercaseUuid(string? value)
        => value is not null && Guid.TryParseExact(value, "D", out _) && value == value.ToLowerInvariant();

    /// <summary>Returns a human readable error, or null when the rule is valid.</summary>
    public static string? Validate(Rule rule, string routeRuleId)
    {
        if (!IsLowercaseUuid(rule.Id))
        {
            return "Rule id must be a lowercase UUID.";
        }

        if (rule.Id != routeRuleId)
        {
            return "Rule id must match the id in the URL.";
        }

        if (!Enum.IsDefined(rule.Kind))
        {
            return "Rule kind must be above, below or move.";
        }

        if (!Enum.IsDefined(rule.Signal))
        {
            return "Rule signal must be hourAverage or fiveMinute.";
        }

        var (min, max) = rule.Kind == RuleKind.Move ? (0.1m, 200m) : (-100m, 1000m);
        if (rule.ThresholdCents < min || rule.ThresholdCents > max)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"thresholdCents must be between {min} and {max}.");
        }

        if (Math.Round(rule.ThresholdCents, 1) != rule.ThresholdCents)
        {
            return "thresholdCents can have at most 1 decimal.";
        }

        if (rule.Kind == RuleKind.Move)
        {
            if (rule.Direction is not { } direction || !Enum.IsDefined(direction))
            {
                return "direction is required for move rules and must be up, down or either.";
            }
        }
        else if (rule.Direction is not null)
        {
            return "direction must be null unless kind is move.";
        }

        if (rule.Kind == RuleKind.Move && rule.Signal == RuleSignal.FiveMinute)
        {
            if (rule.WindowMinutes is not { } window || window < 5 || window > 180 || window % 5 != 0)
            {
                return "windowMinutes is required for 5-minute moves: a multiple of 5 from 5 to 180.";
            }
        }
        else if (rule.WindowMinutes is not null)
        {
            return "windowMinutes must be null unless kind is move and signal is fiveMinute.";
        }

        if (rule.CooldownMinutes < 0 || rule.CooldownMinutes > 1440)
        {
            return "cooldownMinutes must be between 0 and 1440.";
        }

        if (rule.QuietHours is { } quiet
            && (quiet.StartMinute < 0 || quiet.StartMinute > 1439 || quiet.EndMinute < 0 || quiet.EndMinute > 1439))
        {
            return "quietHours startMinute and endMinute must be between 0 and 1439.";
        }

        return null;
    }

    /// <summary>Returns a human readable error, or null when the request is valid.</summary>
    public static string? Validate(DeviceRequest request)
    {
        if (!ApnsTokenPattern().IsMatch(request.ApnsToken))
        {
            return "apnsToken must be 64 to 200 hex characters.";
        }

        if (!Enum.IsDefined(request.ApnsEnvironment))
        {
            return "apnsEnvironment must be sandbox or production.";
        }

        if (!IsValidTimeZone(request.TimeZone))
        {
            return "timeZone must be a valid IANA time zone id.";
        }

        return null;
    }

    public static bool IsValidTimeZone(string? id)
        => !string.IsNullOrEmpty(id)
            && id.Length <= 64
            && TimeZoneIdPattern().IsMatch(id)
            && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    [GeneratedRegex("^[0-9a-fA-F]{64,200}\\z")]
    private static partial Regex ApnsTokenPattern();

    [GeneratedRegex("^[A-Za-z0-9_+\\-/]+\\z")]
    private static partial Regex TimeZoneIdPattern();
}
