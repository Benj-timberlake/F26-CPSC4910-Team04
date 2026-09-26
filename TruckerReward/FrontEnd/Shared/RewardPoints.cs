using System.Globalization;

namespace FrontEnd.Shared;

public static class RewardPoints
{
    // Product and cart API prices are USD; account balances are already points.
    public static decimal FromDollars(decimal dollars) => dollars * 100m;

    public static string Format(decimal points) =>
        $"{points.ToString("N0", CultureInfo.InvariantCulture)} {(points == 1 ? "point" : "points")}";
}
