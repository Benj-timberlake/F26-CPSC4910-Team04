using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// point changes with reasons and each driver's point history
public static class PointsEndpoints
{
    public const int MaxChange = 1_000_000;
    public const int MaxReasonLength = 500;

    public static void MapPointsEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/points", Summary);
        // id is the sponsor or admin who is looking
        app.MapGet("/users/{id:int}/points/drivers", Drivers);
        app.MapPost("/users/{id:int}/points/drivers/{driverId:int}", Change);
    }

    private static async Task<IResult> Summary(int id, AppDbContext db, TimeProvider clock)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return Results.NotFound();

        var rows = await db.PointsHistory.AsNoTracking()
            .Where(h => h.UserId == id)
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
            user.Points,
            rows.Where(r => r.PointsDelta > 0 && r.Timestamp >= monthStart).Sum(r => (long)r.PointsDelta),
            rows.Where(r => r.PointsDelta > 0).Sum(r => (long)r.PointsDelta),
            rows.Where(r => r.PointsDelta < 0 && r.Order).Sum(r => -(long)r.PointsDelta),
            rows.Where(r => r.PointsDelta < 0 && !r.Order).Sum(r => -(long)r.PointsDelta),
            entries));
    }

    // sort is name, points or spent
    private static async Task<IResult> Drivers(int id, string? search, string? sort, AppDbContext db)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (actor is null)
            return Results.NotFound();
        if (!CanManage(actor))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var drivers = db.Users.AsNoTracking().Where(u => u.UserType == AuthEndpoints.Driver && u.CompanyId != null);
        if (actor.UserType == AuthEndpoints.Sponsor)
            drivers = drivers.Where(u => u.CompanyId == actor.CompanyId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            drivers = drivers.Where(u => u.Username.ToLower().Contains(term) || (u.FirstName + " " + u.LastName).ToLower().Contains(term));
        }
        var rows = drivers.Select(u => new
        {
            User = u,
            Spent = db.PointsHistory.Where(h => h.UserId == u.Id && h.PointsDelta < 0 && h.Carts.Any()).Sum(h => -(long)h.PointsDelta)
        });
        rows = sort switch
        {
            "points" => rows.OrderByDescending(r => r.User.Points).ThenBy(r => r.User.Username),
            "spent" => rows.OrderByDescending(r => r.Spent).ThenBy(r => r.User.Username),
            _ => rows.OrderBy(r => r.User.LastName).ThenBy(r => r.User.FirstName).ThenBy(r => r.User.Username)
        };
        return Results.Ok(await rows
            .Select(r => new PointsDriver(r.User.Id, r.User.Username, r.User.FirstName, r.User.LastName, r.User.Email, r.User.Points, r.Spent))
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
        // same balance lock as checkout
        await db.Users.Where(u => u.Id == driverId)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.Points, u => u.Points));
        var driver = await db.Users.FirstOrDefaultAsync(u => u.Id == driverId && u.UserType == AuthEndpoints.Driver);
        if (driver?.CompanyId is null)
            return Results.NotFound(new { message = "That driver isn't with a sponsor." });
        if (!CanManage(actor) || (actor.UserType == AuthEndpoints.Sponsor && actor.CompanyId != driver.CompanyId))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var balance = (long)driver.Points + req.Points;
        if (balance is > int.MaxValue or < int.MinValue)
            return Results.BadRequest(new { message = "That would put the balance out of range." });

        driver.Points = (int)balance;
        var history = new PointsHistory { UserId = driverId, PointsDelta = req.Points, Timestamp = clock.GetUtcNow().UtcDateTime };
        db.PointsHistory.Add(history);
        db.AuditHistories.Add(new AuditHistory { UserId = actor.Id, PointsHistory = history, Resoning = reason });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await notifications.PointsChanged(driver, req.Points, $"Reason: {reason.TrimEnd('.')}.");
        var spent = await db.PointsHistory.Where(h => h.UserId == driverId && h.PointsDelta < 0 && h.Carts.Any()).SumAsync(h => -(long)h.PointsDelta);
        return Results.Ok(new PointsDriver(driver.Id, driver.Username, driver.FirstName, driver.LastName, driver.Email, driver.Points, spent));
    }

    // admins or sponsors with a company
    internal static bool CanManage(User actor) =>
        actor.UserType == AuthEndpoints.Admin || (actor.UserType == AuthEndpoints.Sponsor && actor.CompanyId is not null);
}

public record ChangePointsRequest(int Points, string? Reason);

public record PointsDriver(int Id, string Username, string FirstName, string LastName, string Email, int Points, long Spent);

// by is null for orders
public record PointsEntry(DateTime? At, int Points, string Reason, string? By);

public record PointsSummary(int Balance, long EarnedThisMonth, long Earned, long Spent, long Deducted, List<PointsEntry> History);
