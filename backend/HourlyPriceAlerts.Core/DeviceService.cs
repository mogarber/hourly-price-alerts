using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HourlyPriceAlerts.Core;

/// <summary>
/// The logic behind the /v1/devices routes. Callers pass the raw Authorization header; every route except the first
/// PUT of an install requires the install's secret.
/// </summary>
public sealed partial class DeviceService(IAlertStore store, IPushSender push, IClock clock)
{
    public static readonly TimeSpan TestCooldown = TimeSpan.FromSeconds(60);

    public async Task<ServiceResult> PutDeviceAsync(
        string installId, string? authorization, DeviceRequest request, CancellationToken ct)
    {
        if (!RuleValidator.IsLowercaseUuid(installId))
        {
            return ServiceResult.BadRequest("installId must be a lowercase UUID.");
        }

        if (!TryReadSecret(authorization, out var secret))
        {
            return ServiceResult.Unauthorized();
        }

        var hash = HashSecret(secret);
        var existing = await store.GetDeviceAsync(installId, ct);
        if (existing is not null && !HashMatches(existing.SecretHash, hash))
        {
            return ServiceResult.Unauthorized();
        }

        // Validate before the first PUT stores the hash, so a bad request does not claim the install id.
        var error = RuleValidator.Validate(request);
        if (error is not null)
        {
            return ServiceResult.BadRequest(error);
        }

        var device = new DeviceRecord(
            installId,
            existing?.SecretHash ?? hash,
            request.ApnsToken,
            request.ApnsEnvironment,
            request.TimeZone,
            existing?.LastTestAt);
        await store.UpsertDeviceAsync(device, ct);

        return ServiceResult.Ok(ToResponse(device));
    }

    /// <summary>Deleting is idempotent: an unknown install id is a 204, since there is nothing to delete.</summary>
    public async Task<ServiceResult> DeleteDeviceAsync(string installId, string? authorization, CancellationToken ct)
    {
        if (!RuleValidator.IsLowercaseUuid(installId))
        {
            return ServiceResult.BadRequest("installId must be a lowercase UUID.");
        }

        if (!TryReadSecret(authorization, out var secret))
        {
            return ServiceResult.Unauthorized();
        }

        var device = await store.GetDeviceAsync(installId, ct);
        if (device is null)
        {
            return ServiceResult.NoContent();
        }

        if (!HashMatches(device.SecretHash, HashSecret(secret)))
        {
            return ServiceResult.Unauthorized();
        }

        await store.DeleteDeviceAsync(installId, ct);
        return ServiceResult.NoContent();
    }

    public async Task<ServiceResult> GetRulesAsync(string installId, string? authorization, CancellationToken ct)
    {
        var (failure, _) = await AuthenticateAsync(installId, authorization, ct);
        if (failure is not null)
        {
            return failure;
        }

        var rules = await store.GetRulesAsync(installId, ct);
        return ServiceResult.Ok(new RulesResponse(rules.Select(r => r.Rule).ToList()));
    }

    public async Task<ServiceResult> PutRuleAsync(
        string installId, string ruleId, string? authorization, Rule rule, CancellationToken ct)
    {
        var (failure, _) = await AuthenticateAsync(installId, authorization, ct);
        if (failure is not null)
        {
            return failure;
        }

        var error = RuleValidator.Validate(rule, ruleId);
        if (error is not null)
        {
            return ServiceResult.BadRequest(error);
        }

        var existing = await store.GetRulesAsync(installId, ct);
        var previous = existing.FirstOrDefault(r => r.Rule.Id == ruleId);
        if (previous is null && existing.Count >= RuleValidator.MaxRulesPerDevice)
        {
            return ServiceResult.BadRequest($"A device can have at most {RuleValidator.MaxRulesPerDevice} rules.");
        }

        // Every PUT re-arms the rule but keeps the last time it fired, so the cooldown still applies.
        var state = new RuleState(true, previous?.State.LastFiredAt);
        await store.UpsertRuleAsync(new RuleRecord(installId, rule, state), ct);
        return ServiceResult.Ok(rule);
    }

    /// <summary>Deleting is idempotent once the caller is authenticated: a missing rule is a 204.</summary>
    public async Task<ServiceResult> DeleteRuleAsync(
        string installId, string ruleId, string? authorization, CancellationToken ct)
    {
        var (failure, _) = await AuthenticateAsync(installId, authorization, ct);
        if (failure is not null)
        {
            return failure;
        }

        if (!RuleValidator.IsLowercaseUuid(ruleId))
        {
            return ServiceResult.BadRequest("ruleId must be a lowercase UUID.");
        }

        await store.DeleteRuleAsync(installId, ruleId, ct);
        return ServiceResult.NoContent();
    }

    public async Task<ServiceResult> SendTestAsync(string installId, string? authorization, CancellationToken ct)
    {
        var (failure, device) = await AuthenticateAsync(installId, authorization, ct);
        if (failure is not null)
        {
            return failure;
        }

        var now = clock.UtcNow;
        if (device!.LastTestAt is { } last && now - last < TestCooldown)
        {
            return ServiceResult.TooManyRequests("A test notification was sent less than a minute ago.");
        }

        // Record the attempt first so repeated requests are throttled even if the push is slow.
        await store.UpsertDeviceAsync(device with { LastTestAt = now }, ct);
        var result = await push.SendAsync(
            new PushMessage(installId, device.ApnsToken, device.ApnsEnvironment,
                "Hourly Price Alerts test", "Notifications are working.", "test", null, null),
            ct);
        return result switch
        {
            PushResult.Sent => ServiceResult.Accepted(),
            PushResult.DeviceRemoved => ServiceResult.NotFound("Apple no longer accepts this device's notification token."),
            _ => ServiceResult.BadGateway("Apple's notification service did not accept the test notification."),
        };
    }

    /// <summary>Accepts "Bearer &lt;64 lowercase hex chars&gt;".</summary>
    public static bool TryReadSecret(string? authorization, out string secret)
    {
        secret = "";
        const string prefix = "Bearer ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = authorization[prefix.Length..];
        if (!SecretPattern().IsMatch(candidate))
        {
            return false;
        }

        secret = candidate;
        return true;
    }

    public static string HashSecret(string secret)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static bool HashMatches(string storedHash, string candidateHash)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(storedHash), Encoding.ASCII.GetBytes(candidateHash));

    private async Task<(ServiceResult? Failure, DeviceRecord? Device)> AuthenticateAsync(
        string installId, string? authorization, CancellationToken ct)
    {
        if (!RuleValidator.IsLowercaseUuid(installId))
        {
            return (ServiceResult.BadRequest("installId must be a lowercase UUID."), null);
        }

        if (!TryReadSecret(authorization, out var secret))
        {
            return (ServiceResult.Unauthorized(), null);
        }

        var device = await store.GetDeviceAsync(installId, ct);
        if (device is null)
        {
            return (ServiceResult.NotFound("Device is not registered."), null);
        }

        return HashMatches(device.SecretHash, HashSecret(secret))
            ? (null, device)
            : (ServiceResult.Unauthorized(), null);
    }

    private static DeviceResponse ToResponse(DeviceRecord d)
        => new(d.InstallId, d.ApnsToken, d.ApnsEnvironment, d.TimeZone, d.LastTestAt);

    [GeneratedRegex("^[0-9a-f]{64}\\z")]
    private static partial Regex SecretPattern();
}
