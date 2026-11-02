using HourlyPriceAlerts.Core;
using Microsoft.Azure.Functions.Worker;

namespace HourlyPriceAlerts.Functions;

public sealed class PollFunction(PollService poller)
{
    [Function("Poll")]
    public Task Run([TimerTrigger("0 * * * * *")] TimerInfo timer, CancellationToken ct)
        => poller.RunAsync(ct);
}
