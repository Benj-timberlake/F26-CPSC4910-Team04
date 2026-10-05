using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// what the dashboard shows about the signed in user
public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        app.MapGet("/companies", async (AppDbContext db) =>
            Results.Ok(await db.Companies.AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CompanyOption(c.Id, c.Name, c.Description))
                .ToListAsync()));

        app.MapGet("/users/{id:int}", async (int id, AppDbContext db) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
                return Results.NotFound();
            var company = await db.Companies.AsNoTracking()
                .Where(c => c.Id == user.CompanyId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();
            return Results.Ok(new UserDetails(user.Id, user.UserType, user.Username, user.FirstName, user.LastName, user.Email, user.PhoneNumber, user.Address ?? "", company, user.CompanyId, user.Points));
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

        app.MapPost("/users/{id:int}/username", async (int id, ChangeUsernameRequest req, AppDbContext db, IEmailSender email) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null)
                return Results.NotFound();
            // sso-only accounts have no password yet, so there's nothing to confirm
            if (user.Password is not null && !Passwords.Verify(user, req.CurrentPassword))
                return Results.Json(new { message = "Current password is wrong." }, statusCode: StatusCodes.Status401Unauthorized);

            var username = req.NewUsername?.Trim() ?? "";
            if (username.Length is 0 or > MaxUsernameLength)
                return Results.BadRequest(new { message = $"Usernames are 1 to {MaxUsernameLength} characters." });
            if (username == user.Username)
                return Results.BadRequest(new { message = "That is already your username." });
            if (await db.Users.AnyAsync(u => u.Username == username && u.Id != id))
                return Results.Conflict(new { message = "That username is already taken." });

            var old = user.Username;
            user.Username = username;
            await db.SaveChangesAsync();
            await email.SendAsync(user.Email, "Your TruckerReward username was changed",
                $"Hi {username},\n\nYour username was just changed from {old} to {username}. If that wasn't you, reset your password right away from the login page.");
            return Results.Ok(UserProfile.Of(user));
        });

        app.MapPut("/users/{id:int}/profile", async (int id, UpdateUserProfileRequest req, AppDbContext db) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null)
                return Results.NotFound();

            var firstName = req.FirstName?.Trim() ?? "";
            var lastName = req.LastName?.Trim() ?? "";
            var phoneNumber = req.PhoneNumber?.Trim() ?? "";
            var address = req.Address?.Trim() ?? "";
            if (firstName.Length is 0 or > 100 || lastName.Length is 0 or > 100)
                return Results.BadRequest(new { message = "First and last names are required and must be 100 characters or fewer." });
            if (phoneNumber.Length > 20 || address.Length > 255)
                return Results.BadRequest(new { message = "Phone number must be 20 characters or fewer and address must be 255 characters or fewer." });

            user.FirstName = firstName;
            user.LastName = lastName;
            user.PhoneNumber = phoneNumber;
            user.Address = address;
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }

    // users.username is varchar(255)
    private const int MaxUsernameLength = 255;
}

public record ChangeUsernameRequest(string? CurrentPassword, string? NewUsername);
public record UpdateUserProfileRequest(string? FirstName, string? LastName, string? PhoneNumber, string? Address);

public record UserDetails(int Id, string UserType, string Username, string FirstName, string LastName, string Email, string PhoneNumber, string Address, string? CompanyName, int? CompanyId, int Points);

public record CompanyOption(int Id, string Name, string? Description);

public record LoginEvent(DateTime AttemptedAt, bool Succeeded, string? IpAddress);

public record SecuritySummary(
    DateTime? LastLoginAt,
    string? LastLoginIp,
    DateTime? PreviousLoginAt,
    int FailedSincePreviousLogin,
    List<LoginEvent> Recent,
    DateTime? PasswordChangedAt);
