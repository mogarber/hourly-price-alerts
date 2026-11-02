namespace HourlyPriceAlerts.Core;

public static class QuietHoursCheck
{
    /// <summary>
    /// True when the local time of <paramref name="nowUtc"/> falls in the quiet window. Start is inclusive and end
    /// exclusive. A window with start after end wraps past midnight, and start equal to end means no quiet hours.
    /// </summary>
    public static bool IsQuiet(QuietHours? quietHours, DateTimeOffset nowUtc, TimeZoneInfo timeZone)
    {
        if (quietHours is null || quietHours.StartMinute == quietHours.EndMinute)
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var minute = local.Hour * 60 + local.Minute;
        var start = quietHours.StartMinute;
        var end = quietHours.EndMinute;

        return start < end
            ? minute >= start && minute < end
            : minute >= start || minute < end;
    }
}
