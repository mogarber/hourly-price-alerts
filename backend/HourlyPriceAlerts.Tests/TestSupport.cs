using System.Collections.Concurrent;
using System.Net;
using HourlyPriceAlerts.Core;
using Microsoft.Extensions.Logging;

namespace HourlyPriceAlerts.Tests;

public sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Thread-safe ordered log shared by the fakes, so tests can assert the order of calls.</summary>
public sealed class CallLog
{
    private readonly ConcurrentQueue<string> _entries = new();

    public void Add(string entry) => _entries.Enqueue(entry);

    public IReadOnlyList<string> Entries => _entries.ToArray();
}

public sealed class InMemoryStore(CallLog? log = null) : IAlertStore
{
    private readonly object _gate = new();
    private readonly CallLog _log = log ?? new CallLog();

    public Dictionary<string, DeviceRecord> Devices { get; } = new();
    public Dictionary<(string InstallId, string RuleId), RuleRecord> Rules { get; } = new();
    public PollState Poll { get; set; } = new(null, null);
    public HashSet<string> FailStateUpdateFor { get; } = new();

    public CallLog Log => _log;

    public Task<DeviceRecord?> GetDeviceAsync(string installId, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(Devices.GetValueOrDefault(installId));
        }
    }

    public Task UpsertDeviceAsync(DeviceRecord device, CancellationToken ct)
    {
        lock (_gate)
        {
            Devices[device.InstallId] = device;
        }

        return Task.CompletedTask;
    }

    public Task DeleteDeviceAsync(string installId, CancellationToken ct)
    {
        lock (_gate)
        {
            _log.Add($"DeleteDevice:{installId}");
            Devices.Remove(installId);
            foreach (var key in Rules.Keys.Where(k => k.InstallId == installId).ToList())
            {
                Rules.Remove(key);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RuleRecord>> GetRulesAsync(string installId, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<RuleRecord>>(
                Rules.Values.Where(r => r.InstallId == installId).OrderBy(r => r.Rule.Id).ToList());
        }
    }

    public Task<RuleRecord?> GetRuleAsync(string installId, string ruleId, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(Rules.GetValueOrDefault((installId, ruleId)));
        }
    }

    public Task UpsertRuleAsync(RuleRecord rule, CancellationToken ct)
    {
        lock (_gate)
        {
            Rules[(rule.InstallId, rule.Rule.Id)] = rule;
        }

        return Task.CompletedTask;
    }

    public Task DeleteRuleAsync(string installId, string ruleId, CancellationToken ct)
    {
        lock (_gate)
        {
            Rules.Remove((installId, ruleId));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RuleRecord>> GetEnabledRulesAsync(CancellationToken ct)
    {
        _log.Add("GetEnabledRules");
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<RuleRecord>>(Rules.Values.Where(r => r.Rule.Enabled).ToList());
        }
    }

    public Task<bool> UpdateRuleStateAsync(string installId, string ruleId, RuleState state, CancellationToken ct)
    {
        _log.Add($"UpdateRuleState:{ruleId}");
        lock (_gate)
        {
            if (FailStateUpdateFor.Contains(ruleId))
            {
                throw new InvalidOperationException("simulated storage failure");
            }

            if (!Rules.TryGetValue((installId, ruleId), out var existing))
            {
                return Task.FromResult(false);
            }

            Rules[(installId, ruleId)] = existing with { State = state };
            return Task.FromResult(true);
        }
    }

    public Task<PollState> GetPollStateAsync(CancellationToken ct)
    {
        _log.Add("GetPollState");
        return Task.FromResult(Poll);
    }

    public Task SetLastPollAtAsync(DateTimeOffset value, CancellationToken ct)
    {
        _log.Add("SetLastPollAt");
        Poll = Poll with { LastPollAt = value };
        return Task.CompletedTask;
    }

    public Task SetLastSampleAtAsync(DateTimeOffset value, CancellationToken ct)
    {
        _log.Add("SetLastSampleAt");
        Poll = Poll with { LastSampleAt = value };
        return Task.CompletedTask;
    }
}

public sealed class FakePushSender(CallLog? log = null) : IPushSender
{
    private readonly CallLog _log = log ?? new CallLog();

    public ConcurrentQueue<PushMessage> Sent { get; } = new();

    /// <summary>Rule ids whose push should throw.</summary>
    public HashSet<string> FailFor { get; } = new();

    /// <summary>What a push that doesn't throw returns. Only Sent records the message in <see cref="Sent"/>.</summary>
    public PushResult Result { get; set; } = PushResult.Sent;

    public Task<PushResult> SendAsync(PushMessage message, CancellationToken ct)
    {
        _log.Add($"Push:{message.RuleId ?? message.CollapseId}");
        if (message.RuleId is not null && FailFor.Contains(message.RuleId))
        {
            throw new InvalidOperationException("simulated push failure");
        }

        if (Result == PushResult.Sent)
        {
            Sent.Enqueue(message);
        }

        return Task.FromResult(Result);
    }
}

public sealed class FakeFeedClient(CallLog? log = null) : IFeedClient
{
    private readonly CallLog _log = log ?? new CallLog();

    public IReadOnlyList<Sample> Samples { get; set; } = [];
    public decimal? HourAverage { get; set; }
    public Exception? FeedError { get; set; }
    public Exception? HourAverageError { get; set; }
    public int HourAverageCalls { get; private set; }

    public Task<IReadOnlyList<Sample>> GetFiveMinuteFeedAsync(CancellationToken ct)
    {
        _log.Add("FetchFeed");
        return FeedError is null ? Task.FromResult(Samples) : Task.FromException<IReadOnlyList<Sample>>(FeedError);
    }

    public Task<decimal?> GetCurrentHourAverageAsync(CancellationToken ct)
    {
        HourAverageCalls++;
        _log.Add("FetchHourAverage");
        return HourAverageError is null ? Task.FromResult(HourAverage) : Task.FromException<decimal?>(HourAverageError);
    }
}

public sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    Dictionary<string, string> Headers,
    Version Version,
    HttpVersionPolicy VersionPolicy,
    string Body);

/// <summary>Records each request and answers with whatever the supplied function returns.</summary>
public sealed class FakeHandler(Func<RecordedRequest, int, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private int _count;

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    public FakeHandler(Func<RecordedRequest, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(",", header.Value);
            }
        }

        var recorded = new RecordedRequest(
            request.Method, request.RequestUri!, headers, request.Version, request.VersionPolicy, body);
        Requests.Enqueue(recorded);
        return await respond(recorded, Interlocked.Increment(ref _count));
    }
}

public static class Http
{
    public static HttpResponseMessage Status(HttpStatusCode code, string? body = null)
        => new(code) { Content = new StringContent(body ?? "") };

    public static HttpResponseMessage Reason(HttpStatusCode code, string reason)
        => Status(code, $"{{\"reason\":\"{reason}\"}}");
}

/// <summary>Captures log messages so tests can assert on warnings.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Enqueue((logLevel, formatter(state, exception)));
}

public static class Fx
{
    public const string Chicago = "America/Chicago";

    public static DateTimeOffset At(int hour, int minute, int second = 0)
        => new(2026, 11, 2, hour, minute, second, TimeSpan.Zero);

    public static TimeZoneInfo Zone(string id = Chicago) => TimeZoneInfo.FindSystemTimeZoneById(id);

    /// <summary>Samples every 5 minutes starting at the given UTC time on 2026-11-02.</summary>
    public static List<Sample> Run(int hour, int minute, params decimal[] prices)
    {
        var start = At(hour, minute);
        return prices.Select((p, i) => new Sample(start.AddMinutes(5 * i), p)).ToList();
    }

    public static Rule Rule(
        RuleKind kind = RuleKind.Above,
        RuleSignal signal = RuleSignal.FiveMinute,
        decimal threshold = 10.0m,
        Direction? direction = null,
        int? window = null,
        int cooldown = 60,
        QuietHours? quiet = null,
        bool enabled = true,
        string? id = null)
        => new()
        {
            Id = id ?? "11111111-1111-4111-8111-111111111111",
            Kind = kind,
            Signal = signal,
            ThresholdCents = threshold,
            Direction = direction,
            WindowMinutes = window,
            CooldownMinutes = cooldown,
            QuietHours = quiet,
            Enabled = enabled,
        };
}
