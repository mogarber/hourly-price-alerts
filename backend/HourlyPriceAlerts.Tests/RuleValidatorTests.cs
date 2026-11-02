using System.Text.Json;
using HourlyPriceAlerts.Core;

namespace HourlyPriceAlerts.Tests;

public class RuleValidatorTests
{
    private const string Id = "11111111-1111-4111-8111-111111111111";

    private static string? Validate(Rule rule) => RuleValidator.Validate(rule, Id);

    private static Rule Above(decimal t = 10m) => Fx.Rule(RuleKind.Above, RuleSignal.HourAverage, t);

    private static Rule FiveMinuteMove(int? window = 30, decimal t = 5m, Direction? direction = Direction.Either)
        => Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, t, direction, window);

    [Fact]
    public void A_valid_rule_of_each_shape_passes()
    {
        Assert.Null(Validate(Above()));
        Assert.Null(Validate(Fx.Rule(RuleKind.Below, RuleSignal.FiveMinute, 2m)));
        Assert.Null(Validate(FiveMinuteMove()));
        Assert.Null(Validate(Fx.Rule(RuleKind.Move, RuleSignal.HourAverage, 3m, Direction.Up)));
    }

    [Theory]
    [InlineData("-100", true)]
    [InlineData("-100.1", false)]
    [InlineData("1000", true)]
    [InlineData("1000.1", false)]
    [InlineData("0", true)]
    [InlineData("-0.1", true)]
    public void Above_and_below_threshold_range(string t, bool ok)
    {
        var value = decimal.Parse(t);
        Assert.Equal(ok, Validate(Fx.Rule(RuleKind.Above, RuleSignal.HourAverage, value)) is null);
        Assert.Equal(ok, Validate(Fx.Rule(RuleKind.Below, RuleSignal.FiveMinute, value)) is null);
    }

    [Theory]
    [InlineData("0.1", true)]
    [InlineData("0.0", false)]
    [InlineData("200", true)]
    [InlineData("200.1", false)]
    [InlineData("-1", false)]
    public void Move_threshold_range(string t, bool ok)
        => Assert.Equal(ok, Validate(FiveMinuteMove(t: decimal.Parse(t))) is null);

    [Theory]
    [InlineData("10.0", true)]
    [InlineData("10.5", true)]
    [InlineData("10.50", true)]
    [InlineData("10.55", false)]
    [InlineData("0.15", false)]
    public void Threshold_has_at_most_one_decimal(string t, bool ok)
        => Assert.Equal(ok, Validate(Above(decimal.Parse(t))) is null);

    [Theory]
    [InlineData(5, true)]
    [InlineData(180, true)]
    [InlineData(30, true)]
    [InlineData(0, false)]
    [InlineData(185, false)]
    [InlineData(7, false)]
    [InlineData(-5, false)]
    public void Five_minute_move_window(int window, bool ok)
        => Assert.Equal(ok, Validate(FiveMinuteMove(window)) is null);

    [Fact]
    public void Five_minute_move_requires_a_window()
        => Assert.NotNull(Validate(FiveMinuteMove(window: null)));

    [Fact]
    public void Window_must_be_null_unless_it_is_a_five_minute_move()
    {
        Assert.NotNull(Validate(Above() with { WindowMinutes = 30 }));
        Assert.NotNull(Validate(Fx.Rule(RuleKind.Move, RuleSignal.HourAverage, 3m, Direction.Up, window: 30)));
    }

    [Fact]
    public void Direction_is_required_for_move_and_null_otherwise()
    {
        Assert.NotNull(Validate(FiveMinuteMove(direction: null)));
        Assert.NotNull(Validate(Above() with { Direction = Direction.Up }));
        Assert.Null(Validate(Above() with { Direction = null }));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1440, true)]
    [InlineData(-1, false)]
    [InlineData(1441, false)]
    public void Cooldown_range(int minutes, bool ok)
        => Assert.Equal(ok, Validate(Above() with { CooldownMinutes = minutes }) is null);

    [Theory]
    [InlineData(0, 1439, true)]
    [InlineData(1439, 0, true)]
    [InlineData(600, 600, true)]
    [InlineData(-1, 10, false)]
    [InlineData(10, -1, false)]
    [InlineData(1440, 10, false)]
    [InlineData(10, 1440, false)]
    public void Quiet_hours_range(int start, int end, bool ok)
        => Assert.Equal(ok, Validate(Above() with { QuietHours = new QuietHours { StartMinute = start, EndMinute = end } }) is null);

    [Fact]
    public void Quiet_hours_may_be_null()
        => Assert.Null(Validate(Above() with { QuietHours = null }));

    [Theory]
    [InlineData("11111111-1111-4111-8111-111111111111", true)]
    [InlineData("11111111-1111-4111-8111-11111111111A", false)]   // uppercase
    [InlineData("11111111111141118111111111111111", false)]       // no dashes
    [InlineData("{11111111-1111-4111-8111-111111111111}", false)]
    [InlineData("not-a-uuid", false)]
    [InlineData("", false)]
    public void Rule_id_must_be_a_lowercase_uuid(string id, bool ok)
        => Assert.Equal(ok, RuleValidator.Validate(Above() with { Id = id }, id) is null);

    [Fact]
    public void Rule_id_must_match_the_route()
        => Assert.NotNull(RuleValidator.Validate(Above(), "22222222-2222-4222-8222-222222222222"));

    [Fact]
    public void Undefined_enum_values_are_rejected()
    {
        Assert.NotNull(Validate(Above() with { Kind = (RuleKind)9 }));
        Assert.NotNull(Validate(Above() with { Signal = (RuleSignal)9 }));
        Assert.NotNull(Validate(FiveMinuteMove(direction: (Direction)9)));
    }

    // ---- Device request ----

    private static DeviceRequest Device(string? token = null, ApnsEnvironment env = ApnsEnvironment.Sandbox, string zone = Fx.Chicago)
        => new() { ApnsToken = token ?? new string('a', 64), ApnsEnvironment = env, TimeZone = zone };

    [Theory]
    [InlineData(63, false)]
    [InlineData(64, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void Apns_token_length(int length, bool ok)
        => Assert.Equal(ok, RuleValidator.Validate(Device(new string('b', length))) is null);

    [Theory]
    [InlineData("zz")]
    [InlineData("")]
    public void Apns_token_must_be_hex(string tail)
        => Assert.NotNull(RuleValidator.Validate(Device(new string('a', 62) + tail)));

    [Fact]
    public void Apns_token_accepts_upper_and_lower_case_hex_but_not_trailing_newline()
    {
        Assert.Null(RuleValidator.Validate(Device(new string('A', 32) + new string('f', 32))));
        Assert.NotNull(RuleValidator.Validate(Device(new string('a', 64) + "\n")));
    }

    [Theory]
    [InlineData("America/Chicago", true)]
    [InlineData("Europe/London", true)]
    [InlineData("UTC", true)]
    [InlineData("Not/AZone", false)]
    [InlineData("", false)]
    [InlineData("../etc/passwd", false)]
    [InlineData("America/Chicago\n", false)]
    public void Time_zone_must_be_a_valid_iana_id(string zone, bool ok)
        => Assert.Equal(ok, RuleValidator.Validate(Device(zone: zone)) is null);

    [Fact]
    public void Undefined_environment_is_rejected()
        => Assert.NotNull(RuleValidator.Validate(Device(env: (ApnsEnvironment)7)));

    // ---- JSON shape ----

    [Fact]
    public void Rule_json_from_the_spec_deserializes()
    {
        const string json = """
            {"id":"11111111-1111-4111-8111-111111111111","kind":"above","signal":"hourAverage","thresholdCents":10.0,
             "direction":null,"windowMinutes":null,"cooldownMinutes":60,
             "quietHours":{"startMinute":1320,"endMinute":420},"enabled":true}
            """;

        var rule = JsonSerializer.Deserialize<Rule>(json, ApiJson.Options)!;

        Assert.Equal(RuleKind.Above, rule.Kind);
        Assert.Equal(RuleSignal.HourAverage, rule.Signal);
        Assert.Equal(10.0m, rule.ThresholdCents);
        Assert.Equal(1320, rule.QuietHours!.StartMinute);
        Assert.Null(Validate(rule));
    }

    [Fact]
    public void Rule_serializes_with_camel_case_names_and_nulls()
    {
        var json = JsonSerializer.Serialize(Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 6m, Direction.Either, window: 30),
            ApiJson.Options);

        Assert.Contains("\"kind\":\"move\"", json);
        Assert.Contains("\"signal\":\"fiveMinute\"", json);
        Assert.Contains("\"direction\":\"either\"", json);
        Assert.Contains("\"quietHours\":null", json);
        Assert.Contains("\"thresholdCents\":6", json);
    }

    [Theory]
    [InlineData("""{"id":"x","signal":"hourAverage","thresholdCents":1,"cooldownMinutes":1,"enabled":true}""")]      // kind missing
    [InlineData("""{"id":"x","kind":"sideways","signal":"hourAverage","thresholdCents":1,"cooldownMinutes":1,"enabled":true}""")]
    [InlineData("""{"id":"x","kind":0,"signal":"hourAverage","thresholdCents":1,"cooldownMinutes":1,"enabled":true}""")]  // numeric enum
    [InlineData("""{"id":"x","kind":"above","signal":"hourAverage","thresholdCents":"1","cooldownMinutes":1,"enabled":true}""")] // string for number
    [InlineData("""{"id":"x","kind":"above","signal":"hourAverage","thresholdCents":1,"cooldownMinutes":1}""")]       // enabled missing
    public void Malformed_rule_json_is_rejected(string json)
        => Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<Rule>(json, ApiJson.Options));
}
