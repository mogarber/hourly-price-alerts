using HourlyPriceAlerts.Core;

namespace HourlyPriceAlerts.Tests;

public class PriceMathTests
{
    [Theory]
    [InlineData(18, 0, 18, 0)]    // on the hour: the hour it closes
    [InlineData(18, 5, 19, 0)]
    [InlineData(18, 10, 19, 0)]
    [InlineData(18, 55, 19, 0)]
    [InlineData(17, 5, 18, 0)]
    [InlineData(0, 0, 0, 0)]
    public void HourEnd_rounds_up_to_the_next_whole_utc_hour(int h, int m, int endH, int endM)
        => Assert.Equal(Fx.At(endH, endM), PriceMath.HourEnd(Fx.At(h, m)));

    [Fact]
    public void HourEnd_rounds_up_sub_minute_stamps()
        => Assert.Equal(Fx.At(18, 0), PriceMath.HourEnd(Fx.At(17, 59, 1)));

    [Fact]
    public void HourEnd_crosses_midnight()
        => Assert.Equal(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero),
            PriceMath.HourEnd(new DateTimeOffset(2026, 11, 2, 23, 5, 0, TimeSpan.Zero)));

    [Fact]
    public void Stamp_on_the_hour_closes_the_previous_hour_spec_example()
    {
        // Observed on the live feed: stamp 18:00 returned 0.1, the mean of the 12 samples 17:05 to 18:00 (0.14).
        var prices = new[] { 0.1m, 0.2m, 0.1m, 0.2m, 0.1m, 0.2m, 0.1m, 0.2m, 0.1m, 0.2m, 0.1m, 0.08m };
        Assert.Equal(1.68m, prices.Sum());
        var samples = Fx.Run(17, 5, prices);
        samples.Add(new Sample(Fx.At(17, 0), 50m));   // belongs to the hour ending 17:00
        samples.Add(new Sample(Fx.At(18, 5), 50m));   // belongs to the hour ending 19:00

        var stats = PriceMath.HourMean(samples, Fx.At(18, 0));

        Assert.Equal(12, stats.Count);
        Assert.Equal(0.1m, stats.Mean);
    }

    [Fact]
    public void Stamp_after_the_hour_belongs_to_the_next_bucket_spec_example()
    {
        // Observed: stamp 18:10 returned 1.4, the mean of 18:05 and 18:10.
        var samples = Fx.Run(17, 5, 0.5m, 0.5m, 0.5m, 0.5m, 0.5m, 0.5m, 0.5m, 0.5m, 0.5m, 0.5m, 0.5m, 0.5m); // 17:05..18:00
        samples.Add(new Sample(Fx.At(18, 5), 1.3m));
        samples.Add(new Sample(Fx.At(18, 10), 1.5m));
        var latest = samples[^1];

        var stats = PriceMath.HourMean(samples, PriceMath.HourEnd(latest.Stamp));

        Assert.Equal(2, stats.Count);
        Assert.Equal(1.4m, stats.Mean);
    }

    [Fact]
    public void HourMean_of_an_empty_bucket_has_count_zero()
        => Assert.Equal(new HourStats(0m, 0), PriceMath.HourMean([], Fx.At(18, 0)));

    [Theory]
    [InlineData("0.05", "0.1")]
    [InlineData("-0.05", "-0.1")]    // midpoint rounds away from zero
    [InlineData("0.04", "0.0")]
    [InlineData("0.14", "0.1")]
    [InlineData("2.25", "2.3")]
    [InlineData("-2.25", "-2.3")]
    [InlineData("1.45", "1.5")]
    [InlineData("9.95", "10.0")]
    [InlineData("-1.44", "-1.4")]
    [InlineData("12.4", "12.4")]
    public void Round_is_one_decimal_midpoint_away_from_zero(string input, string expected)
        => Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            PriceMath.Round(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Round_never_produces_negative_zero()
        => Assert.Equal("0.0", PriceMath.Round(-0.04m).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void HourMean_rounds_the_mean_not_the_samples()
    {
        // 0.14 and 0.15 average to 0.145, which rounds to 0.1 (mean of rounded samples would give 0.1 too, so use 3 samples).
        var samples = Fx.Run(14, 5, 0.1m, 0.1m, 0.2m); // mean 0.1333
        Assert.Equal(0.1m, PriceMath.HourMean(samples, Fx.At(15, 0)).Mean);

        samples = Fx.Run(14, 5, 0.1m, 0.2m); // mean 0.15 rounds up to 0.2
        Assert.Equal(0.2m, PriceMath.HourMean(samples, Fx.At(15, 0)).Mean);
    }
}
