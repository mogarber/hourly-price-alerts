using System.Text.Json;
using System.Text.Json.Serialization;

namespace HourlyPriceAlerts.Core;

public static class ApiJson
{
    /// <summary>camelCase properties and camelCase string enums ("hourAverage"); numeric enum values are rejected.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };
}
