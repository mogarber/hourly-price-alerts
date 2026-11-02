namespace HourlyPriceAlerts.Core;

/// <summary>The mean of one hour bucket. Mean is already rounded to 1 decimal; it is 0 when Count is 0.</summary>
public readonly record struct HourStats(decimal Mean, int Count);

public static class PriceMath
{
    /// <summary>Rounds to 1 decimal, midpoint away from zero. Never returns negative zero.</summary>
    public static decimal Round(decimal value)
    {
        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return rounded == 0m ? 0.0m : rounded;
    }

    /// <summary>
    /// The end of the hour a sample belongs to: the stamp rounded up to the next whole UTC hour,
    /// or the stamp itself when it is already on the hour. A sample on the hour closes the previous hour.
    /// </summary>
    public static DateTimeOffset HourEnd(DateTimeOffset stamp)
    {
        var ticks = stamp.UtcTicks;
        var hour = TimeSpan.TicksPerHour;
        var end = (ticks + hour - 1) / hour * hour;
        return new DateTimeOffset(end, TimeSpan.Zero);
    }

    /// <summary>Mean of the samples with stamps in (hourEnd - 60 min, hourEnd], rounded to 1 decimal.</summary>
    public static HourStats HourMean(IEnumerable<Sample> samples, DateTimeOffset hourEnd)
    {
        var start = hourEnd - TimeSpan.FromHours(1);
        decimal sum = 0;
        var count = 0;
        foreach (var sample in samples)
        {
            if (sample.Stamp > start && sample.Stamp <= hourEnd)
            {
                sum += sample.Price;
                count++;
            }
        }

        return count == 0 ? new HourStats(0m, 0) : new HourStats(Round(sum / count), count);
    }
}
