using HourlyPriceAlerts.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HourlyPriceAlerts.Functions;

/// <summary>
/// Thin HTTP layer. Auth level is Anonymous on purpose: a function key shipped in an open-source app would not be secret.
/// Each install authenticates with its own secret instead (see DeviceService). Request bodies and the Authorization
/// header are never logged.
/// </summary>
public sealed class ApiFunctions(DeviceService devices, HealthService health)
{
    [Function("PutDevice")]
    public async Task<IActionResult> PutDevice(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v1/devices/{installId}")] HttpRequest req,
        string installId,
        CancellationToken ct)
    {
        var (body, failure) = await HttpResults.ReadJsonAsync<DeviceRequest>(req, ct);
        if (failure is not null)
        {
            // Unauthenticated callers should learn about auth problems before body problems.
            return DeviceService.TryReadSecret(HttpResults.Authorization(req), out _) ? failure : HttpResults.From(ServiceResult.Unauthorized());
        }

        return HttpResults.From(await devices.PutDeviceAsync(installId, HttpResults.Authorization(req), body!, ct));
    }

    [Function("DeleteDevice")]
    public async Task<IActionResult> DeleteDevice(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/devices/{installId}")] HttpRequest req,
        string installId,
        CancellationToken ct)
        => HttpResults.From(await devices.DeleteDeviceAsync(installId, HttpResults.Authorization(req), ct));

    [Function("GetRules")]
    public async Task<IActionResult> GetRules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/devices/{installId}/rules")] HttpRequest req,
        string installId,
        CancellationToken ct)
        => HttpResults.From(await devices.GetRulesAsync(installId, HttpResults.Authorization(req), ct));

    [Function("PutRule")]
    public async Task<IActionResult> PutRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v1/devices/{installId}/rules/{ruleId}")] HttpRequest req,
        string installId,
        string ruleId,
        CancellationToken ct)
    {
        var (body, failure) = await HttpResults.ReadJsonAsync<Rule>(req, ct);
        if (failure is not null)
        {
            return DeviceService.TryReadSecret(HttpResults.Authorization(req), out _) ? failure : HttpResults.From(ServiceResult.Unauthorized());
        }

        return HttpResults.From(await devices.PutRuleAsync(installId, ruleId, HttpResults.Authorization(req), body!, ct));
    }

    [Function("DeleteRule")]
    public async Task<IActionResult> DeleteRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/devices/{installId}/rules/{ruleId}")] HttpRequest req,
        string installId,
        string ruleId,
        CancellationToken ct)
        => HttpResults.From(await devices.DeleteRuleAsync(installId, ruleId, HttpResults.Authorization(req), ct));

    [Function("SendTest")]
    public async Task<IActionResult> SendTest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/devices/{installId}/test")] HttpRequest req,
        string installId,
        CancellationToken ct)
        => HttpResults.From(await devices.SendTestAsync(installId, HttpResults.Authorization(req), ct));

    [Function("Health")]
    public async Task<IActionResult> Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req,
        CancellationToken ct)
        => HttpResults.Json(200, await health.GetAsync(ct));
}
