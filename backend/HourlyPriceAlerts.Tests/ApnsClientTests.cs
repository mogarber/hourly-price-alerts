using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HourlyPriceAlerts.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace HourlyPriceAlerts.Tests;

public class ApnsClientTests
{
    private const string InstallId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string RuleId = "11111111-1111-4111-8111-111111111111";
    private static readonly string Token = new('a', 64);

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly FakeClock _clock = new(Fx.At(14, 0));
    private readonly InMemoryStore _store = new();

    private ApnsOptions Options() => new()
    {
        KeyId = "KEY1234567",
        TeamId = "TEAM123456",
        BundleId = "io.github.mogarber.hourlypricealerts",
        PrivateKey = _key.ExportPkcs8PrivateKeyPem(),
        RetryDelay = TimeSpan.Zero,
    };

    private ApnsClient Client(FakeHandler handler, ApnsOptions? options = null)
        => new(new HttpClient(handler), options ?? Options(), _clock, _store, NullLogger<ApnsClient>.Instance);

    private static FakeHandler Always(HttpStatusCode code, string? reason = null)
        => new(_ => reason is null ? Http.Status(code) : Http.Reason(code, reason));

    private static PushMessage Message(ApnsEnvironment env = ApnsEnvironment.Production, string? token = null)
        => new(InstallId, token ?? Token, env, "Price above 10.0¢", "Hourly average is 12.4¢ as of 2:05 PM.", RuleId, RuleId, 12.4m);

    private static string AuthToken(RecordedRequest r)
    {
        var header = r.Headers["authorization"];
        Assert.StartsWith("bearer ", header);
        return header["bearer ".Length..];
    }

    // ---- Request shape ----

    [Fact]
    public async Task Sends_the_documented_request_to_the_production_host()
    {
        var handler = Always(HttpStatusCode.OK);

        var result = await Client(handler).SendAsync(Message(), default);

        Assert.Equal(PushResult.Sent, result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://api.push.apple.com/3/device/{Token}", request.Uri.ToString());
        Assert.Equal("io.github.mogarber.hourlypricealerts", request.Headers["apns-topic"]);
        Assert.Equal("alert", request.Headers["apns-push-type"]);
        Assert.Equal("10", request.Headers["apns-priority"]);
        Assert.Equal((Fx.At(15, 0)).ToUnixTimeSeconds().ToString(), request.Headers["apns-expiration"]);
        Assert.Equal(RuleId, request.Headers["apns-collapse-id"]);
        Assert.StartsWith("application/json", request.Headers["Content-Type"]);
        Assert.StartsWith("bearer ", request.Headers["authorization"]);
    }

    [Fact]
    public async Task Requests_are_http2_only()
    {
        var handler = Always(HttpStatusCode.OK);
        await Client(handler).SendAsync(Message(), default);

        var request = handler.Requests.Single();
        Assert.Equal(HttpVersion.Version20, request.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);
    }

    [Fact]
    public async Task Sandbox_devices_use_the_sandbox_host()
    {
        var handler = Always(HttpStatusCode.OK);
        await Client(handler).SendAsync(Message(ApnsEnvironment.Sandbox), default);
        Assert.Equal("api.sandbox.push.apple.com", handler.Requests.Single().Uri.Host);
    }

    [Fact]
    public async Task Expiration_is_one_hour_after_the_injected_clock()
    {
        var handler = Always(HttpStatusCode.OK);
        _clock.UtcNow = Fx.At(9, 30);
        await Client(handler).SendAsync(Message(), default);
        Assert.Equal(Fx.At(10, 30).ToUnixTimeSeconds().ToString(), handler.Requests.Single().Headers["apns-expiration"]);
    }

    [Fact]
    public async Task Payload_matches_the_spec()
    {
        var handler = Always(HttpStatusCode.OK);
        await Client(handler).SendAsync(Message(), default);

        using var doc = JsonDocument.Parse(handler.Requests.Single().Body);
        var root = doc.RootElement;
        Assert.Equal("Price above 10.0¢", root.GetProperty("aps").GetProperty("alert").GetProperty("title").GetString());
        Assert.Equal("Hourly average is 12.4¢ as of 2:05 PM.", root.GetProperty("aps").GetProperty("alert").GetProperty("body").GetString());
        Assert.Equal("default", root.GetProperty("aps").GetProperty("sound").GetString());
        Assert.Equal(RuleId, root.GetProperty("ruleId").GetString());
        Assert.Equal(12.4m, root.GetProperty("valueCents").GetDecimal());
        Assert.Equal(["aps", "ruleId", "valueCents"], root.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Test_push_payload_has_no_rule_id_or_value()
    {
        var message = new PushMessage(InstallId, Token, ApnsEnvironment.Sandbox, "Hourly Price Alerts test", "ok", "test", null, null);
        using var doc = JsonDocument.Parse(ApnsPayload.Build(message));
        Assert.Equal(["aps"], doc.RootElement.EnumerateObject().Select(p => p.Name));
    }

    // ---- JWT ----

    [Fact]
    public async Task Jwt_has_the_documented_structure_and_a_verifiable_es256_signature()
    {
        var handler = Always(HttpStatusCode.OK);
        await Client(handler).SendAsync(Message(), default);

        var jwt = AuthToken(handler.Requests.Single());
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.All(new[] { '=', '+', '/' }, c => Assert.DoesNotContain(c, jwt));   // base64url, unpadded

        using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
        Assert.Equal(["alg", "kid"], header.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("ES256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("KEY1234567", header.RootElement.GetProperty("kid").GetString());

        using var claims = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        Assert.Equal(["iat", "iss"], claims.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("TEAM123456", claims.RootElement.GetProperty("iss").GetString());
        Assert.Equal(Fx.At(14, 0).ToUnixTimeSeconds(), claims.RootElement.GetProperty("iat").GetInt64());

        var signature = Base64UrlDecode(parts[2]);
        Assert.Equal(64, signature.Length);   // IEEE P1363: r || s, 32 bytes each
        Assert.True(_key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature,
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public async Task Jwt_is_reused_for_40_minutes_then_replaced()
    {
        var handler = Always(HttpStatusCode.OK);
        var client = Client(handler);

        await client.SendAsync(Message(), default);
        _clock.Advance(TimeSpan.FromMinutes(39) + TimeSpan.FromSeconds(59));
        await client.SendAsync(Message(), default);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await client.SendAsync(Message(), default);

        var tokens = handler.Requests.Select(AuthToken).ToList();
        Assert.Equal(tokens[0], tokens[1]);
        Assert.NotEqual(tokens[0], tokens[2]);
        using var claims = JsonDocument.Parse(Base64UrlDecode(tokens[2].Split('.')[1]));
        Assert.Equal(_clock.UtcNow.ToUnixTimeSeconds(), claims.RootElement.GetProperty("iat").GetInt64());
    }

    [Fact]
    public async Task Jwt_is_shared_by_concurrent_sends()
    {
        var handler = new FakeHandler(async (_, _) =>
        {
            await Task.Delay(5);
            return Http.Status(HttpStatusCode.OK);
        });
        var client = Client(handler);

        await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => client.SendAsync(Message(), default)));

        Assert.Single(handler.Requests.Select(AuthToken).Distinct());
    }

    [Fact]
    public async Task Missing_configuration_fails_without_sending()
    {
        var handler = Always(HttpStatusCode.OK);
        var client = Client(handler, new ApnsOptions { BundleId = "x" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(Message(), default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Private_key_with_escaped_newlines_is_accepted()
    {
        var options = Options();
        options.PrivateKey = options.PrivateKey.Replace("\r", "").Replace("\n", "\\n");
        var handler = Always(HttpStatusCode.OK);

        Assert.Equal(PushResult.Sent, await Client(handler, options).SendAsync(Message(), default));
    }

    // ---- Dead tokens ----

    private async Task<PushResult> SendToRegisteredDevice(FakeHandler handler)
    {
        _store.Devices[InstallId] = new DeviceRecord(InstallId, "hash", Token, ApnsEnvironment.Production, Fx.Chicago, null);
        _store.Rules[(InstallId, RuleId)] = new RuleRecord(InstallId, Fx.Rule(id: RuleId), RuleState.New);
        return await Client(handler).SendAsync(Message(), default);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, "Unregistered")]
    [InlineData(HttpStatusCode.Gone, null)]
    [InlineData(HttpStatusCode.BadRequest, "BadDeviceToken")]
    [InlineData(HttpStatusCode.BadRequest, "Unregistered")]
    public async Task Dead_tokens_delete_the_device_and_its_rules_without_retrying(HttpStatusCode code, string? reason)
    {
        var handler = Always(code, reason);

        var result = await SendToRegisteredDevice(handler);

        Assert.Equal(PushResult.DeviceRemoved, result);
        Assert.Empty(_store.Devices);
        Assert.Empty(_store.Rules);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "PayloadEmpty")]
    [InlineData(HttpStatusCode.BadRequest, "DeviceTokenNotForTopic")]
    [InlineData(HttpStatusCode.BadRequest, null)]
    [InlineData(HttpStatusCode.Forbidden, "BadCertificate")]
    [InlineData(HttpStatusCode.Forbidden, "InvalidProviderToken")]
    public async Task Other_rejections_keep_the_device_and_do_not_retry(HttpStatusCode code, string? reason)
    {
        var handler = Always(code, reason);

        var result = await SendToRegisteredDevice(handler);

        Assert.Equal(PushResult.Failed, result);
        Assert.Single(_store.Devices);
        Assert.Single(_store.Rules);
        Assert.Single(handler.Requests);
    }

    // ---- Retries ----

    [Fact]
    public async Task Expired_provider_token_refreshes_the_jwt_and_retries_once()
    {
        var handler = new FakeHandler((_, n) => n == 1
            ? Task.FromResult(Http.Reason(HttpStatusCode.Forbidden, "ExpiredProviderToken"))
            : Task.FromResult(Http.Status(HttpStatusCode.OK)));
        var client = Client(handler);

        var result = await client.SendAsync(Message(), default);

        Assert.Equal(PushResult.Sent, result);
        var tokens = handler.Requests.Select(AuthToken).ToList();
        Assert.Equal(2, tokens.Count);
        Assert.NotEqual(tokens[0], tokens[1]);

        // The refreshed token is now the cached one.
        await client.SendAsync(Message(), default);
        Assert.Equal(tokens[1], AuthToken(handler.Requests.Last()));
    }

    [Fact]
    public async Task Expired_provider_token_twice_gives_up_after_one_retry()
    {
        var handler = Always(HttpStatusCode.Forbidden, "ExpiredProviderToken");

        var result = await Client(handler).SendAsync(Message(), default);

        Assert.Equal(PushResult.Failed, result);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Throttling_and_server_errors_are_retried_once_then_succeed(HttpStatusCode first)
    {
        var handler = new FakeHandler((_, n) => Task.FromResult(n == 1 ? Http.Status(first) : Http.Status(HttpStatusCode.OK)));

        var result = await Client(handler).SendAsync(Message(), default);

        Assert.Equal(PushResult.Sent, result);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Persistent_throttling_or_server_errors_stop_after_one_retry(HttpStatusCode code)
    {
        var handler = Always(code);

        var result = await Client(handler).SendAsync(Message(), default);

        Assert.Equal(PushResult.Failed, result);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(handler.Requests.Select(AuthToken).Distinct());   // no needless JWT refresh
    }

    [Fact]
    public async Task Network_errors_are_retried_once_like_server_errors()
    {
        var handler = new FakeHandler((_, n) => n == 1
            ? throw new HttpRequestException("connection reset")
            : Task.FromResult(Http.Status(HttpStatusCode.OK)));

        Assert.Equal(PushResult.Sent, await Client(handler).SendAsync(Message(), default));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Retry_waits_for_the_configured_delay()
    {
        var options = Options();
        options.RetryDelay = TimeSpan.FromMilliseconds(150);
        var handler = new FakeHandler((_, n) => Task.FromResult(Http.Status(n == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)));
        var started = DateTime.UtcNow;

        await Client(handler, options).SendAsync(Message(), default);

        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(140));
    }

    [Fact]
    public async Task A_cancelled_send_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Client(Always(HttpStatusCode.OK)).SendAsync(Message(), cts.Token));
    }

    // ---- Concurrency ----

    [Fact]
    public async Task At_most_20_pushes_are_in_flight_at_once()
    {
        var inFlight = 0;
        var peak = 0;
        var handler = new FakeHandler(async (_, _) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            int seen;
            while ((seen = Volatile.Read(ref peak)) < now && Interlocked.CompareExchange(ref peak, now, seen) != seen)
            {
            }

            await Task.Delay(40);
            Interlocked.Decrement(ref inFlight);
            return Http.Status(HttpStatusCode.OK);
        });
        var client = Client(handler);

        var results = await Task.WhenAll(Enumerable.Range(0, 80).Select(_ => client.SendAsync(Message(), default)));

        Assert.All(results, r => Assert.Equal(PushResult.Sent, r));
        Assert.Equal(80, handler.Requests.Count);
        Assert.Equal(ApnsClient.MaxConcurrentPushes, peak);
        Assert.Equal(20, ApnsClient.MaxConcurrentPushes);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s += new string('=', (4 - s.Length % 4) % 4);
        return Convert.FromBase64String(s);
    }
}
