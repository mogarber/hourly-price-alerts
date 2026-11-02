using System.Text.Json;
using HourlyPriceAlerts.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HourlyPriceAlerts.Functions;

internal static class HttpResults
{
    public const int MaxBodyBytes = 16 * 1024;

    public static string? Authorization(HttpRequest req) => req.Headers.Authorization.FirstOrDefault();

    public static IActionResult Json(int status, object body) => new ContentResult
    {
        StatusCode = status,
        ContentType = "application/json",
        Content = JsonSerializer.Serialize(body, body.GetType(), ApiJson.Options),
    };

    public static IActionResult Error(int status, string message) => Json(status, new { error = message });

    public static IActionResult From(ServiceResult result)
    {
        var status = (int)result.Status;
        if (result.Error is not null)
        {
            return Error(status, result.Error);
        }

        return result.Body is not null ? Json(status, result.Body) : new StatusCodeResult(status);
    }

    /// <summary>Reads a JSON body of at most 16 KB. On failure returns the 400 response to send.</summary>
    public static async Task<(T? Value, IActionResult? Failure)> ReadJsonAsync<T>(HttpRequest req, CancellationToken ct)
        where T : class
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await req.Body.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBodyBytes)
            {
                return (null, Error(400, "Request body is larger than 16 KB."));
            }
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(buffer.ToArray(), ApiJson.Options);
            return value is null ? (null, Error(400, "Request body is empty.")) : (value, null);
        }
        catch (JsonException ex)
        {
            var where = string.IsNullOrEmpty(ex.Path) ? "" : $" (at {ex.Path})";
            return (null, Error(400, $"Request body is not valid{where}."));
        }
    }
}
