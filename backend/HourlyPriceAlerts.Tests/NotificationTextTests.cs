using HourlyPriceAlerts.Core;

namespace HourlyPriceAlerts.Tests;

public class NotificationTextTests
{
    private static readonly TimeZoneInfo Chicago = Fx.Zone();

    // 20:05Z on 2026-11-02 is 2:05 PM in Chicago (CST).
    private static readonly DateTimeOffset TwoOhFive = Fx.At(20, 5);

    [Fact]
    public void Above_hourly()
    {
        var rule = Fx.Rule(RuleKind.Above, RuleSignal.HourAverage, 10.0m);
        Assert.Equal(("Price above 10.0¢", "Hourly average is 12.4¢ as of 2:05 PM."),
            NotificationText.Threshold(rule, 12.4m, TwoOhFive, Chicago));
    }

    [Fact]
    public void Above_five_minute()
    {
        var rule = Fx.Rule(RuleKind.Above, RuleSignal.FiveMinute, 10.0m);
        Assert.Equal(("Price above 10.0¢", "5-minute price is 12.4¢ as of 2:05 PM."),
            NotificationText.Threshold(rule, 12.4m, TwoOhFive, Chicago));
    }

    [Fact]
    public void Below_hourly()
    {
        var rule = Fx.Rule(RuleKind.Below, RuleSignal.HourAverage, 2.0m);
        Assert.Equal(("Price below 2.0¢", "Hourly average is 1.5¢ as of 2:05 PM."),
            NotificationText.Threshold(rule, 1.5m, TwoOhFive, Chicago));
    }

    [Fact]
    public void Below_five_minute()
    {
        var rule = Fx.Rule(RuleKind.Below, RuleSignal.FiveMinute, 2.0m);
        Assert.Equal(("Price below 2.0¢", "5-minute price is 1.5¢ as of 2:05 PM."),
            NotificationText.Threshold(rule, 1.5m, TwoOhFive, Chicago));
    }

    [Fact]
    public void Move_up_hour()
    {
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.HourAverage, 3m, Direction.Either);
        Assert.Equal(("Price up 3.2¢", "Hourly average is 9.1¢, up 3.2¢ from last hour."),
            NotificationText.Move(rule, 9.1m, 3.2m, up: true));
    }

    [Fact]
    public void Move_down_hour()
    {
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.HourAverage, 3m, Direction.Either);
        Assert.Equal(("Price down 3.2¢", "Hourly average is 2.1¢, down 3.2¢ from last hour."),
            NotificationText.Move(rule, 2.1m, 3.2m, up: false));
    }

    [Fact]
    public void Move_up_five_minute()
    {
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 5m, Direction.Either, window: 30);
        Assert.Equal(("Price up 6.0¢", "5-minute price is 9.1¢, up 6.0¢ in the last 30 minutes."),
            NotificationText.Move(rule, 9.1m, 6.0m, up: true));
    }

    [Fact]
    public void Move_down_five_minute()
    {
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 5m, Direction.Either, window: 30);
        Assert.Equal(("Price down 6.0¢", "5-minute price is 2.1¢, down 6.0¢ in the last 30 minutes."),
            NotificationText.Move(rule, 2.1m, 6.0m, up: false));
    }

    [Fact]
    public void Move_text_uses_the_rules_window()
    {
        var rule = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 5m, Direction.Up, window: 5);
        Assert.EndsWith("in the last 5 minutes.", NotificationText.Move(rule, 9.1m, 6.0m, true).Body);
    }

    [Fact]
    public void Negative_values_use_an_ascii_minus()
    {
        var above = Fx.Rule(RuleKind.Above, RuleSignal.FiveMinute, -1.4m);
        var (title, body) = NotificationText.Threshold(above, -0.3m, TwoOhFive, Chicago);
        Assert.Equal("Price above -1.4¢", title);
        Assert.Equal("5-minute price is -0.3¢ as of 2:05 PM.", body);

        var below = Fx.Rule(RuleKind.Below, RuleSignal.HourAverage, -5m);
        (title, body) = NotificationText.Threshold(below, -5.6m, TwoOhFive, Chicago);
        Assert.Equal("Price below -5.0¢", title);
        Assert.Equal("Hourly average is -5.6¢ as of 2:05 PM.", body);

        var move = Fx.Rule(RuleKind.Move, RuleSignal.FiveMinute, 5m, Direction.Down, window: 30);
        (title, body) = NotificationText.Move(move, -2.1m, 6.0m, up: false);
        Assert.Equal("Price down 6.0¢", title);
        Assert.Equal("5-minute price is -2.1¢, down 6.0¢ in the last 30 minutes.", body);

        Assert.DoesNotContain('−', title + body);
    }

    [Fact]
    public void Values_are_rounded_to_one_decimal_with_no_thousands_separator()
    {
        Assert.Equal("0.1¢", NotificationText.Cents(0.05m));
        Assert.Equal("-0.1¢", NotificationText.Cents(-0.05m));
        Assert.Equal("0.0¢", NotificationText.Cents(-0.04m));
        Assert.Equal("1000.0¢", NotificationText.Cents(1000m));
        Assert.Equal("10.0¢", NotificationText.Cents(10m));
    }

    [Theory]
    [InlineData(6, 5, "12:05 AM")]     // 06:05Z is 00:05 CST
    [InlineData(12, 30, "6:30 AM")]
    [InlineData(18, 0, "12:00 PM")]
    [InlineData(20, 5, "2:05 PM")]
    [InlineData(5, 59, "11:59 PM")]    // on Nov 2, 05:59Z is 23:59 CST of Nov 1
    public void Times_are_h_mm_tt_in_the_device_time_zone(int hour, int minute, string expected)
    {
        var rule = Fx.Rule(RuleKind.Above, RuleSignal.FiveMinute, 1m);
        var body = NotificationText.Threshold(rule, 2m, Fx.At(hour, minute), Chicago).Body;
        Assert.Equal($"5-minute price is 2.0¢ as of {expected}.", body);
        Assert.DoesNotContain(' ', body);
    }

    [Fact]
    public void Time_follows_the_device_zone_and_dst()
    {
        var rule = Fx.Rule(RuleKind.Above, RuleSignal.FiveMinute, 1m);
        var summer = new DateTimeOffset(2026, 7, 1, 19, 5, 0, TimeSpan.Zero);    // 2:05 PM CDT
        Assert.EndsWith("as of 2:05 PM.", NotificationText.Threshold(rule, 2m, summer, Chicago).Body);
        Assert.EndsWith("as of 12:35 AM.", NotificationText.Threshold(rule, 2m, Fx.At(19, 5), Fx.Zone("Asia/Kolkata")).Body);
    }
}
