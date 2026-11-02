using System.Text.Json;
using HourlyPriceAlerts.Core;

namespace HourlyPriceAlerts.Tests;

public class FeedParserTests
{
    private static string Row(string millis, string price) => $"{{\"millisUTC\":\"{millis}\",\"price\":\"{price}\"}}";

    private static string Millis(DateTimeOffset t) => t.ToUnixTimeMilliseconds().ToString();

    [Fact]
    public void Parses_the_documented_shape_including_negative_prices()
    {
        var json = $"[{Row(Millis(Fx.At(18, 10)), "1.4")},{Row(Millis(Fx.At(18, 5)), "-0.3")}]";

        var samples = FeedParser.Parse(json);

        Assert.Equal(2, samples.Count);
        Assert.Equal(new Sample(Fx.At(18, 5), -0.3m), samples[0]);
        Assert.Equal(new Sample(Fx.At(18, 10), 1.4m), samples[1]);
    }

    [Fact]
    public void Parses_the_exact_example_from_the_spec()
    {
        var samples = FeedParser.Parse("[{\"millisUTC\":\"1793642400000\",\"price\":\"1.4\"}]");

        Assert.Single(samples);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793642400000), samples[0].Stamp);
        Assert.Equal(1.4m, samples[0].Price);
    }

    [Fact]
    public void Sorts_ascending_whatever_the_input_order()
    {
        var times = new[] { Fx.At(18, 20), Fx.At(18, 5), Fx.At(18, 15), Fx.At(18, 10) };
        var json = "[" + string.Join(",", times.Select((t, i) => Row(Millis(t), i.ToString()))) + "]";

        var samples = FeedParser.Parse(json);

        Assert.Equal(times.Order(), samples.Select(s => s.Stamp));
    }

    [Fact]
    public void Keeps_one_sample_per_stamp_and_the_first_one_wins()
    {
        var stamp = Millis(Fx.At(18, 5));
        var json = $"[{Row(stamp, "1.0")},{Row(Millis(Fx.At(18, 10)), "2.0")},{Row(stamp, "9.9")}]";

        var samples = FeedParser.Parse(json);

        Assert.Equal(2, samples.Count);
        Assert.Equal(1.0m, samples[0].Price);
    }

    [Fact]
    public void Gaps_are_kept_as_they_are()
    {
        var json = $"[{Row(Millis(Fx.At(18, 5)), "1")},{Row(Millis(Fx.At(18, 40)), "2")}]";
        Assert.Equal(2, FeedParser.Parse(json).Count);
    }

    [Theory]
    [InlineData("{\"millisUTC\":\"1793642400000\"}")]                          // price missing
    [InlineData("{\"price\":\"1.4\"}")]                                        // stamp missing
    [InlineData("{\"millisUTC\":\"abc\",\"price\":\"1.4\"}")]                  // stamp not numeric
    [InlineData("{\"millisUTC\":\"1793642400000\",\"price\":\"n/a\"}")]        // price not numeric
    [InlineData("{\"millisUTC\":\"1793642400000\",\"price\":\"\"}")]           // price empty
    [InlineData("{\"millisUTC\":\"1793642400000\",\"price\":\"1e3\"}")]        // exponent not allowed
    [InlineData("{\"millisUTC\":null,\"price\":\"1.4\"}")]
    [InlineData("{\"millisUTC\":\"1793642400000\",\"price\":null}")]
    [InlineData("{\"millisUTC\":{},\"price\":\"1.4\"}")]
    [InlineData("{\"millisUTC\":\"99999999999999999999\",\"price\":\"1.4\"}")] // overflows long
    [InlineData("{\"millisUTC\":\"9999999999999999\",\"price\":\"1.4\"}")]     // out of DateTimeOffset range
    [InlineData("{\"millisUTC\":\"-1793642400000\",\"price\":\"1.4\"}")]       // negative stamp not accepted
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("\"text\"")]
    [InlineData("[]")]
    public void Bad_rows_are_skipped_and_good_rows_survive(string badRow)
    {
        var good = Row(Millis(Fx.At(18, 5)), "1.4");
        var samples = FeedParser.Parse($"[{badRow},{good},{badRow}]");

        Assert.Equal([new Sample(Fx.At(18, 5), 1.4m)], samples);
    }

    [Fact]
    public void Accepts_numbers_as_well_as_strings()
    {
        var samples = FeedParser.Parse($"[{{\"millisUTC\":{Millis(Fx.At(18, 5))},\"price\":1.4}}]");
        Assert.Equal([new Sample(Fx.At(18, 5), 1.4m)], samples);
    }

    [Fact]
    public void A_single_object_is_read_as_one_row()
    {
        var samples = FeedParser.Parse(Row(Millis(Fx.At(18, 5)), "1.4"));
        Assert.Single(samples);
    }

    [Fact]
    public void Empty_array_gives_no_samples() => Assert.Empty(FeedParser.Parse("[]"));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[{")]
    [InlineData("42")]
    [InlineData("\"x\"")]
    public void A_document_that_is_not_an_array_or_object_throws(string json)
        => Assert.ThrowsAny<JsonException>(() => FeedParser.Parse(json));
}
