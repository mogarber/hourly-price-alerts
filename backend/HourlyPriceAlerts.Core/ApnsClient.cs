using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HourlyPriceAlerts.Core;

public sealed class ApnsOptions
{
    public string KeyId { get; set; } = "";
    public string TeamId { get; set; } = "";
    public string BundleId { get; set; } = "";

    /// <summary>The .p8 key as PEM text. Literal backslash-n sequences are accepted for single-line settings.</summary>
    public string PrivateKey { get; set; } = "";

    /// <summary>Wait before the single retry after a 429, 5xx or network error.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}

public static class ApnsPayload
{
    private sealed record Alert(string Title, string Body);

    private sealed record Aps(Alert Alert, string Sound);

    private sealed record Payload(
        Aps Aps,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RuleId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? ValueCents);

    public static byte[] Build(PushMessage message)
        => JsonSerializer.SerializeToUtf8Bytes(
            new Payload(new Aps(new Alert(message.Title, message.Body), "default"), message.RuleId, message.ValueCents),
            ApiJson.Options);
}

/// <summary>Sends pushes over HTTP/2 with a cached ES256 provider token. One instance must be shared (it owns the token and the concurrency cap).</summary>
public sealed class ApnsClient : IPushSender
{
    public const string ProductionHost = "api.push.apple.com";
    public const string SandboxHost = "api.sandbox.push.apple.com";
    public const int MaxConcurrentPushes = 20;
    public static readonly TimeSpan JwtLifetime = TimeSpan.FromMinutes(40);

    private readonly HttpClient _http;
    private readonly ApnsOptions _options;
    private readonly IClock _clock;
    private readonly IAlertStore _store;
    private readonly ILogger<ApnsClient> _logger;
    private readonly SemaphoreSlim _concurrency = new(MaxConcurrentPushes);

    private readonly object _jwtLock = new();
    private string? _jwt;
    private DateTimeOffset _jwtIssuedAt;

    public ApnsClient(HttpClient http, ApnsOptions options, IClock clock, IAlertStore store, ILogger<ApnsClient> logger)
    {
        _http = http;
        _options = options;
        _clock = clock;
        _store = store;
        _logger = logger;
    }

    public async Task<PushResult> SendAsync(PushMessage message, CancellationToken ct)
    {
        var payload = ApnsPayload.Build(message);

        var response = await SendOnceAsync(message, payload, ct);
        if (response.Status == 403 && response.Reason == "ExpiredProviderToken")
        {
            ForgetJwt(response.Jwt);
            response = await SendOnceAsync(message, payload, ct);
        }
        else if (response.Status is 0 or 429 or >= 500)
        {
            await Task.Delay(_options.RetryDelay, ct);
            response = await SendOnceAsync(message, payload, ct);
        }

        if (response.Status == 200)
        {
            return PushResult.Sent;
        }

        if (response.Status == 410
            || (response.Status == 400 && response.Reason is "BadDeviceToken" or "Unregistered"))
        {
            _logger.LogInformation("APNs rejected the device token ({Status} {Reason}); deleting device {InstallId}.",
                response.Status, response.Reason, message.InstallId);
            await _store.DeleteDeviceAsync(message.InstallId, ct);
            return PushResult.DeviceRemoved;
        }

        _logger.LogWarning("APNs push to device {InstallId} failed: {Status} {Reason}.",
            message.InstallId, response.Status, response.Reason);
        return PushResult.Failed;
    }

    private sealed record ApnsResponse(int Status, string? Reason, string Jwt);

    private async Task<ApnsResponse> SendOnceAsync(PushMessage message, byte[] payload, CancellationToken ct)
    {
        var jwt = GetJwt();
        var host = message.Environment == ApnsEnvironment.Sandbox ? SandboxHost : ProductionHost;

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://{host}/3/device/{Uri.EscapeDataString(message.ApnsToken)}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", jwt);
        request.Headers.TryAddWithoutValidation("apns-topic", _options.BundleId);
        request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");
        request.Headers.TryAddWithoutValidation("apns-expiration",
            (_clock.UtcNow + TimeSpan.FromHours(1)).ToUnixTimeSeconds().ToString());
        request.Headers.TryAddWithoutValidation("apns-collapse-id", message.CollapseId);

        await _concurrency.WaitAsync(ct);
        try
        {
            using var response = await _http.SendAsync(request, ct);
            string? reason = null;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                reason = await ReadReasonAsync(response, ct);
            }

            return new ApnsResponse((int)response.StatusCode, reason, jwt);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Network failure or timeout: treated like a 5xx. The exception text is not logged because it can include the URL (and so the token).
            _logger.LogWarning("APNs request failed: {ExceptionType}.", ex.GetType().Name);
            return new ApnsResponse(0, null, jwt);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private static async Task<string?> ReadReasonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Returns the cached provider token, minting a new one every 40 minutes.</summary>
    private string GetJwt()
    {
        lock (_jwtLock)
        {
            var now = _clock.UtcNow;
            if (_jwt is null || now - _jwtIssuedAt >= JwtLifetime)
            {
                _jwt = CreateJwt(now);
                _jwtIssuedAt = now;
            }

            return _jwt;
        }
    }

    /// <summary>Drops the cached token, but only if it is still the one that was rejected.</summary>
    private void ForgetJwt(string rejected)
    {
        lock (_jwtLock)
        {
            if (_jwt == rejected)
            {
                _jwt = null;
            }
        }
    }

    private string CreateJwt(DateTimeOffset now)
    {
        if (_options.PrivateKey.Length == 0 || _options.KeyId.Length == 0 || _options.TeamId.Length == 0)
        {
            throw new InvalidOperationException("APNs is not configured (Apns__KeyId, Apns__TeamId, Apns__PrivateKey).");
        }

        var header = JsonSerializer.Serialize(new { alg = "ES256", kid = _options.KeyId });
        var claims = JsonSerializer.Serialize(new { iss = _options.TeamId, iat = now.ToUnixTimeSeconds() });
        var signingInput = Base64Url(Encoding.UTF8.GetBytes(header)) + "." + Base64Url(Encoding.UTF8.GetBytes(claims));

        using var key = ECDsa.Create();
        key.ImportFromPem(_options.PrivateKey.Replace("\\n", "\n"));
        var signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return signingInput + "." + Base64Url(signature);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
