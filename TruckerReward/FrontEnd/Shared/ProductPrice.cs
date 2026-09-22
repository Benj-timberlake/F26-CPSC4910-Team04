using System.Globalization;
using System.Text.Json.Serialization;

namespace FrontEnd.Shared;

public sealed class ProductPrice
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    public string Display => decimal.TryParse(Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
        && !string.IsNullOrWhiteSpace(Currency)
        ? $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {Currency}"
        : "Price unavailable";
}
