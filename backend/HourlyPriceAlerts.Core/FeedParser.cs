using System.Globalization;
using System.Text.Json;

namespace HourlyPriceAlerts.Core;

/// <summary>Parses ComEd feed JSON. The feed is untrusted, so bad rows are skipped rather than failing the whole parse.</summary>
public static class FeedParser
{
    /// <summary>
    /// Returns samples sorted ascending by stamp with one sample per stamp (the first one seen wins).
    /// Throws <see cref="JsonException"/> when the document is not JSON or is neither an array nor an object.
    /// </summary>
    public static IReadOnlyList<Sample> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var byStamp = new Dictionary<DateTimeOffset, Sample>();
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var row in root.EnumerateArray())
                {
                    Add(row, byStamp);
                }
                break;
            case JsonValueKind.Object:
                Add(root, byStamp);
                break;
            default:
                throw new JsonException("Feed is not a JSON array.");
        }

        return byStamp.Values.OrderBy(s => s.Stamp).ToList();
    }

    private static void Add(JsonElement row, Dictionary<DateTimeOffset, Sample> byStamp)
    {
        if (row.ValueKind != JsonValueKind.Object
            || !row.TryGetProperty("millisUTC", out var millisElement)
            || !row.TryGetProperty("price", out var priceElement)
            || !TryReadText(millisElement, out var millisText)
            || !TryReadText(priceElement, out var priceText)
            || !long.TryParse(millisText, NumberStyles.None, CultureInfo.InvariantCulture, out var millis)
            || !decimal.TryParse(priceText, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var price))
        {
            return;
        }

        DateTimeOffset stamp;
        try
        {
            stamp = DateTimeOffset.FromUnixTimeMilliseconds(millis);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        byStamp.TryAdd(stamp, new Sample(stamp, price));
    }

    /// <summary>The feed sends both fields as strings, but numbers are accepted too.</summary>
    private static bool TryReadText(JsonElement element, out string text)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                text = element.GetString() ?? "";
                return true;
            case JsonValueKind.Number:
                text = element.GetRawText();
                return true;
            default:
                text = "";
                return false;
        }
    }
}
