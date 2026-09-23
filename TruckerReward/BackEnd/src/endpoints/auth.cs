using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// register, log in with a password, log in through google/microsoft
public static class AuthEndpoints
{
    // values in the users.user_type enum
    public const string Driver = "driver";
    public const string Sponsor = "sponsor";
    public const string Admin = "admin";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        // the frontend shows this next to every new-password box
        app.MapGet("/auth/password-policy", () => Results.Ok(new { description = PasswordPolicy.Description }));

        app.MapPost("/auth/register", async (RegisterRequest req, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { message = "Username, email and password are required." });
            if (PasswordPolicy.Check(req.Password) is { } weak)
                return Results.BadRequest(new { message = weak });
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
            user.Password = Passwords.Hash(user, req.Password);
            db.Users.Add(user);
            if (req.UserType == Sponsor)
                db.Sponsors.Add(new Sponsor { User = user, CompanyName = req.CompanyName!.Trim() });
            await db.SaveChangesAsync();

            return Results.Created($"/users/{user.Id}", UserProfile.Of(user));
        });

        app.MapPost("/auth/login", async (LoginRequest req, AppDbContext db, IEmailSender email, TimeProvider clock) =>
        {
            var username = req.Username?.Trim() ?? "";
            var now = clock.GetUtcNow().UtcDateTime;
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            var lockedUntil = await Lockout.LockedUntil(db, username, now);

            var ok = Passwords.Verify(user, req.Password) && lockedUntil is null;
            await RecordAttempt(db, username, user?.Id, ok, req.IpAddress, now);

            if (ok)
                return Results.Ok(UserProfile.Of(user!));
            if (lockedUntil is not null)
                return Results.Json(new { message = "Too many failed attempts.", lockedUntil }, statusCode: StatusCodes.Status423Locked);

            // this failure may be the one that starts a lock, tell the admins once
            if (await Lockout.LockedUntil(db, username, now) is { } until)
                await Notify.Admins(db, email, $"Account locked: {username}",
                    $"{Lockout.MaxFailures} failed sign-in attempts in a row for {username} from {req.IpAddress ?? "an unknown address"}. The account is locked until {until:u}.");
            return Results.Unauthorized();
        });

        // after a google/microsoft sign in on the frontend. first time makes a driver account
        app.MapPost("/auth/external", async (ExternalLoginRequest req, AppDbContext db, TimeProvider clock) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email))
                return Results.BadRequest(new { message = "The sign in provider did not return an email." });

            var email = req.Email.Trim();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user is null)
            {
                user = new User { UserType = Driver, Username = await UniqueUsername(db, email), Email = email, PhoneNumber = "", Address = "" };
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
            await RecordAttempt(db, user.Username, user.Id, true, req.IpAddress, clock.GetUtcNow().UtcDateTime);
            return Results.Ok(UserProfile.Of(user));
        });
    }

    private static Task RecordAttempt(AppDbContext db, string username, int? userId, bool succeeded, string? ip, DateTime at)
    {
        db.LoginAttempts.Add(new LoginAttempt { Username = username, UserId = userId, Succeeded = succeeded, IpAddress = ip, AttemptedAt = at });
        return db.SaveChangesAsync();
    }

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

public record RegisterRequest(string Username, string Email, string Password, string UserType, string? CompanyName, string? PhoneNumber, string? Address);

public record LoginRequest(string Username, string Password, string? IpAddress);

public record ExternalLoginRequest(string Provider, string Email, string? Name, string? IpAddress);

// what login and register hand back, the frontend turns it into the cookie claims
public record UserProfile(int Id, string UserType, string Username, string Email, string PhoneNumber, string Address)
{
    public static UserProfile Of(User u) => new(u.Id, u.UserType, u.Username, u.Email, u.PhoneNumber, u.Address);
}
