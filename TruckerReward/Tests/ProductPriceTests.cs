using FrontEnd.Shared;
using System.Globalization;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ProductPriceTests
{
    [Theory]
    [InlineData("29.5", "USD", "29.50 USD")]
    [InlineData("0", "USD", "0.00 USD")]
    [InlineData("1234.56", "EUR", "1,234.56 EUR")]
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
