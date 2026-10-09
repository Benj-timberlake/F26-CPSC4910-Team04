using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// admin pages: lockouts, sign-in attempts, password changes and account status
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

        // sort is name, created or last-login
        app.MapGet("/admin/accounts", async (string? search, string? type, string? status, string? sort, AppDbContext db) =>
        {
            var users = db.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                users = users.Where(u => u.Username.ToLower().Contains(term) || u.Email.ToLower().Contains(term)
                    || (u.FirstName + " " + u.LastName).ToLower().Contains(term));
            }
            if (!string.IsNullOrWhiteSpace(type))
                users = users.Where(u => u.UserType == type);
            if (!string.IsNullOrWhiteSpace(status))
                users = users.Where(u => u.Status == status);

            var rows = users.Select(u => new
            {
                User = u,
                // the users insert trigger writes the first accounts_history row
                CreatedAt = db.AccountsHistories.Where(h => h.UserId == u.Id).Min(h => (DateTime?)h.Timestamp),
                LastLoginAt = db.LoginAttempts.Where(a => a.UserId == u.Id && a.Succeeded).Max(a => (DateTime?)a.AttemptedAt)
            });
            rows = sort switch
            {
                "created" => rows.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.User.Username),
                "last-login" => rows.OrderByDescending(r => r.LastLoginAt).ThenBy(r => r.User.Username),
                _ => rows.OrderBy(r => r.User.LastName).ThenBy(r => r.User.FirstName).ThenBy(r => r.User.Username)
            };
            return Results.Ok(await rows.Take(MaxAccounts)
                .Select(r => new AdminAccount(r.User.Id, r.User.Username, r.User.FirstName, r.User.LastName, r.User.Email,
                    r.User.UserType, r.User.Status, r.CreatedAt, r.LastLoginAt))
                .ToListAsync());
        });

        app.MapGet("/users/{id:int}/status", async (int id, AppDbContext db) =>
            await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Status).FirstOrDefaultAsync() is { } status
                ? Results.Ok(new AccountStatus(status))
                : Results.NotFound());

        app.MapPost("/admin/accounts/{id:int}/status", async (int id, ChangeAccountStatusRequest req, AppDbContext db, Notifications notifications) =>
        {
            if (req.Status is not (User.Active or User.Inactive))
                return Results.BadRequest(new { message = "Status must be active or inactive." });
            var admin = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.AdminId && u.UserType == AuthEndpoints.Admin);
            if (admin is null)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (admin.Id == id)
                return Results.BadRequest(new { message = "You can't change the status of your own account." });
            var account = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (account is null)
                return Results.NotFound();
            if (account.Status == req.Status)
                return Results.Conflict(new { message = $"That account is already {req.Status}." });

            account.Status = req.Status;
            await db.SaveChangesAsync();
            await notifications.AccountStatusChanged(account, admin);
            return Results.NoContent();
        });
    }

    public const int MaxAccounts = 500;

    // lists take an optional limit, capped so nobody asks for the whole table
    private static int Limit(int? requested) => requested is > 0 and <= 500 ? requested.Value : 100;
}

public record AdminAccount(int Id, string Username, string FirstName, string LastName, string Email, string UserType, string Status, DateTime? CreatedAt, DateTime? LastLoginAt);

public record AccountStatus(string Status);

public record ChangeAccountStatusRequest(int AdminId, string? Status);

public record PasswordChangeEntry(DateTime ChangedAt, int UserId, string Username, string ChangeType, string? IpAddress);
