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
                await AwardDriverPoints(db, clock.GetUtcNow(), stoppingToken);
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

    public static DateTime CurrentMonth(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, BudgetTimeZone);
        return new DateTime(local.Year, local.Month, 1);
    }

    public static async Task AwardDriverPoints(AppDbContext db, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var month = CurrentMonth(now);
        var ids = await db.Companies.AsNoTracking()
            .Where(c => c.DriverPointsMonth != null && c.DriverPointsMonth < month)
            .Select(c => c.Id).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            // Claim this month in the same transaction as the awards, including across app instances.
            var claimed = await db.Companies.Where(c => c.Id == id && c.DriverPointsMonth < month)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.DriverPointsMonth, month), cancellationToken);
            if (claimed == 0) continue;
            var company = await db.Companies.AsNoTracking().SingleAsync(c => c.Id == id, cancellationToken);
            if (company.MonthlyDriverPoints > 0)
            {
                var drivers = await db.Users.Where(u => u.UserType == "driver" && u.Status == User.Active &&
                    db.AccountsCompanies.Any(m => m.CompanyId == id && m.AccountId == u.Id))
                    .Select(u => u.Id).ToListAsync(cancellationToken);
                foreach (var driverId in drivers)
                {
                    var updated = await db.Users.Where(u => u.Id == driverId && u.Points <= int.MaxValue - company.MonthlyDriverPoints)
                        .ExecuteUpdateAsync(s => s.SetProperty(u => u.Points, u => u.Points + company.MonthlyDriverPoints), cancellationToken);
                    if (updated == 0) throw new InvalidOperationException("Monthly points would exceed the driver balance limit.");
                    var history = new PointsHistory { UserId = driverId, CompanyId = id, PointsDelta = company.MonthlyDriverPoints, Timestamp = now.UtcDateTime };
                    db.PointsHistory.Add(history);
                    if (company.MonthlyPointsSponsorId is int sponsorId)
                        db.AuditHistories.Add(new AuditHistory { UserId = sponsorId, PointsHistory = history, Resoning = "Monthly driver points" });
                }
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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
            var sponsorIds = CompanyMembershipQueries.FirstPerUser(db)
                .Where(membership => membership.CompanyId == company.Id).Select(membership => membership.AccountId);
            // One atomic statement prevents duplicate resets across retries or multiple app instances.
            await db.Users
                .Where(u => u.UserType == "sponsor" && sponsorIds.Contains(u.Id) &&
                    (u.BudgetResetMonth == null || u.BudgetResetMonth < month))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(u => u.Points, points)
                    .SetProperty(u => u.BudgetResetMonth, month), cancellationToken);
        }
    }
}
