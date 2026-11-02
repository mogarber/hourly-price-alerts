using System.Net.Http.Headers;

namespace HourlyPriceAlerts.Core;

public sealed class ComEdFeedClient(HttpClient http) : IFeedClient
{
    public const string FiveMinuteUrl = "https://hourlypricing.comed.com/api?type=5minutefeed";
    public const string CurrentHourAverageUrl = "https://hourlypricing.comed.com/api?type=currenthouraverage";

    public async Task<IReadOnlyList<Sample>> GetFiveMinuteFeedAsync(CancellationToken ct)
        => FeedParser.Parse(await GetAsync(FiveMinuteUrl, ct));

    public async Task<decimal?> GetCurrentHourAverageAsync(CancellationToken ct)
    {
        var samples = FeedParser.Parse(await GetAsync(CurrentHourAverageUrl, ct));
        return samples.Count == 0 ? null : samples[^1].Price;
    }

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(AppInfo.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
