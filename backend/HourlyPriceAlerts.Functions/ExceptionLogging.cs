using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace HourlyPriceAlerts.Functions;

/// <summary>Records unhandled exceptions, which otherwise reach the logs only as "Executed (Failed)" with no cause.</summary>
public sealed class ExceptionLogging(ILogger<ExceptionLogging> logger) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Function {FunctionName} failed.", context.FunctionDefinition.Name);
            throw;
        }
    }
}
