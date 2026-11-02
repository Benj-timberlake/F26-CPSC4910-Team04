using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// point changes with reasons and each driver's point history
public static class PointsEndpoints
{
    public const int MaxChange = 1_000_000;
    public const int MaxReasonLength = 500;
    public const int MaxBulkDrivers = 200;
    public const int MaxSavedReasons = 50;

    public static void MapPointsEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/points", Summary);
        app.MapGet("/users/{id:int}/points/monthly", Monthly);
        app.MapPost("/users/{id:int}/points/monthly", SaveMonthly);
        // id is the sponsor or admin who is looking
        app.MapGet("/users/{id:int}/points/drivers", Drivers);
        app.MapPost("/users/{id:int}/points/drivers/{driverId:int}", Change);
        app.MapPost("/users/{id:int}/points/drivers/{driverId:int}/remove", Remove);
        app.MapPost("/users/{id:int}/points/bulk", Bulk);
        app.MapGet("/users/{id:int}/points/reasons", Reasons);
        app.MapPost("/users/{id:int}/points/reasons", SaveReason);
        app.MapDelete("/users/{id:int}/points/reasons/{reasonId:int}", ForgetReason);
        // period is all, month or week
        app.MapGet("/users/{id:int}/leaderboard", Leaderboard);
    }

    private static async Task<IResult> Monthly(int id, AppDbContext db)
    {
        if (await SponsorCompany(db, id) is not int companyId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        return Results.Ok(new MonthlyPointsRequest(await db.Companies.Where(c => c.Id == companyId)
            .Select(c => c.MonthlyDriverPoints).SingleAsync()));
    }

    private static async Task<IResult> SaveMonthly(int id, MonthlyPointsRequest request, AppDbContext db, TimeProvider clock)
    {
        if (request.Points < 0 || request.Points > MaxChange)
            return Results.BadRequest(new { message = $"Enter 0 to {MaxChange:N0} whole points." });
        if (await SponsorCompany(db, id) is not int companyId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        // Settings take effect next month; saving never triggers an immediate award.
        var month = MonthlySponsorBudgetService.CurrentMonth(clock.GetUtcNow());
        await db.Companies.Where(c => c.Id == companyId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.MonthlyDriverPoints, request.Points)
            .SetProperty(c => c.MonthlyPointsSponsorId, id)
            .SetProperty(c => c.DriverPointsMonth, month));
        return Results.Ok(request);
    }

    private static async Task<IResult> Summary(int id, AppDbContext db, TimeProvider clock)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return Results.NotFound();

        var companyId = await CompanyMembershipQueries.FirstCompanyId(db, id);
        var rows = await db.PointsHistory.AsNoTracking()
            .Where(h => h.UserId == id && companyId != null && h.CompanyId == companyId)
            .OrderByDescending(h => h.Timestamp).ThenByDescending(h => h.Id)
            .Select(h => new
            {
                h.Timestamp,
                h.PointsDelta,
                Order = h.Carts.Any(),
                Audit = h.AuditHistories.Select(a => new { a.Resoning, a.User.Username }).FirstOrDefault()
            })
            .ToListAsync();
        var entries = rows.Select(r => new PointsEntry(r.Timestamp, r.PointsDelta,
            r.Order ? "Order" : r.Audit?.Resoning ?? "",
            r.Order ? null : r.Audit?.Username)).ToList();

        var now = clock.GetUtcNow().UtcDateTime;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return Results.Ok(new PointsSummary(
            rows.Sum(r => (long)r.PointsDelta),
            rows.Where(r => r.PointsDelta > 0 && r.Timestamp >= monthStart).Sum(r => (long)r.PointsDelta),
            rows.Where(r => r.PointsDelta > 0).Sum(r => (long)r.PointsDelta),
            rows.Where(r => r.PointsDelta < 0 && r.Order).Sum(r => -(long)r.PointsDelta),
            rows.Where(r => r.PointsDelta < 0 && !r.Order).Sum(r => -(long)r.PointsDelta),
            entries));
    }

    // sort is name, points, spent or earned
    private static async Task<IResult> Drivers(int id, string? search, string? sort, DateOnly? from, DateOnly? to, AppDbContext db)
    {
        if (from > to)
            return Results.BadRequest(new { message = "The start date has to be on or before the end date." });
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (actor is null)
            return Results.NotFound();
        if (!await CanManage(db, actor))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        int? companyId = null;
        var drivers = db.Users.AsNoTracking().Where(u => u.UserType == AuthEndpoints.Driver &&
            CompanyMembershipQueries.FirstPerUser(db).Any(membership => membership.AccountId == u.Id));
        if (actor.UserType == AuthEndpoints.Sponsor)
        {
            companyId = await CompanyMembershipQueries.FirstCompanyId(db, actor.Id);
            drivers = drivers.Where(u => db.AccountsCompanies
                .Any(membership => membership.AccountId == u.Id && membership.CompanyId == companyId));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            drivers = drivers.Where(u => u.Username.ToLower().Contains(term) || (u.FirstName + " " + u.LastName).ToLower().Contains(term));
        }
        DateTime? start = from?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        // to is inclusive
        DateTime? end = to?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = drivers.Select(u => new
        {
            User = u,
            Points = companyId == null ? (long)u.Points : db.PointsHistory.Where(h => h.UserId == u.Id && h.CompanyId == companyId).Sum(h => (long)h.PointsDelta),
            Spent = db.PointsHistory.Where(h => h.UserId == u.Id && (companyId == null || h.CompanyId == companyId) && h.PointsDelta < 0 && h.Carts.Any()).Sum(h => -(long)h.PointsDelta),
            Earned = db.PointsHistory.Where(h => h.UserId == u.Id && (companyId == null || h.CompanyId == companyId) && h.PointsDelta > 0 &&
                (start == null || h.Timestamp >= start) && (end == null || h.Timestamp < end)).Sum(h => (long)h.PointsDelta)
        });
        rows = sort switch
        {
            "points" => rows.OrderByDescending(r => r.Points).ThenBy(r => r.User.Username),
            "spent" => rows.OrderByDescending(r => r.Spent).ThenBy(r => r.User.Username),
            "earned" => rows.OrderByDescending(r => r.Earned).ThenBy(r => r.User.Username),
            _ => rows.OrderBy(r => r.User.LastName).ThenBy(r => r.User.FirstName).ThenBy(r => r.User.Username)
        };
        return Results.Ok(await rows
            .Select(r => new PointsDriver(r.User.Id, r.User.Username, r.User.FirstName, r.User.LastName, r.User.Email, r.Points, r.Spent,
                from == null && to == null ? null : r.Earned))
            .ToListAsync());
    }

    private static async Task<IResult> Change(int id, int driverId, ChangePointsRequest req, AppDbContext db, Notifications notifications, TimeProvider clock)
    {
        var reason = req.Reason?.Trim() ?? "";
        if (req.Points == 0 || Math.Abs((long)req.Points) > MaxChange)
            return Results.BadRequest(new { message = $"Change points by 1 to {MaxChange:N0} either way." });
        if (reason.Length is 0 or > MaxReasonLength)
            return Results.BadRequest(new { message = $"Give a reason of up to {MaxReasonLength} characters." });

        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (actor is null)
            return Results.NotFound();

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var (failure, driver, historyCompanyId) = await Apply(db, actor, driverId, req.Points, reason, req.CompanyId, clock);
        if (failure is not null || driver is null)
            return failure!;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        var spent = await db.PointsHistory.Where(h => h.UserId == driverId && h.CompanyId == historyCompanyId && h.PointsDelta < 0 && h.Carts.Any()).SumAsync(h => -(long)h.PointsDelta);
        var displayedPoints = await db.PointsHistory.Where(h => h.UserId == driverId && h.CompanyId == historyCompanyId).SumAsync(h => (long)h.PointsDelta);
        await notifications.PointsChanged(driver, req.Points, $"Reason: {reason.TrimEnd('.') }.", historyCompanyId, actor.Username);
        return Results.Ok(new PointsDriver(driver.Id, driver.Username, driver.FirstName, driver.LastName, driver.Email, displayedPoints, spent));
    }

    // all or nothing, so a bad driver in the list changes no one
    private static async Task<IResult> Bulk(int id, BulkPointsRequest req, AppDbContext db, Notifications notifications, TimeProvider clock)
    {
        var reason = req.Reason?.Trim() ?? "";
        var driverIds = req.DriverIds?.Distinct().ToList() ?? [];
        if (driverIds.Count is 0 or > MaxBulkDrivers)
            return Results.BadRequest(new { message = $"Pick 1 to {MaxBulkDrivers} drivers." });
        if (req.Points == 0 || Math.Abs((long)req.Points) > MaxChange)
            return Results.BadRequest(new { message = $"Change points by 1 to {MaxChange:N0} either way." });
        if (reason.Length is 0 or > MaxReasonLength)
            return Results.BadRequest(new { message = $"Give a reason of up to {MaxReasonLength} characters." });

        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (actor is null)
            return Results.NotFound();

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var changed = new List<(User Driver, int CompanyId)>();
        foreach (var driverId in driverIds)
        {
            var (failure, driver, companyId) = await Apply(db, actor, driverId, req.Points, reason, req.CompanyId, clock);
            if (failure is not null || driver is null)
                return failure!;
            changed.Add((driver, companyId));
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        foreach (var (driver, companyId) in changed)
            await notifications.PointsChanged(driver, req.Points, $"Reason: {reason.TrimEnd('.')}.", companyId, actor.Username);
        return Results.Ok(new { message = $"{(req.Points > 0 ? "Gave" : "Took")} {Math.Abs(req.Points):N0} points {(req.Points > 0 ? "to" : "from")} {changed.Count} {(changed.Count == 1 ? "driver" : "drivers")}." });
    }

    // admins have no company, so they get none
    private static async Task<IResult> Reasons(int id, AppDbContext db)
    {
        var companyId = await SponsorCompany(db, id);
        return Results.Ok(companyId is null ? [] : await db.SavedReasons.AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .OrderBy(r => r.Reason)
            .Select(r => new SavedReasonEntry(r.Id, r.Reason))
            .ToListAsync());
    }

    private static async Task<IResult> SaveReason(int id, SaveReasonRequest req, AppDbContext db)
    {
        var reason = req.Reason?.Trim() ?? "";
        if (reason.Length is 0 or > MaxReasonLength)
            return Results.BadRequest(new { message = $"Give a reason of up to {MaxReasonLength} characters." });
        if (await SponsorCompany(db, id) is not int companyId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (await db.SavedReasons.FirstOrDefaultAsync(r => r.CompanyId == companyId && r.Reason == reason) is { } existing)
            return Results.Ok(new SavedReasonEntry(existing.Id, existing.Reason));
        if (await db.SavedReasons.CountAsync(r => r.CompanyId == companyId) >= MaxSavedReasons)
            return Results.Conflict(new { message = $"You can save up to {MaxSavedReasons} reasons. Remove one first." });

        var saved = new SavedReason { CompanyId = companyId, Reason = reason };
        db.SavedReasons.Add(saved);
        await db.SaveChangesAsync();
        return Results.Ok(new SavedReasonEntry(saved.Id, saved.Reason));
    }

    private static async Task<IResult> ForgetReason(int id, int reasonId, AppDbContext db)
    {
        var companyId = await SponsorCompany(db, id);
        var removed = await db.SavedReasons.Where(r => r.Id == reasonId && r.CompanyId == companyId).ExecuteDeleteAsync();
        return removed == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<int?> SponsorCompany(AppDbContext db, int id) =>
        await db.Users.AnyAsync(u => u.Id == id && u.UserType == AuthEndpoints.Sponsor)
            ? await CompanyMembershipQueries.FirstCompanyId(db, id)
            : null;

    // checks and stages one driver's change, the caller saves inside a serializable transaction
    private static async Task<(IResult? Failure, User? Driver, int CompanyId)> Apply(AppDbContext db, User actor, int driverId, int points, string reason, int? companyId, TimeProvider clock)
    {
        // same balance lock as checkout
        await db.Users.Where(u => u.Id == driverId)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.Points, u => u.Points));
        var driver = await db.Users.FirstOrDefaultAsync(u => u.Id == driverId && u.UserType == AuthEndpoints.Driver);
        List<int> driverCompanyIds = driver is null ? [] : await db.AccountsCompanies.Where(membership => membership.AccountId == driverId)
            .Select(membership => membership.CompanyId).ToListAsync();
        var actorCompanyId = actor.UserType == AuthEndpoints.Sponsor ? await CompanyMembershipQueries.FirstCompanyId(db, actor.Id) : null;
        if (driver is null || driverCompanyIds.Count == 0)
            return (Results.NotFound(new { message = "That driver isn't with a sponsor." }), null, 0);
        var sponsorHasDriver = actorCompanyId is int sponsorCompany &&
            driverCompanyIds.Contains(sponsorCompany);
        if (!await CanManage(db, actor) || (actor.UserType == AuthEndpoints.Sponsor && !sponsorHasDriver))
            return (Results.StatusCode(StatusCodes.Status403Forbidden), null, 0);
        int historyCompanyId;
        if (actor.UserType == AuthEndpoints.Sponsor)
        {
            historyCompanyId = actorCompanyId!.Value;
        }
        else if (companyId is int selectedCompanyId)
        {
            if (!driverCompanyIds.Contains(selectedCompanyId))
                return (Results.BadRequest(new { message = "Choose a company this driver belongs to." }), null, 0);
            historyCompanyId = selectedCompanyId;
        }
        else if (driverCompanyIds.Count == 1)
        {
            historyCompanyId = driverCompanyIds[0];
        }
        else
        {
            return (Results.BadRequest(new { message = "Choose which company this points change is for." }), null, 0);
        }
        var balance = (long)driver.Points + points;
        if (balance is > int.MaxValue or < int.MinValue)
            return (Results.BadRequest(new { message = "That would put the balance out of range." }), null, 0);

        driver.Points = (int)balance;
        var history = new PointsHistory { UserId = driverId, CompanyId = historyCompanyId, PointsDelta = points, Timestamp = clock.GetUtcNow().UtcDateTime };
        db.PointsHistory.Add(history);
        db.AuditHistories.Add(new AuditHistory { UserId = actor.Id, PointsHistory = history, Resoning = reason });
        return (null, driver, historyCompanyId);
    }

    // cancels the accepted application to allow reapplying
    private static async Task<IResult> Remove(int id, int driverId, RemoveDriverRequest req, AppDbContext db, Notifications notifications, TimeProvider clock)
    {
        var reason = req.Reason?.Trim() ?? "";
        if (reason.Length is 0 or > MaxReasonLength)
            return Results.BadRequest(new { message = $"Give a reason of up to {MaxReasonLength} characters." });
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        var driver = await db.Users.FirstOrDefaultAsync(u => u.Id == driverId && u.UserType == AuthEndpoints.Driver);
        var driverCompanyId = driver is null ? null : await CompanyMembershipQueries.FirstCompanyId(db, driverId);
        var actorCompanyId = actor?.UserType == AuthEndpoints.Sponsor ? await CompanyMembershipQueries.FirstCompanyId(db, id) : null;
        if (actor is null || driver is null || driverCompanyId is null)
            return Results.NotFound(new { message = "That driver isn't with a sponsor." });
        var company = actor.UserType == AuthEndpoints.Sponsor ? actorCompanyId : driverCompanyId;
        if (!await CanManage(db, actor) || company is null)
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var companyToRemove = company.Value;
        var belongsToCompany = await db.AccountsCompanies.AnyAsync(membership => membership.AccountId == driverId && membership.CompanyId == companyToRemove);
        if (!belongsToCompany)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var wasCurrent = await db.AccountsCompanies.AnyAsync(membership => membership.AccountId == driverId && membership.CompanyId == companyToRemove && membership.CurrentCompanyViewed);

        await db.AccountsCompanies.Where(membership => membership.AccountId == driverId && membership.CompanyId == companyToRemove)
            .ExecuteDeleteAsync();
        if (wasCurrent)
        {
            var nextCompany = await db.AccountsCompanies.Where(membership => membership.AccountId == driverId)
                .OrderBy(membership => membership.CompanyId).Select(membership => (int?)membership.CompanyId).FirstOrDefaultAsync();
            if (nextCompany is int next)
                await db.AccountsCompanies.Where(membership => membership.AccountId == driverId && membership.CompanyId == next)
                    .ExecuteUpdateAsync(update => update.SetProperty(membership => membership.CurrentCompanyViewed, true));
        }
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var application in await db.Applications.Where(a => a.ApplicantId == driverId && a.CompanyId == companyToRemove && a.Status == "approved").ToListAsync())
        {
            application.Status = "canceled";
            application.ReviewerId = actor.Id;
            application.ReviewerReasoning = reason;
            application.ResponseTimestamp = now;
        }
        await db.SaveChangesAsync();
        await notifications.DriverRemoved(driver, companyToRemove, reason);
        return Results.NoContent();
    }

    private static async Task<IResult> Leaderboard(int id, string? period, string? category, int? companyId, AppDbContext db, TimeProvider clock)
    {
        var viewer = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (viewer is null)
            return Results.NotFound();
        period = string.IsNullOrEmpty(period) ? "all" : period;
        var now = clock.GetUtcNow().UtcDateTime;
        DateTime? start = period switch
        {
            "month" => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            "week" => now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7)),
            _ => null
        };
        if (period is not ("all" or "month" or "week"))
            return Results.BadRequest(new { message = "Period must be all, month or week." });
        category = string.IsNullOrEmpty(category) ? "points" : category;
        if (category is not ("points" or "spent" or "orders"))
            return Results.BadRequest(new { message = "Category must be points, spent or orders." });
        var company = viewer.UserType == AuthEndpoints.Admin ? companyId : await CompanyMembershipQueries.FirstCompanyId(db, id);
        if (company is null)
            return Results.BadRequest(new { message = "Pick a sponsor to see its leaderboard." });

        var drivers = db.Users.AsNoTracking()
            .Where(u => u.UserType == AuthEndpoints.Driver && u.Status == User.Active &&
                db.AccountsCompanies.Any(membership => membership.AccountId == u.Id && membership.CompanyId == company));
        var history = db.PointsHistory.Where(h => h.CompanyId == company && (start == null || h.Timestamp >= start));
        var totals = await (category switch
        {
            "spent" => drivers.Select(u => new Total(u.Id, u.Username, u.FirstName, u.LastName,
                history.Where(h => h.UserId == u.Id && h.PointsDelta < 0 && h.Carts.Any()).Sum(h => -(long)h.PointsDelta))),
            "orders" => drivers.Select(u => new Total(u.Id, u.Username, u.FirstName, u.LastName,
                history.Count(h => h.UserId == u.Id && h.Carts.Any()))),
            _ => drivers.Select(u => new Total(u.Id, u.Username, u.FirstName, u.LastName, start == null
                ? history.Where(h => h.UserId == u.Id).Sum(h => (long)h.PointsDelta)
                : history.Where(h => h.UserId == u.Id && h.PointsDelta > 0).Sum(h => (long)h.PointsDelta)))
        }).ToListAsync();

        var ordered = totals.OrderByDescending(t => t.Points).ThenBy(t => t.Username).ToList();
        // ties share a rank
        var rows = ordered.Select(t => new LeaderboardRow(ordered.FindIndex(o => o.Points == t.Points) + 1, t.Id, t.Username, t.FirstName, t.LastName, t.Points)).ToList();
        return Results.Ok(new Leaderboard(period, rows.FirstOrDefault(r => r.Id == id)?.Rank, rows));
    }

    private sealed record Total(int Id, string Username, string FirstName, string LastName, long Points);

    // admins or sponsors with a company
    internal static async Task<bool> CanManage(AppDbContext db, User actor) =>
        actor.UserType == AuthEndpoints.Admin || (actor.UserType == AuthEndpoints.Sponsor &&
            await CompanyMembershipQueries.FirstCompanyId(db, actor.Id) is not null);
}

public record LeaderboardRow(int Rank, int Id, string Username, string FirstName, string LastName, long Points);

// your rank is null for viewers off the board
public record Leaderboard(string Period, int? YourRank, List<LeaderboardRow> Rows);

public record RemoveDriverRequest(string? Reason);
public record MonthlyPointsRequest(int Points);

public record SaveReasonRequest(string? Reason);

public record SavedReasonEntry(int Id, string Reason);

public record BulkPointsRequest(List<int>? DriverIds, int Points, string? Reason, int? CompanyId = null);

public record ChangePointsRequest(int Points, string? Reason, int? CompanyId = null);

// earned is null without a date range
public record PointsDriver(int Id, string Username, string FirstName, string LastName, string Email, long Points, long Spent, long? Earned = null);

// by is null for orders
public record PointsEntry(DateTime? At, int Points, string Reason, string? By);

public record PointsSummary(long Balance, long EarnedThisMonth, long Earned, long Spent, long Deducted, List<PointsEntry> History);
