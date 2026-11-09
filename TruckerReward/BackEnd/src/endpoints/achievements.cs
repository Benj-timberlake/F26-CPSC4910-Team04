using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// badges drivers earn for reaching goals. goals are checked whenever a driver's badges are read
public static class AchievementEndpoints
{
    public static void MapAchievementEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/achievements", Badges);
        // unearned goals, closest first
        app.MapGet("/users/{id:int}/achievements/progress", Progress);
    }

    private static async Task<IResult> Badges(int id, AppDbContext db, Notifications notifications, TimeProvider clock)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return Results.NotFound();
        if (user.UserType == AuthEndpoints.Driver)
            await Check(db, notifications, clock, user);
        return Results.Ok(await db.EarnedBadges.AsNoTracking()
            .Where(b => b.UserId == id)
            .Join(db.Achievements, b => b.AchievementId, a => a.Id, (b, a) => new { b, a })
            .OrderByDescending(x => x.b.EarnedAt).ThenByDescending(x => x.b.Id)
            .Select(x => new Badge(x.a.Id, x.a.Name, x.a.Description,
                db.Companies.Where(c => c.Id == x.a.SponsorCompanyId).Select(c => c.Name).FirstOrDefault(), x.b.EarnedAt))
            .ToListAsync());
    }

    private static async Task<IResult> Progress(int id, AppDbContext db, Notifications notifications, TimeProvider clock)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return Results.NotFound();
        if (user.UserType != AuthEndpoints.Driver)
            return Results.Ok(new List<GoalProgress>());
        var open = (await Check(db, notifications, clock, user)).Where(r => !r.Earned).ToList();
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

    // works out the driver's totals for each goal that applies and awards the ones just reached
    internal static async Task<List<(Achievement Goal, long Value, bool Earned)>> Check(AppDbContext db, Notifications notifications, TimeProvider clock, User driver)
    {
        var companies = await db.AccountsCompanies.Where(m => m.AccountId == driver.Id).Select(m => m.CompanyId).ToListAsync();
        var goals = await db.Achievements.AsNoTracking()
            .Where(a => a.Active && (a.SponsorCompanyId == null || companies.Contains(a.SponsorCompanyId.Value)))
            .OrderBy(a => a.Id)
            .ToListAsync();
        var history = await db.PointsHistory.AsNoTracking().Where(h => h.UserId == driver.Id)
            .Select(h => new Row(h.CompanyId, h.PointsDelta, h.Carts.Any()))
            .ToListAsync();
        var earned = (await db.EarnedBadges.Where(b => b.UserId == driver.Id).Select(b => b.AchievementId).ToListAsync()).ToHashSet();

        var results = goals.Select(g => (Goal: g, Value: Value(g, history), Earned: earned.Contains(g.Id))).ToList();
        var reached = results.Where(r => !r.Earned && r.Value >= r.Goal.Threshold).Select(r => r.Goal).ToList();
        if (reached.Count == 0)
            return results;

        var now = clock.GetUtcNow().UtcDateTime;
        var rows = reached.Select(g => new EarnedBadge { UserId = driver.Id, AchievementId = g.Id, EarnedAt = now }).ToList();
        db.EarnedBadges.AddRange(rows);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // another request awarded them first and already sent the notification
            foreach (var row in rows)
                db.Entry(row).State = EntityState.Detached;
            return results;
        }
        foreach (var goal in reached)
            await notifications.BadgeEarned(driver, goal.Name, goal.Description);
        return results.Select(r => r with { Earned = r.Earned || reached.Contains(r.Goal) }).ToList();
    }

    // sponsor goals only count points from that sponsor
    private static long Value(Achievement goal, List<Row> history)
    {
        var rows = goal.SponsorCompanyId is int company ? history.Where(h => h.CompanyId == company) : history;
        return goal.Metric switch
        {
            Achievement.PointsEarned => rows.Where(h => h.Delta > 0).Sum(h => (long)h.Delta),
            Achievement.PointsSpent => rows.Where(h => h.Delta < 0 && h.Order).Sum(h => -(long)h.Delta),
            Achievement.OrdersPlaced => rows.Count(h => h.Order),
            _ => 0
        };
    }

    private sealed record Row(int CompanyId, int Delta, bool Order);
}

// company is null for built-in badges
public record Badge(int Id, string Name, string Description, string? Company, DateTime EarnedAt);

public record GoalProgress(int Id, string Name, string Description, string? Company, string Metric, long Current, int Threshold);
