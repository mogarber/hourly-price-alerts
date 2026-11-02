using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HourlyPriceAlerts.Core;

/// <summary>One run of the poller. The timer function calls <see cref="RunAsync"/> every minute.</summary>
public sealed class PollService(
    IFeedClient feed,
    IAlertStore store,
    IPushSender push,
    IClock clock,
    ILogger<PollService> logger)
{
    private const int MaxParallelDevices = 16;
    private static readonly decimal HourAverageTolerance = 0.1m;

    public async Task RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;

        // 1. Fetch the feed. On failure or an empty result, exit and let the next minute retry.
        IReadOnlyList<Sample> samples;
        try
        {
            samples = await feed.GetFiveMinuteFeedAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not fetch the ComEd 5-minute feed.");
            return;
        }

        if (samples.Count == 0)
        {
            logger.LogWarning("The ComEd 5-minute feed had no usable rows.");
            return;
        }

        // 2. Record the poll, then stop early if there is nothing new (no rule reads on most runs).
        var latest = samples[^1];
        var pollState = await store.GetPollStateAsync(ct);
        await store.SetLastPollAtAsync(now, ct);
        if (pollState.LastSampleAt is { } lastSampleAt && latest.Stamp <= lastSampleAt)
        {
            return;
        }

        // 3. Cross-check the hour bucket rule against ComEd's own number. Failure here is not fatal.
        await CheckHourAverageAsync(samples, latest, ct);

        // 4. Evaluate rules. Notifications go out before state is written, and lastSampleAt is written last.
        var rules = await store.GetEnabledRulesAsync(ct);
        var byDevice = rules.GroupBy(r => r.InstallId).ToList();
        var tally = new ConcurrentDictionary<string, int>();
        await Parallel.ForEachAsync(
            byDevice,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDevices, CancellationToken = ct },
            (group, token) => new ValueTask(EvaluateDeviceAsync(group.Key, group.ToList(), samples, now, tally, token)));

        await store.SetLastSampleAtAsync(latest.Stamp, ct);
        logger.LogInformation("Processed sample {Stamp:O} for {RuleCount} rules on {DeviceCount} devices. {Tally}",
            latest.Stamp, rules.Count, byDevice.Count,
            string.Join(", ", tally.OrderBy(t => t.Key).Select(t => $"{t.Key}={t.Value}")));
    }

    private async Task CheckHourAverageAsync(IReadOnlyList<Sample> samples, Sample latest, CancellationToken ct)
    {
        try
        {
            var comEd = await feed.GetCurrentHourAverageAsync(ct);
            var local = PriceMath.HourMean(samples, PriceMath.HourEnd(latest.Stamp));
            if (comEd is { } value && local.Count > 0 && Math.Abs(value - local.Mean) > HourAverageTolerance)
            {
                logger.LogWarning(
                    "Hour average mismatch: ComEd says {ComEd}, local mean of {Count} samples is {Local} (latest stamp {Stamp:O}).",
                    value, local.Count, local.Mean, latest.Stamp);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not fetch the ComEd current hour average.");
        }
    }

    private async Task EvaluateDeviceAsync(
        string installId,
        IReadOnlyList<RuleRecord> rules,
        IReadOnlyList<Sample> samples,
        DateTimeOffset now,
        ConcurrentDictionary<string, int> tally,
        CancellationToken ct)
    {
        DeviceRecord? device;
        TimeZoneInfo timeZone;
        try
        {
            device = await store.GetDeviceAsync(installId, ct);
            if (device is null)
            {
                logger.LogWarning("Skipping {Count} rules for unknown device {InstallId}.", rules.Count, installId);
                return;
            }

            timeZone = TimeZoneInfo.FindSystemTimeZoneById(device.TimeZone);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not load device {InstallId}; its rules were skipped.", installId);
            return;
        }

        foreach (var record in rules)
        {
            try
            {
                var result = AlertEngine.Evaluate(record.Rule, record.State, samples, now, timeZone);
                tally.AddOrUpdate(result.Outcome.ToString(), 1, (_, count) => count + 1);

                if (result.Notification is { } notification)
                {
                    var pushResult = await push.SendAsync(
                        new PushMessage(installId, device.ApnsToken, device.ApnsEnvironment,
                            notification.Title, notification.Body, record.Rule.Id, record.Rule.Id,
                            notification.ValueCents),
                        ct);
                    tally.AddOrUpdate($"Push{pushResult}", 1, (_, count) => count + 1);

                    // The device and all its rules are gone, so don't push to the dead token again.
                    if (pushResult == PushResult.DeviceRemoved)
                    {
                        return;
                    }

                    // Not delivered: keep the rule armed so the next sample retries instead of losing the alert.
                    if (pushResult == PushResult.Failed)
                    {
                        continue;
                    }
                }

                if (result.StateChanged)
                {
                    await store.UpdateRuleStateAsync(installId, record.Rule.Id, result.NewState, ct);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Rule {RuleId} on device {InstallId} failed.", record.Rule.Id, installId);
            }
        }
    }
}

public sealed class HealthService(IAlertStore store, IClock clock)
{
    public async Task<HealthResponse> GetAsync(CancellationToken ct)
    {
        var state = await store.GetPollStateAsync(ct);
        var stale = state.LastSampleAt is not { } latest || clock.UtcNow - latest > AlertEngine.StaleAfter;
        return new HealthResponse(state.LastSampleAt, state.LastPollAt, stale);
    }
}
