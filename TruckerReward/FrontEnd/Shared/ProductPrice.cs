using System.Globalization;
using System.Text.Json.Serialization;

namespace FrontEnd.Shared;

public sealed class ProductPrice
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    public bool TryGetDollars(out decimal dollars) =>
        decimal.TryParse(Value, NumberStyles.Number, CultureInfo.InvariantCulture, out dollars)
        && string.Equals(Currency, "USD", StringComparison.OrdinalIgnoreCase)
        && dollars >= 0 && dollars <= 99999999.99m
        && decimal.Round(dollars, 2) == dollars;

    public string Display => TryGetDollars(out var dollars)
        ? RewardPoints.Format(RewardPoints.FromDollars(dollars))
        : "Price unavailable";
}
