using HourlyPriceAlerts.Core;
using Microsoft.Extensions.Logging;

namespace HourlyPriceAlerts.Tests;

public class PollServiceTests
{
    private const string InstallId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string OtherInstallId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

    private readonly CallLog _log = new();
    private readonly InMemoryStore _store;
    private readonly FakePushSender _push;
    private readonly FakeFeedClient _feed;
    private readonly FakeClock _clock = new(Fx.At(14, 6));
    private readonly ListLogger<PollService> _logger = new();
    private readonly PollService _poller;

    public PollServiceTests()
    {
        _store = new InMemoryStore(_log);
        _push = new FakePushSender(_log);
        _feed = new FakeFeedClient(_log);
        _poller = new PollService(_feed, _store, _push, _clock, _logger);
        AddDevice(InstallId);
        _feed.Samples = Fx.Run(14, 5, 50m);
        _feed.HourAverage = 50m;
    }

    private static string RuleId(int n) => $"{n:x8}-0000-4000-8000-000000000000";

    private void AddDevice(string installId)
        => _store.Devices[installId] = new DeviceRecord(installId, "hash", new string('a', 64), ApnsEnvironment.Sandbox, Fx.Chicago, null);

    private void AddRule(int n, string installId = InstallId, Rule? rule = null, RuleState? state = null)
        => _store.Rules[(installId, RuleId(n))] = new RuleRecord(installId, rule ?? Fx.Rule(threshold: 10m, id: RuleId(n)), state ?? RuleState.New);

    private Task Run() => _poller.RunAsync(default);

    [Fact]
    public async Task Fetches_the_feed_records_the_poll_and_stores_the_new_sample_stamp()
    {
        await Run();

        Assert.Equal(_clock.UtcNow, _store.Poll.LastPollAt);
        Assert.Equal(Fx.At(14, 5), _store.Poll.LastSampleAt);
    }

    [Fact]
    public async Task Without_a_new_sample_no_rules_are_read_and_nothing_is_sent()
    {
        AddRule(1);
        _store.Poll = new PollState(Fx.At(14, 5), null);

        await Run();

        Assert.DoesNotContain("GetEnabledRules", _log.Entries);
        Assert.Equal(0, _feed.HourAverageCalls);
        Assert.Empty(_push.Sent);
        Assert.Equal(_clock.UtcNow, _store.Poll.LastPollAt);       // the poll itself is still recorded
        Assert.Equal(Fx.At(14, 5), _store.Poll.LastSampleAt);
    }

    [Fact]
    public async Task An_older_latest_stamp_also_skips_the_rule_reads()
    {
        _store.Poll = new PollState(Fx.At(14, 10), null);
        await Run();
        Assert.DoesNotContain("GetEnabledRules", _log.Entries);
    }

    [Fact]
    public async Task A_newer_sample_reads_the_rules_and_notifies()
    {
        AddRule(1);
        _store.Poll = new PollState(Fx.At(14, 0), null);

        await Run();

        var message = Assert.Single(_push.Sent);
        Assert.Equal(RuleId(1), message.RuleId);
        Assert.Equal(RuleId(1), message.CollapseId);
        Assert.Equal(50.0m, message.ValueCents);
        Assert.Equal("Price above 10.0¢", message.Title);
        Assert.Equal(new string('a', 64), message.ApnsToken);
        Assert.Equal(new RuleState(false, _clock.UtcNow), _store.Rules[(InstallId, RuleId(1))].State);
    }

    [Fact]
    public async Task Notification_time_uses_the_devices_time_zone()
    {
        _store.Devices[InstallId] = _store.Devices[InstallId] with { TimeZone = "Asia/Kolkata" };
        AddRule(1);

        await Run();

        Assert.Contains("as of 7:35 PM.", Assert.Single(_push.Sent).Body);   // 14:05Z is 19:35 IST
    }

    [Fact]
    public async Task Disabled_rules_are_not_evaluated()
    {
        AddRule(1, rule: Fx.Rule(threshold: 10m, enabled: false, id: RuleId(1)));
        await Run();
        Assert.Empty(_push.Sent);
    }

    [Fact]
    public async Task Rule_state_is_only_written_when_it_changed()
    {
        AddRule(1, rule: Fx.Rule(threshold: 99m, id: RuleId(1)));   // not met
        AddRule(2);                                                // fires

        await Run();

        Assert.DoesNotContain($"UpdateRuleState:{RuleId(1)}", _log.Entries);
        Assert.Contains($"UpdateRuleState:{RuleId(2)}", _log.Entries);
    }

    [Fact]
    public async Task Sends_before_writing_state_and_writes_the_sample_stamp_last()
    {
        AddRule(1);

        await Run();

        var entries = _log.Entries.ToList();
        var push = entries.IndexOf($"Push:{RuleId(1)}");
        var state = entries.IndexOf($"UpdateRuleState:{RuleId(1)}");
        var sample = entries.IndexOf("SetLastSampleAt");
        Assert.True(push >= 0 && push < state, "push must come before the state write");
        Assert.True(state < sample, "the state write must come before lastSampleAt");
        Assert.Equal(entries.Count - 1, sample);
    }

    [Fact]
    public async Task Order_of_the_early_steps_is_feed_then_poll_time_then_hour_average_then_rules()
    {
        AddRule(1);
        await Run();

        var entries = _log.Entries.ToList();
        Assert.True(entries.IndexOf("FetchFeed") < entries.IndexOf("SetLastPollAt"));
        Assert.True(entries.IndexOf("SetLastPollAt") < entries.IndexOf("FetchHourAverage"));
        Assert.True(entries.IndexOf("FetchHourAverage") < entries.IndexOf("GetEnabledRules"));
    }

    [Fact]
    public async Task A_failing_push_does_not_stop_other_rules_and_the_sample_is_still_recorded()
    {
        AddRule(1);
        AddRule(2);
        AddRule(3);
        _push.FailFor.Add(RuleId(2));

        await Run();

        Assert.Equal(new[] { RuleId(1), RuleId(3) }.Order(), _push.Sent.Select(m => m.RuleId!).Order());
        Assert.Equal(Fx.At(14, 5), _store.Poll.LastSampleAt);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error);
        // The failed rule never reached its state write, so it will be retried on the next sample.
        Assert.True(_store.Rules[(InstallId, RuleId(2))].State.Armed);
        Assert.False(_store.Rules[(InstallId, RuleId(1))].State.Armed);
        Assert.False(_store.Rules[(InstallId, RuleId(3))].State.Armed);
    }

    [Fact]
    public async Task An_undelivered_push_keeps_the_rule_armed_and_the_next_sample_retries()
    {
        AddRule(1);
        _push.Result = PushResult.Failed;

        await Run();

        var afterFailure = _store.Rules[(InstallId, RuleId(1))].State;
        Assert.True(afterFailure.Armed);
        Assert.Null(afterFailure.LastFiredAt);
        Assert.Empty(_push.Sent);

        _push.Result = PushResult.Sent;
        _feed.Samples = Fx.Run(14, 10, 50m);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await Run();

        Assert.Single(_push.Sent);
        Assert.False(_store.Rules[(InstallId, RuleId(1))].State.Armed);
    }

    [Fact]
    public async Task A_removed_device_gets_no_more_pushes_in_the_same_run()
    {
        AddRule(1);
        AddRule(2);
        _push.Result = PushResult.DeviceRemoved;

        await Run();

        Assert.Single(_log.Entries, entry => entry.StartsWith("Push:"));
    }

    [Fact]
    public async Task A_failing_state_write_does_not_stop_other_rules()
    {
        AddRule(1);
        AddRule(2);
        _store.FailStateUpdateFor.Add(RuleId(1));

        await Run();

        Assert.Equal(2, _push.Sent.Count);
        Assert.False(_store.Rules[(InstallId, RuleId(2))].State.Armed);
        Assert.Equal(Fx.At(14, 5), _store.Poll.LastSampleAt);
    }

    [Fact]
    public async Task A_broken_device_does_not_stop_other_devices()
    {
        AddDevice(OtherInstallId);
        _store.Devices[OtherInstallId] = _store.Devices[OtherInstallId] with { TimeZone = "Not/AZone" };
        AddRule(1, OtherInstallId);
        AddRule(2);

        await Run();

        Assert.Equal(RuleId(2), Assert.Single(_push.Sent).RuleId);
    }

    [Fact]
    public async Task Rules_of_an_unknown_device_are_skipped()
    {
        AddRule(1, "cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        AddRule(2);

        await Run();

        Assert.Equal(RuleId(2), Assert.Single(_push.Sent).RuleId);
    }

    [Fact]
    public async Task Feed_failure_logs_and_changes_nothing()
    {
        AddRule(1);
        _feed.FeedError = new HttpRequestException("boom");

        await Run();

        Assert.Null(_store.Poll.LastPollAt);
        Assert.Null(_store.Poll.LastSampleAt);
        Assert.Empty(_push.Sent);
        Assert.DoesNotContain("GetEnabledRules", _log.Entries);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Empty_feed_logs_and_changes_nothing()
    {
        _feed.Samples = [];

        await Run();

        Assert.Equal(new PollState(null, null), _store.Poll);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Hour_average_mismatch_over_a_tenth_of_a_cent_logs_a_warning()
    {
        _feed.Samples = Fx.Run(14, 5, 5.0m, 5.0m, 5.0m);   // local mean 5.0
        _feed.HourAverage = 5.2m;

        await Run();

        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("mismatch"));
    }

    [Theory]
    [InlineData("5.1")]
    [InlineData("4.9")]
    [InlineData("5.0")]
    public async Task Hour_average_within_a_tenth_of_a_cent_is_quiet(string comEd)
    {
        _feed.Samples = Fx.Run(14, 5, 5.0m, 5.0m, 5.0m);
        _feed.HourAverage = decimal.Parse(comEd);

        await Run();

        Assert.DoesNotContain(_logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Hour_average_failure_does_not_stop_the_run()
    {
        AddRule(1);
        _feed.HourAverageError = new HttpRequestException("down");

        await Run();

        Assert.Single(_push.Sent);
        Assert.Equal(Fx.At(14, 5), _store.Poll.LastSampleAt);
    }

    [Fact]
    public async Task Hour_average_is_not_requested_when_there_is_no_new_sample()
    {
        _store.Poll = new PollState(Fx.At(14, 5), null);
        await Run();
        Assert.Equal(0, _feed.HourAverageCalls);
    }

    [Fact]
    public async Task A_fired_rule_stays_quiet_on_the_next_new_sample()
    {
        AddRule(1);
        await Run();

        _clock.Advance(TimeSpan.FromMinutes(5));
        _feed.Samples = Fx.Run(14, 5, 50m, 60m);
        await Run();

        Assert.Single(_push.Sent);
    }
}

public class HealthServiceTests
{
    private readonly InMemoryStore _store = new();
    private readonly FakeClock _clock = new(Fx.At(14, 30));

    private Task<HealthResponse> Get() => new HealthService(_store, _clock).GetAsync(default);

    [Fact]
    public async Task Fresh_sample_is_not_stale()
    {
        _store.Poll = new PollState(Fx.At(14, 10), Fx.At(14, 29));
        var health = await Get();
        Assert.False(health.Stale);
        Assert.Equal(Fx.At(14, 10), health.LastSampleAt);
        Assert.Equal(Fx.At(14, 29), health.LastPollAt);
    }

    [Fact]
    public async Task Exactly_20_minutes_is_not_stale_but_a_second_more_is()
    {
        _store.Poll = new PollState(Fx.At(14, 10), null);
        Assert.False((await Get()).Stale);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await Get()).Stale);
    }

    [Fact]
    public async Task No_sample_yet_is_stale()
        => Assert.True((await Get()).Stale);
}
