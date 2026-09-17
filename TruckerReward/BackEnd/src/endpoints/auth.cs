using BackEnd.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

public static class AuthEndpoints
{
    // values in the users.user_type enum
    public const string Driver = "driver";
    public const string Sponsor = "sponsor";
    public const string Admin = "admin";

    // PBKDF2 with a random salt per user, built into asp.net
    private static readonly PasswordHasher<User> hasher = new();

    // stand-ins so a login for a missing user still does one hash verify, see /auth/login
    private static readonly User dummyUser = new() { UserType = Driver, Username = "", Email = "", PhoneNumber = "", Address = "" };
    private static readonly string dummyHash = hasher.HashPassword(dummyUser, Guid.NewGuid().ToString());

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/register", async (RegisterRequest req, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { message = "Username, email and password are required." });
            if (req.UserType != Driver && req.UserType != Sponsor)
                return Results.BadRequest(new { message = "Account type must be driver or sponsor." });
            if (req.UserType == Sponsor && string.IsNullOrWhiteSpace(req.CompanyName))
                return Results.BadRequest(new { message = "Sponsors need a company name." });

            var username = req.Username.Trim();
            var email = req.Email.Trim();
            if (await db.Users.AnyAsync(u => u.Username == username))
                return Results.Conflict(new { message = "That username is already taken." });
            if (await db.Users.AnyAsync(u => u.Email == email))
                return Results.Conflict(new { message = "An account with that email already exists." });

            var user = new User
            {
                UserType = req.UserType,
                Username = username,
                Email = email,
                PhoneNumber = req.PhoneNumber?.Trim() ?? "",
                Address = req.Address?.Trim() ?? ""
            };
            user.Password = hasher.HashPassword(user, req.Password);
            db.Users.Add(user);
            if (req.UserType == Sponsor)
                db.Sponsors.Add(new Sponsor { User = user, CompanyName = req.CompanyName!.Trim() });
            await db.SaveChangesAsync();

            return Results.Created($"/users/{user.Id}", ToProfile(user));
        });

        app.MapPost("/auth/login", async (LoginRequest req, AppDbContext db) =>
        {
            var username = req.Username?.Trim() ?? "";
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);

            // always run a hash check, even when the username doesn't exist or the account is sso only,
            // so the response time doesn't tell an attacker which usernames are real
            var ok = hasher.VerifyHashedPassword(
                user ?? dummyUser,
                user?.Password ?? dummyHash,
                req.Password ?? "") != PasswordVerificationResult.Failed
                && user?.Password is not null;

            // every attempt is logged, unknown usernames included, so admins can spot guessing
            db.LoginAttempts.Add(new LoginAttempt
            {
                Username = username,
                UserId = user?.Id,
                Succeeded = ok,
                IpAddress = req.IpAddress,
                AttemptedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            return ok
                ? Results.Ok(ToProfile(user!))
                : Results.Unauthorized();
        });

        // called after a google/microsoft sign in on the frontend
        app.MapPost("/auth/external", async (ExternalLoginRequest req, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email))
                return Results.BadRequest(new { message = "The sign in provider did not return an email." });

            var email = req.Email.Trim();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user is null)
            {
                user = new User
                {
                    UserType = Driver,
                    Username = await UniqueUsername(db, email),
                    Email = email,
                    PhoneNumber = "",
                    Address = ""
                };
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }

            db.LoginAttempts.Add(new LoginAttempt
            {
                Username = user.Username,
                UserId = user.Id,
                Succeeded = true,
                IpAddress = req.IpAddress,
                AttemptedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            return Results.Ok(ToProfile(user));
        });

        app.MapGet("/users/{id:int}", async (int id, AppDbContext db) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
                return Results.NotFound();
            var company = await db.Sponsors.AsNoTracking()
                .Where(s => s.UserId == id)
                .Select(s => s.CompanyName)
                .FirstOrDefaultAsync();
            return Results.Ok(new UserDetails(user.Id, user.UserType, user.Username, user.Email, user.PhoneNumber, user.Address, company));
        });

        app.MapGet("/admin/login-attempts", async (bool failedOnly, int? limit, AppDbContext db) =>
        {
            var query = db.LoginAttempts.AsNoTracking();
            if (failedOnly)
                query = query.Where(a => !a.Succeeded);
            var attempts = await query
                .OrderByDescending(a => a.AttemptedAt)
                .Take(limit is > 0 and <= 500 ? limit.Value : 100)
                .ToListAsync();
            return Results.Ok(attempts);
        });

        // real query so this fails if the db is asleep or unreachable
        app.MapGet("/health", async (AppDbContext db) =>
        {
            try
            {
                var users = await db.Users.CountAsync();
                return Results.Ok(new { status = "ok", users });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 503);
            }
        });
    }

    private static UserProfile ToProfile(User u) =>
        new(u.Id, u.UserType, u.Username, u.Email, u.PhoneNumber, u.Address);

    // sso accounts don't pick a username so use the email prefix, add a number if taken
    private static async Task<string> UniqueUsername(AppDbContext db, string email)
    {
        var baseName = email.Split('@')[0];
        var name = baseName;
        for (var n = 2; await db.Users.AnyAsync(u => u.Username == name); n++)
            name = baseName + n;
        return name;
    }
}

public record RegisterRequest(
    string Username,
    string Email,
    string Password,
    string UserType,
    string? CompanyName,
    string? PhoneNumber,
    string? Address);

public record LoginRequest(string Username, string Password, string? IpAddress);

// what login/register hand back, the frontend turns this into the cookie claims
public record UserProfile(int Id, string UserType, string Username, string Email, string PhoneNumber, string Address);

public record ExternalLoginRequest(string Provider, string Email, string? Name, string? IpAddress);

public record UserDetails(int Id, string UserType, string Username, string Email, string PhoneNumber, string Address, string? CompanyName);
