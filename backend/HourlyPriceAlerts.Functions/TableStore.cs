using System.Globalization;
using Azure;
using Azure.Data.Tables;
using HourlyPriceAlerts.Core;

namespace HourlyPriceAlerts.Functions;

/// <summary>Azure Table Storage implementation of the store. Tables: Devices, Rules, PollState.</summary>
public sealed class TableStore : IAlertStore
{
    private const string DeviceRowKey = "device";
    private const string PollPartitionKey = "poll";
    private const string PollRowKey = "state";

    private readonly TableClient _devices;
    private readonly TableClient _rules;
    private readonly TableClient _poll;
    private volatile bool _tablesCreated;

    public TableStore(TableServiceClient service)
    {
        _devices = service.GetTableClient("Devices");
        _rules = service.GetTableClient("Rules");
        _poll = service.GetTableClient("PollState");
    }

    private async Task EnsureTablesAsync(CancellationToken ct)
    {
        if (_tablesCreated)
        {
            return;
        }

        await _devices.CreateIfNotExistsAsync(ct);
        await _rules.CreateIfNotExistsAsync(ct);
        await _poll.CreateIfNotExistsAsync(ct);
        _tablesCreated = true;
    }

    // Devices

    public async Task<DeviceRecord?> GetDeviceAsync(string installId, CancellationToken ct)
    {
        await EnsureTablesAsync(ct);
        var result = await _devices.GetEntityIfExistsAsync<TableEntity>(installId, DeviceRowKey, cancellationToken: ct);
        return result.HasValue ? ToDevice(result.Value!) : null;
    }

    public async Task UpsertDeviceAsync(DeviceRecord device, CancellationToken ct)
    {
        await EnsureTablesAsync(ct);
        var entity = new TableEntity(device.InstallId, DeviceRowKey)
        {
            ["SecretHash"] = device.SecretHash,
            ["ApnsToken"] = device.ApnsToken,
            ["ApnsEnvironment"] = device.ApnsEnvironment.ToString(),
            ["TimeZone"] = device.TimeZone,
        };
        if (device.LastTestAt is { } lastTest)
        {
            entity["LastTestAt"] = lastTest;
        }

        await _devices.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    public async Task DeleteDeviceAsync(string installId, CancellationToken ct)
    {
        await EnsureTablesAsync(ct);
        foreach (var rule in await GetRulesAsync(installId, ct))
        {
            await DeleteIgnoringMissingAsync(_rules, installId, rule.Rule.Id, ct);
        }

        await DeleteIgnoringMissingAsync(_devices, installId, DeviceRowKey, ct);
    }

    // Rules

    public async Task<IReadOnlyList<RuleRecord>> GetRulesAsync(string installId, CancellationToken ct)
    {
        await EnsureTablesAsync(ct);
        var rules = new List<RuleRecord>();
        await foreach (var entity in _rules.QueryAsync<TableEntity>(
            TableClient.CreateQueryFilter($"PartitionKey eq {installId}"), cancellationToken: ct))
        {
            rules.Add(ToRule(entity));
        }

        return rules;
    }

    public async Task<RuleRecord?> GetRuleAsync(string installId, string ruleId, CancellationToken ct)
    {
        await EnsureTablesAsync(ct);
        var result = await _rules.GetEntityIfExistsAsync<TableEntity>(installId, ruleId, cancellationToken: ct);
        return result.HasValue ? ToRule(result.Value!) : null;
    }

    public async Task UpsertRuleAsync(RuleRecord record, CancellationToken ct)
    {
        await EnsureTablesAsync(ct);
        var rule = record.Rule;
        var entity = new TableEntity(record.InstallId, rule.Id)
        {
            ["Kind"] = rule.Kind.ToString(),
            ["Signal"] = rule.Signal.ToString(),
            ["ThresholdCents"] = rule.ThresholdCents.ToString("0.0", CultureInfo.InvariantCulture),
            ["CooldownMinutes"] = rule.CooldownMinutes,
            ["Enabled"] = rule.Enabled,
            ["Armed"] = record.State.Armed,
        };
        SetIfNotNull(entity, "Direction", rule.Direction?.ToString());
        SetIfNotNull(entity, "WindowMinutes", rule.WindowMinutes);
        SetIfNotNull(entity, "QuietStartMinute", rule.QuietHours?.StartMinute);
        SetIfNotNull(entity, "QuietEndMinute", rule.QuietHours?.EndMinute);
        SetIfNotNull(entity, "LastFiredAt", record.State.LastFiredAt);

        await _rules.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    public async Task DeleteRuleAsync(string installId, string ruleId, CancellationToken ct)
    {
        await EnsureTablesAsync(ct);
        await DeleteIgnoringMissingAsync(_rules, installId, ruleId, ct);
    }

    public async Task<IReadOnlyList<RuleRecord>> GetEnabledRulesAsync(CancellationToken ct)
    {
        await EnsureTablesAsync(ct);
        var rules = new List<RuleRecord>();
        await foreach (var entity in _rules.QueryAsync<TableEntity>("Enabled eq true", cancellationToken: ct))
        {
            rules.Add(ToRule(entity));
        }

        return rules;
    }

    public async Task<bool> UpdateRuleStateAsync(string installId, string ruleId, RuleState state, CancellationToken ct)
    {
        await EnsureTablesAsync(ct);
        var entity = new TableEntity(installId, ruleId) { ["Armed"] = state.Armed };
        SetIfNotNull(entity, "LastFiredAt", state.LastFiredAt);

        try
        {
            // Merge touches only the state columns and fails with 404 if the rule was deleted in the meantime.
            await _rules.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Merge, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    // Poll state

    public async Task<PollState> GetPollStateAsync(CancellationToken ct)
    {
        await EnsureTablesAsync(ct);
        var result = await _poll.GetEntityIfExistsAsync<TableEntity>(PollPartitionKey, PollRowKey, cancellationToken: ct);
        return result.HasValue
            ? new PollState(result.Value!.GetDateTimeOffset("LastSampleAt"), result.Value.GetDateTimeOffset("LastPollAt"))
            : new PollState(null, null);
    }

    public Task SetLastPollAtAsync(DateTimeOffset value, CancellationToken ct) => MergePollAsync("LastPollAt", value, ct);

    public Task SetLastSampleAtAsync(DateTimeOffset value, CancellationToken ct) => MergePollAsync("LastSampleAt", value, ct);

    private async Task MergePollAsync(string column, DateTimeOffset value, CancellationToken ct)
    {
        await EnsureTablesAsync(ct);
        var entity = new TableEntity(PollPartitionKey, PollRowKey) { [column] = value };
        await _poll.UpsertEntityAsync(entity, TableUpdateMode.Merge, ct);
    }

    // Mapping

    private static async Task DeleteIgnoringMissingAsync(TableClient table, string partitionKey, string rowKey, CancellationToken ct)
    {
        try
        {
            await table.DeleteEntityAsync(partitionKey, rowKey, cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
    }

    private static void SetIfNotNull(TableEntity entity, string column, object? value)
    {
        if (value is not null)
        {
            entity[column] = value;
        }
    }

    private static DeviceRecord ToDevice(TableEntity e) => new(
        e.PartitionKey,
        e.GetString("SecretHash"),
        e.GetString("ApnsToken"),
        Enum.Parse<ApnsEnvironment>(e.GetString("ApnsEnvironment")),
        e.GetString("TimeZone"),
        e.GetDateTimeOffset("LastTestAt"));

    private static RuleRecord ToRule(TableEntity e)
    {
        var quiet = e.GetInt32("QuietStartMinute") is { } start && e.GetInt32("QuietEndMinute") is { } end
            ? new QuietHours { StartMinute = start, EndMinute = end }
            : null;

        var rule = new Rule
        {
            Id = e.RowKey,
            Kind = Enum.Parse<RuleKind>(e.GetString("Kind")),
            Signal = Enum.Parse<RuleSignal>(e.GetString("Signal")),
            ThresholdCents = PriceMath.Round(decimal.Parse(e.GetString("ThresholdCents"), CultureInfo.InvariantCulture)),
            Direction = e.GetString("Direction") is { } direction ? Enum.Parse<Direction>(direction) : null,
            WindowMinutes = e.GetInt32("WindowMinutes"),
            CooldownMinutes = e.GetInt32("CooldownMinutes") ?? 0,
            QuietHours = quiet,
            Enabled = e.GetBoolean("Enabled") ?? false,
        };
        var state = new RuleState(e.GetBoolean("Armed") ?? true, e.GetDateTimeOffset("LastFiredAt"));
        return new RuleRecord(e.PartitionKey, rule, state);
    }
}
