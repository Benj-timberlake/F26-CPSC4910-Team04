using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// what the dashboard shows about the signed in user
public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}", async (int id, AppDbContext db) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
                return Results.NotFound();
            var company = await db.Sponsors.AsNoTracking()
                .Where(s => s.UserId == id)
                .Select(s => s.CompanyName)
                .FirstOrDefaultAsync();
            return Results.Ok(new UserDetails(user.Id, user.UserType, user.Username, user.Email, user.PhoneNumber, user.Address, company, user.Points));
        });

        // this login, the one before it, failures in between, the last ten attempts and when the
        // password last changed
        app.MapGet("/users/{id:int}/security", async (int id, AppDbContext db) =>
        {
            if (!await db.Users.AnyAsync(u => u.Id == id))
                return Results.NotFound();

            var attempts = db.LoginAttempts.AsNoTracking()
                .Where(a => a.UserId == id)
                .OrderByDescending(a => a.AttemptedAt).ThenByDescending(a => a.Id);
            var logins = await attempts.Where(a => a.Succeeded).Take(2).ToListAsync();
            var last = logins.ElementAtOrDefault(0);
            var previous = logins.ElementAtOrDefault(1);

            var failedSincePrevious = await attempts
                .CountAsync(a => !a.Succeeded && (previous == null || a.AttemptedAt > previous.AttemptedAt));
            var recent = await attempts
                .Take(10)
                .Select(a => new LoginEvent(a.AttemptedAt, a.Succeeded, a.IpAddress))
                .ToListAsync();
            var passwordChangedAt = await db.PasswordChanges.AsNoTracking()
                .Where(c => c.UserId == id && c.ChangeType != PasswordChange.ResetRequested)
                .MaxAsync(c => (DateTime?)c.ChangedAt);

            return Results.Ok(new SecuritySummary(last?.AttemptedAt, last?.IpAddress, previous?.AttemptedAt, failedSincePrevious, recent, passwordChangedAt));
        });
    }
}

public record UserDetails(int Id, string UserType, string Username, string Email, string PhoneNumber, string Address, string? CompanyName, int Points);

public record LoginEvent(DateTime AttemptedAt, bool Succeeded, string? IpAddress);

public record SecuritySummary(
    DateTime? LastLoginAt,
    string? LastLoginIp,
    DateTime? PreviousLoginAt,
    int FailedSincePreviousLogin,
    List<LoginEvent> Recent,
    DateTime? PasswordChangedAt);
