namespace HourlyPriceAlerts.Core;

/// <summary>One 5-minute price sample. Price is in cents per kWh and can be negative.</summary>
public sealed record Sample(DateTimeOffset Stamp, decimal Price);

public enum RuleKind { Above, Below, Move }

public enum RuleSignal { HourAverage, FiveMinute }

public enum Direction { Up, Down, Either }

public enum ApnsEnvironment { Sandbox, Production }

/// <summary>Minutes after local midnight (0 to 1439). Wraps past midnight when start is after end.</summary>
public sealed record QuietHours
{
    public required int StartMinute { get; init; }
    public required int EndMinute { get; init; }
}

/// <summary>An alert rule exactly as it appears in the API.</summary>
public sealed record Rule
{
    public required string Id { get; init; }
    public required RuleKind Kind { get; init; }
    public required RuleSignal Signal { get; init; }
    public required decimal ThresholdCents { get; init; }
    public Direction? Direction { get; init; }
    public int? WindowMinutes { get; init; }
    public required int CooldownMinutes { get; init; }
    public QuietHours? QuietHours { get; init; }
    public required bool Enabled { get; init; }
}

/// <summary>Mutable alert state kept per rule. A new rule starts armed.</summary>
public sealed record RuleState(bool Armed, DateTimeOffset? LastFiredAt)
{
    public static RuleState New { get; } = new(true, null);
}

/// <summary>A rule together with its state and owner, as stored.</summary>
public sealed record RuleRecord(string InstallId, Rule Rule, RuleState State);

public sealed record DeviceRecord(
    string InstallId,
    string SecretHash,
    string ApnsToken,
    ApnsEnvironment ApnsEnvironment,
    string TimeZone,
    DateTimeOffset? LastTestAt);

public sealed record PollState(DateTimeOffset? LastSampleAt, DateTimeOffset? LastPollAt);

/// <summary>Body of PUT /v1/devices/{installId}.</summary>
public sealed record DeviceRequest
{
    public required string ApnsToken { get; init; }
    public required ApnsEnvironment ApnsEnvironment { get; init; }
    public required string TimeZone { get; init; }
}

/// <summary>Response of PUT /v1/devices/{installId}: the device row without the secret hash.</summary>
public sealed record DeviceResponse(
    string InstallId,
    string ApnsToken,
    ApnsEnvironment ApnsEnvironment,
    string TimeZone,
    DateTimeOffset? LastTestAt);

public sealed record RulesResponse(IReadOnlyList<Rule> Rules);

public sealed record HealthResponse(DateTimeOffset? LastSampleAt, DateTimeOffset? LastPollAt, bool Stale);

public sealed record Notification(string Title, string Body, decimal ValueCents);

/// <summary>Why the engine decided what it did. Logged as a per-run count.</summary>
public enum EvaluationOutcome { Disabled, Stale, NoSignal, NotArmed, Rearmed, NotMet, Cooldown, QuietHours, Fired }

public sealed record EvaluationResult(
    RuleState NewState,
    bool StateChanged,
    Notification? Notification,
    EvaluationOutcome Outcome = EvaluationOutcome.NotMet);
