using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// badges drivers earn for reaching goals. goals are checked whenever a driver's badges are read
public static class AchievementEndpoints
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 255;
    public const int MaxThreshold = 10_000_000;
    public const int MaxGoals = 50;

    public static void MapAchievementEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/achievements", Badges);
        // unearned goals, closest first
        app.MapGet("/users/{id:int}/achievements/progress", Progress);
        // id is the sponsor managing its company's goals
        app.MapGet("/users/{id:int}/achievements/goals", Goals);
        app.MapPost("/users/{id:int}/achievements/goals", CreateGoal);
        app.MapPut("/users/{id:int}/achievements/goals/{goalId:int}", EditGoal);
    }

    private static async Task<IResult> Badges(int id, AppDbContext db, Notifications notifications, TimeProvider clock, bool award = true)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return Results.NotFound();
        if (user.UserType == AuthEndpoints.Driver && award)
            await Check(db, notifications, clock, user);
        return Results.Ok(await db.EarnedBadges.AsNoTracking()
            .Where(b => b.UserId == id)
            .Join(db.Achievements, b => b.AchievementId, a => a.Id, (b, a) => new { b, a })
            .OrderByDescending(x => x.b.EarnedAt).ThenByDescending(x => x.b.Id)
            .Select(x => new Badge(x.a.Id, x.a.Name, x.a.Description,
                db.Companies.Where(c => c.Id == x.a.SponsorCompanyId).Select(c => c.Name).FirstOrDefault(), x.b.EarnedAt))
            .ToListAsync());
    }

    private static async Task<IResult> Progress(int id, AppDbContext db, Notifications notifications, TimeProvider clock, bool award = true)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return Results.NotFound();
        if (user.UserType != AuthEndpoints.Driver)
            return Results.Ok(new List<GoalProgress>());
        var open = (await Check(db, notifications, clock, user, award)).Where(r => !r.Earned).ToList();
        var companyIds = open.Select(r => r.Goal.SponsorCompanyId).OfType<int>().Distinct().ToList();
        var names = await db.Companies.AsNoTracking().Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
        return Results.Ok(open
            .OrderByDescending(r => (double)r.Value / r.Goal.Threshold)
            .ThenBy(r => r.Goal.Threshold - r.Value)
            .ThenBy(r => r.Goal.Id)
            .Select(r => new GoalProgress(r.Goal.Id, r.Goal.Name, r.Goal.Description,
                r.Goal.SponsorCompanyId is int c ? names.GetValueOrDefault(c) : null, r.Goal.Metric, r.Value, r.Goal.Threshold))
            .ToList());
    }

    private static async Task<IResult> Goals(int id, AppDbContext db)
    {
        if (await SponsorCompany(db, id) is not int companyId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        return Results.Ok(await db.Achievements.AsNoTracking()
            .Where(a => a.SponsorCompanyId == companyId)
            .OrderByDescending(a => a.Active).ThenBy(a => a.Name).ThenBy(a => a.Id)
            .Select(a => new SponsorGoal(a.Id, a.Name, a.Description, a.Metric, a.Threshold, a.Active,
                db.EarnedBadges.Count(b => b.AchievementId == a.Id)))
            .ToListAsync());
    }

    private static async Task<IResult> CreateGoal(int id, GoalRequest req, AppDbContext db)
    {
        if (Invalid(req) is { } bad)
            return bad;
        if (await SponsorCompany(db, id) is not int companyId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (await db.Achievements.CountAsync(a => a.SponsorCompanyId == companyId) >= MaxGoals)
            return Results.Conflict(new { message = $"You can have up to {MaxGoals} goals. Edit or reuse one instead." });

        var goal = new Achievement { SponsorCompanyId = companyId, CreatedBy = id };
        Fill(goal, req);
        db.Achievements.Add(goal);
        await db.SaveChangesAsync();
        return Results.Ok(new SponsorGoal(goal.Id, goal.Name, goal.Description, goal.Metric, goal.Threshold, goal.Active, 0));
    }

    // also how goals are switched off and back on. badges already earned stay
    private static async Task<IResult> EditGoal(int id, int goalId, GoalRequest req, AppDbContext db)
    {
        if (Invalid(req) is { } bad)
            return bad;
        if (await SponsorCompany(db, id) is not int companyId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        // built-in goals never match a company
        var goal = await db.Achievements.FirstOrDefaultAsync(a => a.Id == goalId && a.SponsorCompanyId == companyId);
        if (goal is null)
            return Results.NotFound(new { message = "That goal isn't one of yours." });
        Fill(goal, req);
        await db.SaveChangesAsync();
        return Results.Ok(new SponsorGoal(goal.Id, goal.Name, goal.Description, goal.Metric, goal.Threshold, goal.Active,
            await db.EarnedBadges.CountAsync(b => b.AchievementId == goal.Id)));
    }

    private static IResult? Invalid(GoalRequest req)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length is 0 or > MaxNameLength)
            return Results.BadRequest(new { message = $"Give the goal a name of up to {MaxNameLength} characters." });
        if ((req.Description?.Trim().Length ?? 0) > MaxDescriptionLength)
            return Results.BadRequest(new { message = $"Keep the description to {MaxDescriptionLength} characters." });
        if (req.Metric is null || !Achievement.Metrics.Contains(req.Metric))
            return Results.BadRequest(new { message = "Pick what the goal counts: points earned, points spent or orders placed." });
        if (req.Threshold is < 1 or > MaxThreshold)
            return Results.BadRequest(new { message = $"The target has to be 1 to {MaxThreshold:N0}." });
        return null;
    }

    private static void Fill(Achievement goal, GoalRequest req)
    {
        goal.Name = req.Name!.Trim();
        goal.Description = req.Description?.Trim() ?? "";
        goal.Metric = req.Metric!;
        goal.Threshold = req.Threshold;
        goal.Active = req.Active;
    }

    private static async Task<int?> SponsorCompany(AppDbContext db, int id) =>
        await db.Users.AnyAsync(u => u.Id == id && u.UserType == AuthEndpoints.Sponsor)
            ? await CompanyMembershipQueries.FirstCompanyId(db, id)
            : null;

    // works out the driver's totals for each goal that applies and awards the ones just reached
    internal static async Task<List<(Achievement Goal, long Value, bool Earned)>> Check(AppDbContext db, Notifications notifications, TimeProvider clock, User driver, bool award = true)
    {
        var companies = await db.AccountsCompanies.Where(m => m.AccountId == driver.Id).Select(m => m.CompanyId).ToListAsync();
        var goals = await db.Achievements.AsNoTracking()
            .Where(a => a.Active && (a.SponsorCompanyId == null || companies.Contains(a.SponsorCompanyId.Value)))
            .OrderBy(a => a.Id)
            .ToListAsync();
        var now = clock.GetUtcNow().UtcDateTime;
        // orders count only after the refund window
        var settled = now.AddHours(-24);
        var history = await db.PointsHistory.AsNoTracking().Where(h => h.UserId == driver.Id)
            .Select(h => new Row(h.CompanyId, h.PointsDelta, h.Carts.Any(), h.RejectionReason != null,
                h.Timestamp == null || h.Timestamp <= settled || h.Carts.Any(c => c.WasOrdered)))
            .ToListAsync();
        var results = goals.Select(g => (Goal: g, Value: Value(g, history), Earned: false)).ToList();
        // a second try covers a request that awarded some of the same badges at the same time
        for (var attempt = 0; ; attempt++)
        {
            var earned = (await db.EarnedBadges.Where(b => b.UserId == driver.Id).Select(b => b.AchievementId).ToListAsync()).ToHashSet();
            results = results.Select(r => r with { Earned = earned.Contains(r.Goal.Id) }).ToList();
            var reached = results.Where(r => !r.Earned && r.Value >= r.Goal.Threshold).Select(r => r.Goal).ToList();
            if (reached.Count == 0 || !award)
                return results;

            var rows = reached.Select(g => new EarnedBadge { UserId = driver.Id, AchievementId = g.Id, EarnedAt = now }).ToList();
            db.EarnedBadges.AddRange(rows);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                foreach (var row in rows)
                    db.Entry(row).State = EntityState.Detached;
                continue;
            }
            foreach (var goal in reached)
                await notifications.BadgeEarned(driver, goal.Name, goal.Description);
            return results.Select(r => r with { Earned = r.Earned || reached.Contains(r.Goal) }).ToList();
        }
    }

    // sponsor goals only count points from that sponsor
    private static long Value(Achievement goal, List<Row> history)
    {
        var rows = goal.SponsorCompanyId is int company ? history.Where(h => h.CompanyId == company) : history;
        var orders = rows.Where(h => h.Order && h.Delta < 0 && !h.Rejected && h.Settled).ToList();
        return goal.Metric switch
        {
            // rejection refunds aren't earned points
            Achievement.PointsEarned => Math.Max(0, rows.Where(h => h.Delta > 0).Sum(h => (long)h.Delta)
                - rows.Where(h => h.Order && h.Rejected && h.Delta < 0).Sum(h => -(long)h.Delta)),
            Achievement.PointsSpent => orders.Sum(h => -(long)h.Delta),
            Achievement.OrdersPlaced => orders.Count,
            _ => 0
        };
    }

    private sealed record Row(int CompanyId, int Delta, bool Order, bool Rejected, bool Settled);
}

// company is null for built-in badges
public record Badge(int Id, string Name, string Description, string? Company, DateTime EarnedAt);

public record GoalProgress(int Id, string Name, string Description, string? Company, string Metric, long Current, int Threshold);

public record GoalRequest(string? Name, string? Description, string? Metric, int Threshold, bool Active = true);

// earned is how many drivers have the badge
public record SponsorGoal(int Id, string Name, string Description, string Metric, int Threshold, bool Active, int Earned);
