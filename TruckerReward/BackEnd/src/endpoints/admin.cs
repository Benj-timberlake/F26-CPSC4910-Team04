using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// the admin security page: who is locked, every sign-in attempt, every password change
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/locked-accounts", async (AppDbContext db, TimeProvider clock) =>
            Results.Ok(await Lockout.All(db, clock.GetUtcNow().UtcDateTime)));

        app.MapGet("/admin/login-attempts", async (bool failedOnly, int? limit, AppDbContext db) =>
        {
            var query = db.LoginAttempts.AsNoTracking();
            if (failedOnly)
                query = query.Where(a => !a.Succeeded);
            return Results.Ok(await query
                .OrderByDescending(a => a.AttemptedAt).ThenByDescending(a => a.Id)
                .Take(Limit(limit))
                .ToListAsync());
        });

        app.MapGet("/admin/password-changes", async (int? limit, AppDbContext db) =>
            Results.Ok(await db.PasswordChanges.AsNoTracking()
                .OrderByDescending(c => c.ChangedAt).ThenByDescending(c => c.Id)
                .Take(Limit(limit))
                .Join(db.Users, c => c.UserId, u => u.Id,
                    (c, u) => new PasswordChangeEntry(c.ChangedAt, u.Id, u.Username, c.ChangeType, c.IpAddress))
                .ToListAsync()));
    }

    // lists take an optional limit, capped so nobody asks for the whole table
    private static int Limit(int? requested) => requested is > 0 and <= 500 ? requested.Value : 100;
}

public record PasswordChangeEntry(DateTime ChangedAt, int UserId, string Username, string ChangeType, string? IpAddress);
