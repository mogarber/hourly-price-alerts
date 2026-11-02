namespace HourlyPriceAlerts.Core;

/// <summary>The status values are the HTTP status codes the API returns.</summary>
public enum ResultStatus
{
    Ok = 200,
    Accepted = 202,
    NoContent = 204,
    BadRequest = 400,
    Unauthorized = 401,
    NotFound = 404,
    TooManyRequests = 429,
    BadGateway = 502,
}

public sealed record ServiceResult(ResultStatus Status, object? Body = null, string? Error = null)
{
    public static ServiceResult Ok(object body) => new(ResultStatus.Ok, body);
    public static ServiceResult Accepted() => new(ResultStatus.Accepted);
    public static ServiceResult NoContent() => new(ResultStatus.NoContent);
    public static ServiceResult BadRequest(string error) => new(ResultStatus.BadRequest, Error: error);
    public static ServiceResult Unauthorized() => new(ResultStatus.Unauthorized, Error: "Missing or invalid credentials.");
    public static ServiceResult NotFound(string error) => new(ResultStatus.NotFound, Error: error);
    public static ServiceResult TooManyRequests(string error) => new(ResultStatus.TooManyRequests, Error: error);
    public static ServiceResult BadGateway(string error) => new(ResultStatus.BadGateway, Error: error);

    public bool IsSuccess => (int)Status < 400;
}
