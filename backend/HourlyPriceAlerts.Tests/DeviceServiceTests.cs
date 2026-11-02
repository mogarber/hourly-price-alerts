using System.Text.Json;
using HourlyPriceAlerts.Core;

namespace HourlyPriceAlerts.Tests;

public class DeviceServiceTests
{
    private const string InstallId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private static readonly string Secret = new('1', 64);
    private static readonly string Bearer = "Bearer " + Secret;

    private readonly InMemoryStore _store = new();
    private readonly FakePushSender _push = new();
    private readonly FakeClock _clock = new(Fx.At(14, 0));
    private readonly DeviceService _service;

    public DeviceServiceTests() => _service = new DeviceService(_store, _push, _clock);

    private static DeviceRequest Body(string? token = null) => new()
    {
        ApnsToken = token ?? new string('a', 64),
        ApnsEnvironment = ApnsEnvironment.Sandbox,
        TimeZone = Fx.Chicago,
    };

    private static string RuleId(int n) => $"{n:x8}-0000-4000-8000-000000000000";

    private Task<ServiceResult> PutDevice(string? auth = null, DeviceRequest? body = null, string? id = null)
        => _service.PutDeviceAsync(id ?? InstallId, auth ?? Bearer, body ?? Body(), default);

    private Task<ServiceResult> PutRule(int n, string? auth = null, Rule? rule = null)
        => _service.PutRuleAsync(InstallId, RuleId(n), auth ?? Bearer, rule ?? Fx.Rule(id: RuleId(n)), default);

    private async Task RegisterAsync() => Assert.Equal(ResultStatus.Ok, (await PutDevice()).Status);

    // ---- Authentication ----

    [Fact]
    public async Task First_put_stores_the_sha256_hash_of_the_secret_and_never_the_secret()
    {
        var result = await PutDevice();

        Assert.Equal(ResultStatus.Ok, result.Status);
        var device = _store.Devices[InstallId];
        Assert.Equal(DeviceService.HashSecret(Secret), device.SecretHash);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Secret))), device.SecretHash);
        Assert.NotEqual(Secret, device.SecretHash);

        // Nothing stored or returned contains the secret itself.
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(device, ApiJson.Options));
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result.Body, ApiJson.Options));
    }

    [Fact]
    public async Task The_response_does_not_expose_the_secret_hash()
    {
        var json = JsonSerializer.Serialize((await PutDevice()).Body, ApiJson.Options);
        Assert.DoesNotContain(DeviceService.HashSecret(Secret), json);
        Assert.Contains("\"installId\":\"" + InstallId + "\"", json);
        Assert.Contains("\"apnsEnvironment\":\"sandbox\"", json);
    }

    [Fact]
    public async Task Later_puts_with_the_same_secret_update_the_row_and_keep_hash_and_last_test()
    {
        await RegisterAsync();
        _store.Devices[InstallId] = _store.Devices[InstallId] with { LastTestAt = Fx.At(13, 0) };

        var result = await PutDevice(body: Body(new string('c', 70)));

        Assert.Equal(ResultStatus.Ok, result.Status);
        var device = _store.Devices[InstallId];
        Assert.Equal(new string('c', 70), device.ApnsToken);
        Assert.Equal(DeviceService.HashSecret(Secret), device.SecretHash);
        Assert.Equal(Fx.At(13, 0), device.LastTestAt);
    }

    [Fact]
    public async Task Wrong_secret_is_401_and_changes_nothing()
    {
        await RegisterAsync();
        var other = "Bearer " + new string('2', 64);

        Assert.Equal(ResultStatus.Unauthorized, (await PutDevice(other, Body(new string('d', 64)))).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await _service.GetRulesAsync(InstallId, other, default)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await PutRule(1, other)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await _service.DeleteRuleAsync(InstallId, RuleId(1), other, default)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await _service.SendTestAsync(InstallId, other, default)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await _service.DeleteDeviceAsync(InstallId, other, default)).Status);

        Assert.Equal(new string('a', 64), _store.Devices[InstallId].ApnsToken);
        Assert.Empty(_push.Sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic abc")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Bearer short")]
    [InlineData("Bearer 1111111111111111111111111111111111111111111111111111111111111111aa")]
    [InlineData("Bearer AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Missing_or_malformed_authorization_is_401_on_every_route(string? header)
    {
        await RegisterAsync();

        Assert.Equal(ResultStatus.Unauthorized, (await _service.PutDeviceAsync(InstallId, header, Body(), default)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await _service.GetRulesAsync(InstallId, header, default)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await _service.PutRuleAsync(InstallId, RuleId(1), header, Fx.Rule(id: RuleId(1)), default)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await _service.DeleteRuleAsync(InstallId, RuleId(1), header, default)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await _service.SendTestAsync(InstallId, header, default)).Status);
        Assert.Equal(ResultStatus.Unauthorized, (await _service.DeleteDeviceAsync(InstallId, header, default)).Status);
    }

    [Fact]
    public async Task The_bearer_scheme_is_case_insensitive()
        => Assert.Equal(ResultStatus.Ok, (await PutDevice("bearer " + Secret)).Status);

    [Fact]
    public async Task First_put_with_an_invalid_body_does_not_claim_the_install_id()
    {
        var result = await PutDevice(body: Body("nothex"));

        Assert.Equal(ResultStatus.BadRequest, result.Status);
        Assert.Empty(_store.Devices);

        // A different secret can still register this install id.
        Assert.Equal(ResultStatus.Ok, (await PutDevice("Bearer " + new string('2', 64))).Status);
    }

    [Fact]
    public async Task Wrong_secret_wins_over_a_bad_body()
    {
        await RegisterAsync();
        Assert.Equal(ResultStatus.Unauthorized, (await PutDevice("Bearer " + new string('2', 64), Body("bad"))).Status);
    }

    [Theory]
    [InlineData("AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA")]
    [InlineData("not-a-uuid")]
    public async Task Install_id_must_be_a_lowercase_uuid(string id)
    {
        Assert.Equal(ResultStatus.BadRequest, (await PutDevice(id: id)).Status);
        Assert.Equal(ResultStatus.BadRequest, (await _service.GetRulesAsync(id, Bearer, default)).Status);
    }

    [Fact]
    public async Task Unknown_device_is_404_on_rule_and_test_routes()
    {
        Assert.Equal(ResultStatus.NotFound, (await _service.GetRulesAsync(InstallId, Bearer, default)).Status);
        Assert.Equal(ResultStatus.NotFound, (await PutRule(1)).Status);
        Assert.Equal(ResultStatus.NotFound, (await _service.DeleteRuleAsync(InstallId, RuleId(1), Bearer, default)).Status);
        Assert.Equal(ResultStatus.NotFound, (await _service.SendTestAsync(InstallId, Bearer, default)).Status);
    }

    // ---- Device validation ----

    [Fact]
    public async Task Bad_device_fields_are_400_with_a_message()
    {
        var shortToken = await PutDevice(body: Body("abcd"));
        var badZone = await PutDevice(body: Body() with { TimeZone = "Nowhere/Land" });

        Assert.Equal(ResultStatus.BadRequest, shortToken.Status);
        Assert.False(string.IsNullOrWhiteSpace(shortToken.Error));
        Assert.Equal(ResultStatus.BadRequest, badZone.Status);
    }

    // ---- Delete device ----

    [Fact]
    public async Task Delete_device_removes_the_device_and_all_its_rules()
    {
        await RegisterAsync();
        await PutRule(1);
        await PutRule(2);

        var result = await _service.DeleteDeviceAsync(InstallId, Bearer, default);

        Assert.Equal(ResultStatus.NoContent, result.Status);
        Assert.Empty(_store.Devices);
        Assert.Empty(_store.Rules);
    }

    [Fact]
    public async Task Delete_device_is_idempotent_for_an_unknown_install()
        => Assert.Equal(ResultStatus.NoContent, (await _service.DeleteDeviceAsync(InstallId, Bearer, default)).Status);

    // ---- Rules ----

    [Fact]
    public async Task Put_rule_stores_a_new_rule_armed_and_returns_it()
    {
        await RegisterAsync();
        var rule = Fx.Rule(RuleKind.Below, RuleSignal.HourAverage, 2m, id: RuleId(1));

        var result = await PutRule(1, rule: rule);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(rule, result.Body);
        Assert.Equal(new RuleRecord(InstallId, rule, RuleState.New), _store.Rules[(InstallId, RuleId(1))]);
    }

    [Fact]
    public async Task Put_rule_rearms_but_keeps_last_fired_at()
    {
        await RegisterAsync();
        await PutRule(1);
        _store.Rules[(InstallId, RuleId(1))] = _store.Rules[(InstallId, RuleId(1))] with { State = new RuleState(false, Fx.At(9, 0)) };

        await PutRule(1, rule: Fx.Rule(threshold: 20m, id: RuleId(1)));

        var stored = _store.Rules[(InstallId, RuleId(1))];
        Assert.Equal(new RuleState(true, Fx.At(9, 0)), stored.State);
        Assert.Equal(20m, stored.Rule.ThresholdCents);
    }

    [Fact]
    public async Task Put_rule_validates_the_rule_and_id()
    {
        await RegisterAsync();

        var invalid = await PutRule(1, rule: Fx.Rule(threshold: 5000m, id: RuleId(1)));
        var mismatch = await PutRule(1, rule: Fx.Rule(id: RuleId(2)));

        Assert.Equal(ResultStatus.BadRequest, invalid.Status);
        Assert.Equal(ResultStatus.BadRequest, mismatch.Status);
        Assert.Empty(_store.Rules);
    }

    [Fact]
    public async Task A_device_can_have_ten_rules_but_not_eleven()
    {
        await RegisterAsync();
        for (var i = 1; i <= 10; i++)
        {
            Assert.Equal(ResultStatus.Ok, (await PutRule(i)).Status);
        }

        var eleventh = await PutRule(11);

        Assert.Equal(ResultStatus.BadRequest, eleventh.Status);
        Assert.Contains("10", eleventh.Error);
        Assert.Equal(10, _store.Rules.Count);
    }

    [Fact]
    public async Task At_the_limit_an_existing_rule_can_still_be_updated()
    {
        await RegisterAsync();
        for (var i = 1; i <= 10; i++)
        {
            await PutRule(i);
        }

        var update = await PutRule(5, rule: Fx.Rule(threshold: 3m, id: RuleId(5)));

        Assert.Equal(ResultStatus.Ok, update.Status);
        Assert.Equal(10, _store.Rules.Count);
    }

    [Fact]
    public async Task The_limit_is_per_device()
    {
        await RegisterAsync();
        for (var i = 1; i <= 10; i++)
        {
            await PutRule(i);
        }

        var other = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
        await _service.PutDeviceAsync(other, Bearer, Body(), default);
        var result = await _service.PutRuleAsync(other, RuleId(1), Bearer, Fx.Rule(id: RuleId(1)), default);

        Assert.Equal(ResultStatus.Ok, result.Status);
    }

    [Fact]
    public async Task Get_rules_returns_only_this_devices_rules_without_state()
    {
        await RegisterAsync();
        await PutRule(1);
        await PutRule(2);
        await _service.PutDeviceAsync("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", Bearer, Body(), default);
        await _service.PutRuleAsync("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", RuleId(3), Bearer, Fx.Rule(id: RuleId(3)), default);

        var result = await _service.GetRulesAsync(InstallId, Bearer, default);

        var rules = Assert.IsType<RulesResponse>(result.Body).Rules;
        Assert.Equal([RuleId(1), RuleId(2)], rules.Select(r => r.Id));
        var json = JsonSerializer.Serialize(result.Body, ApiJson.Options);
        Assert.StartsWith("{\"rules\":[", json);
        Assert.DoesNotContain("armed", json);
        Assert.DoesNotContain("lastFiredAt", json);
    }

    [Fact]
    public async Task Delete_rule_removes_it_and_is_idempotent()
    {
        await RegisterAsync();
        await PutRule(1);

        Assert.Equal(ResultStatus.NoContent, (await _service.DeleteRuleAsync(InstallId, RuleId(1), Bearer, default)).Status);
        Assert.Empty(_store.Rules);
        Assert.Equal(ResultStatus.NoContent, (await _service.DeleteRuleAsync(InstallId, RuleId(1), Bearer, default)).Status);
    }

    // ---- Test push ----

    [Fact]
    public async Task Test_endpoint_sends_a_push_and_returns_202()
    {
        await RegisterAsync();

        var result = await _service.SendTestAsync(InstallId, Bearer, default);

        Assert.Equal(ResultStatus.Accepted, result.Status);
        var sent = Assert.Single(_push.Sent);
        Assert.Equal(new string('a', 64), sent.ApnsToken);
        Assert.Equal(ApnsEnvironment.Sandbox, sent.Environment);
        Assert.Null(sent.RuleId);
        Assert.Equal(_clock.UtcNow, _store.Devices[InstallId].LastTestAt);
    }

    [Fact]
    public async Task Test_endpoint_is_502_when_apple_does_not_accept_the_push()
    {
        await RegisterAsync();
        _push.Result = PushResult.Failed;

        var result = await _service.SendTestAsync(InstallId, Bearer, default);

        Assert.Equal(ResultStatus.BadGateway, result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Test_endpoint_is_404_when_the_device_token_is_dead()
    {
        await RegisterAsync();
        _push.Result = PushResult.DeviceRemoved;

        var result = await _service.SendTestAsync(InstallId, Bearer, default);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Test_endpoint_is_429_when_under_60_seconds_since_the_last_test()
    {
        await RegisterAsync();
        await _service.SendTestAsync(InstallId, Bearer, default);

        _clock.Advance(TimeSpan.FromSeconds(59));
        var tooSoon = await _service.SendTestAsync(InstallId, Bearer, default);

        Assert.Equal(ResultStatus.TooManyRequests, tooSoon.Status);
        Assert.Single(_push.Sent);

        _clock.Advance(TimeSpan.FromSeconds(1));
        var allowed = await _service.SendTestAsync(InstallId, Bearer, default);

        Assert.Equal(ResultStatus.Accepted, allowed.Status);
        Assert.Equal(2, _push.Sent.Count);
    }

    [Fact]
    public async Task A_device_put_does_not_reset_the_test_throttle()
    {
        await RegisterAsync();
        await _service.SendTestAsync(InstallId, Bearer, default);
        await PutDevice();

        Assert.Equal(ResultStatus.TooManyRequests, (await _service.SendTestAsync(InstallId, Bearer, default)).Status);
    }
}
