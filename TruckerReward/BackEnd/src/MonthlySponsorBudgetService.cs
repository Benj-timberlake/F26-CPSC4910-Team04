using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public sealed class MonthlySponsorBudgetService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<MonthlySponsorBudgetService> logger) : BackgroundService
{
    private static readonly TimeZoneInfo BudgetTimeZone = FindBudgetTimeZone();

    private static TimeZoneInfo FindBudgetTimeZone()
    {
        // IANA-to-Windows ID conversion is unavailable in invariant globalization mode.
        // Try both native IDs so Windows and Linux keep Eastern daylight-saving rules.
        if (TimeZoneInfo.TryFindSystemTimeZoneById("America/New_York", out var zone) ||
            TimeZoneInfo.TryFindSystemTimeZoneById("Eastern Standard Time", out zone))
            return zone;

        throw new TimeZoneNotFoundException("Eastern time is unavailable. Install the operating system's time zone data.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ResetDueBalances(db, clock.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Monthly sponsor budget reset failed; will retry in one minute.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public static async Task ResetDueBalances(
        AppDbContext db, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var local = TimeZoneInfo.ConvertTime(now, BudgetTimeZone);
        var month = new DateTime(local.Year, local.Month, 1);
        var companies = await db.Companies.AsNoTracking()
            .Select(c => new { c.Id, c.MonthlyBudget })
            .ToListAsync(cancellationToken);

        foreach (var company in companies)
        {
            // User balances store whole points. Reject invalid budgets rather than silently rounding.
            if (company.MonthlyBudget < 0 || company.MonthlyBudget > int.MaxValue ||
                decimal.Truncate(company.MonthlyBudget) != company.MonthlyBudget)
                throw new InvalidOperationException($"Company {company.Id} monthly budget must be a nonnegative whole number of points within the user balance limit.");

            var points = (int)company.MonthlyBudget;
            // One atomic statement prevents duplicate resets across retries or multiple app instances.
            await db.Users
                .Where(u => u.UserType == "sponsor" && u.CompanyId == company.Id &&
                    (u.BudgetResetMonth == null || u.BudgetResetMonth < month))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(u => u.Points, points)
                    .SetProperty(u => u.BudgetResetMonth, month), cancellationToken);
        }
    }
}
