// notification kinds a user can switch off or have emailed
public sealed record NotificationCategory(string Key, string Label, bool Emailed, params string[] Roles)
{
    public static readonly NotificationCategory Points = new("points", "Points added or deducted", false, AuthEndpoints.Driver);
    public static readonly NotificationCategory Applications = new("applications", "Application updates", true, AuthEndpoints.Driver, AuthEndpoints.Sponsor);
    public static readonly NotificationCategory Purchases = new("purchases", "Driver purchases", false, AuthEndpoints.Sponsor);
    public static readonly NotificationCategory NegativeBalances = new("negative-balances", "Drivers with a negative balance", true, AuthEndpoints.Sponsor);
    public static readonly NotificationCategory NewAccounts = new("new-accounts", "New accounts", false, AuthEndpoints.Admin);
    public static readonly NotificationCategory SecurityAlerts = new("security-alerts", "Lockouts and password reset requests", true, AuthEndpoints.Admin);

    public static readonly IReadOnlyList<NotificationCategory> All = [Points, Applications, Purchases, NegativeBalances, NewAccounts, SecurityAlerts];

    public static IEnumerable<NotificationCategory> For(string userType) => All.Where(c => c.Roles.Contains(userType));
}
