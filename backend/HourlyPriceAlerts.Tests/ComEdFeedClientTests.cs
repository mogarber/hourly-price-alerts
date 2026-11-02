using System.Net;
using HourlyPriceAlerts.Core;

namespace HourlyPriceAlerts.Tests;

public class ComEdFeedClientTests
{
    private static string Json(params (DateTimeOffset Stamp, string Price)[] rows)
        => "[" + string.Join(",", rows.Select(r => $"{{\"millisUTC\":\"{r.Stamp.ToUnixTimeMilliseconds()}\",\"price\":\"{r.Price}\"}}")) + "]";

    [Fact]
    public async Task Five_minute_feed_uses_the_documented_url_and_a_user_agent()
    {
        var handler = new FakeHandler(_ => Http.Status(HttpStatusCode.OK, Json((Fx.At(18, 10), "1.4"), (Fx.At(18, 5), "1.3"))));

        var samples = await new ComEdFeedClient(new HttpClient(handler)).GetFiveMinuteFeedAsync(default);

        var request = handler.Requests.Single();
        Assert.Equal("https://hourlypricing.comed.com/api?type=5minutefeed", request.Uri.ToString());
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Matches(@"^HourlyPriceAlerts/\d+\.\d+\.\d+$", request.Headers["User-Agent"]);
        Assert.Equal([Fx.At(18, 5), Fx.At(18, 10)], samples.Select(s => s.Stamp));
    }

    [Fact]
    public async Task Current_hour_average_uses_its_url_and_returns_the_price()
    {
        var handler = new FakeHandler(_ => Http.Status(HttpStatusCode.OK, Json((Fx.At(18, 10), "1.4"))));

        var value = await new ComEdFeedClient(new HttpClient(handler)).GetCurrentHourAverageAsync(default);

        Assert.Equal("https://hourlypricing.comed.com/api?type=currenthouraverage", handler.Requests.Single().Uri.ToString());
        Assert.Equal(1.4m, value);
    }

    [Fact]
    public async Task Current_hour_average_is_null_when_there_is_no_usable_row()
    {
        var handler = new FakeHandler(_ => Http.Status(HttpStatusCode.OK, "[{\"price\":\"x\"}]"));
        Assert.Null(await new ComEdFeedClient(new HttpClient(handler)).GetCurrentHourAverageAsync(default));
    }

    [Fact]
    public async Task Http_errors_throw()
    {
        var handler = new FakeHandler(_ => Http.Status(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<HttpRequestException>(() => new ComEdFeedClient(new HttpClient(handler)).GetFiveMinuteFeedAsync(default));
    }
}
