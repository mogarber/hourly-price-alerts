namespace HourlyPriceAlerts.Core;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IFeedClient
{
    /// <summary>The 5-minute feed, sorted ascending and de-duplicated. Throws on request failure.</summary>
    Task<IReadOnlyList<Sample>> GetFiveMinuteFeedAsync(CancellationToken ct);

    /// <summary>ComEd's own current hour average. Null when the response has no usable row.</summary>
    Task<decimal?> GetCurrentHourAverageAsync(CancellationToken ct);
}

public interface IAlertStore
{
    Task<DeviceRecord?> GetDeviceAsync(string installId, CancellationToken ct);

    Task UpsertDeviceAsync(DeviceRecord device, CancellationToken ct);

    /// <summary>Deletes the device row and all of its rules. Does nothing if the device does not exist.</summary>
    Task DeleteDeviceAsync(string installId, CancellationToken ct);

    Task<IReadOnlyList<RuleRecord>> GetRulesAsync(string installId, CancellationToken ct);

    Task<RuleRecord?> GetRuleAsync(string installId, string ruleId, CancellationToken ct);

    /// <summary>Writes the whole rule row (rule fields and state).</summary>
    Task UpsertRuleAsync(RuleRecord rule, CancellationToken ct);

    /// <summary>Removes one rule. Does nothing if it does not exist.</summary>
    Task DeleteRuleAsync(string installId, string ruleId, CancellationToken ct);

    Task<IReadOnlyList<RuleRecord>> GetEnabledRulesAsync(CancellationToken ct);

    /// <summary>
    /// Updates only Armed and LastFiredAt. Returns false, without creating anything,
    /// if the rule no longer exists (for example the user deleted it while the poller ran).
    /// </summary>
    Task<bool> UpdateRuleStateAsync(string installId, string ruleId, RuleState state, CancellationToken ct);

    Task<PollState> GetPollStateAsync(CancellationToken ct);

    Task SetLastPollAtAsync(DateTimeOffset value, CancellationToken ct);

    Task SetLastSampleAtAsync(DateTimeOffset value, CancellationToken ct);
}

public enum PushResult
{
    Sent,
    /// <summary>APNs said the token is dead, so the device and its rules were deleted.</summary>
    DeviceRemoved,
    Failed,
}

/// <param name="RuleId">Null for the test push.</param>
/// <param name="CollapseId">The rule id, or "test".</param>
public sealed record PushMessage(
    string InstallId,
    string ApnsToken,
    ApnsEnvironment Environment,
    string Title,
    string Body,
    string CollapseId,
    string? RuleId,
    decimal? ValueCents);

public interface IPushSender
{
    Task<PushResult> SendAsync(PushMessage message, CancellationToken ct);
}
