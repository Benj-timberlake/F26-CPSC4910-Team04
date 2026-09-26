using FrontEnd.Shared;
using System.Globalization;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ProductPriceTests
{
    [Theory]
    [InlineData("29.5", "USD", "2,950 points")]
    [InlineData("0", "USD", "0 points")]
    [InlineData("0.01", "USD", "1 point")]
    [InlineData("1234.56", "USD", "123,456 points")]
    [InlineData("-1", "USD", "Price unavailable")]
    [InlineData("0.001", "USD", "Price unavailable")]
    [InlineData("79228162514264337593543950335", "USD", "Price unavailable")]
    [InlineData("1234.56", "EUR", "Price unavailable")]
    [InlineData(null, "USD", "Price unavailable")]
    [InlineData("invalid", "USD", "Price unavailable")]
    [InlineData("10", null, "Price unavailable")]
    [InlineData("10", " ", "Price unavailable")]
    public void PriceFormattingHandlesCurrencyAndMissingValues(string? value, string? currency, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(expected, new ProductPrice { Value = value, Currency = currency }.Display);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
