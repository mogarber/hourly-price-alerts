namespace HourlyPriceAlerts.Core;

public static class AppInfo
{
    public static string UserAgent { get; } =
        "HourlyPriceAlerts/" + (typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
}
