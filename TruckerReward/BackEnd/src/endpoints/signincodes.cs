using System.Security.Cryptography;
using System.Text;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// optional emailed code after the password. passkeys and google/microsoft skip it
public static class SignInCodeEndpoints
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int MaxAttempts = 5;
    // codes a password can ask for per Lifetime, so the inbox can't be flooded
    public const int MaxIssued = 3;

    public static void MapSignInCodeEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/login/code", async (CodeRequest req, AppDbContext db, Notifications notifications, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var hash = Hash(req.Challenge ?? "");
            var pending = await db.SignInCodes.FirstOrDefaultAsync(c => c.ChallengeHash == hash && c.UsedAt == null && c.ExpiresAt > now && c.Attempts < MaxAttempts);
            var user = pending is null ? null : await db.Users.FindAsync(pending.UserId);
            if (pending is null || user is null)
                return Results.Json(new { message = "That code has expired. Log in again for a new one." }, statusCode: StatusCodes.Status410Gone);
            if (await Lockout.LockedUntil(db, user.Username, now) is { } lockedUntil)
                return Results.Json(new { message = "Too many failed attempts.", lockedUntil }, statusCode: StatusCodes.Status423Locked);

            var right = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Hash(req.Code?.Trim() ?? "")), Encoding.UTF8.GetBytes(pending.CodeHash));
            pending.Attempts++;
            if (right)
                pending.UsedAt = now;
            // wrong codes feed the same lockout as wrong passwords
            db.LoginAttempts.Add(new LoginAttempt { Username = user.Username, UserId = user.Id, Succeeded = right, IpAddress = req.IpAddress ?? "", AttemptedAt = now });
            await db.SaveChangesAsync();
            if (right)
                return Results.Ok(UserProfile.Of(user));
            if (await Lockout.LockedUntil(db, user.Username, now) is { } until)
                await notifications.AccountLocked(user.Username, req.IpAddress, until);
            return pending.Attempts >= MaxAttempts
                ? Results.Json(new { message = "Too many wrong codes. Log in again for a new one." }, statusCode: StatusCodes.Status410Gone)
                : Results.BadRequest(new { message = "That code isn't right. Check the email and try again." });
        });

        app.MapPut("/users/{id:int}/sign-in-codes", async (int id, CodeSettingRequest req, AppDbContext db, IEmailSender email) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null)
                return Results.NotFound();
            if (user.Password is not null && !Passwords.Verify(user, req.CurrentPassword))
                return Results.Json(new { message = "Current password is wrong." }, statusCode: StatusCodes.Status401Unauthorized);
            if (req.Enabled && user.EmailConfirmedAt is null)
                return Results.Conflict(new { message = "Confirm your email first, so the codes reach you." });

            user.EmailCodes = req.Enabled;
            await db.SaveChangesAsync();
            await email.SendAsync(user.Email, req.Enabled ? "Sign-in codes are on" : "Sign-in codes are off",
                $"Hi {user.Username},\n\n" + (req.Enabled
                    ? "From now on, signing in with your password also needs a code we email here."
                    : "Signing in with your password no longer needs an emailed code. If that wasn't you, reset your password right away."));
            return Results.NoContent();
        });
    }

    // null when the password alone is enough
    internal static async Task<IResult?> Challenge(AppDbContext db, User user, IEmailSender email, TimeProvider clock)
    {
        if (!user.EmailCodes || user.EmailConfirmedAt is null)
            return null;
        var now = clock.GetUtcNow().UtcDateTime;
        if (await db.SignInCodes.CountAsync(c => c.UserId == user.Id && c.CreatedAt > now - Lifetime) >= MaxIssued)
            return Results.Json(new { message = "Too many codes asked for. Wait a few minutes and log in again." }, statusCode: StatusCodes.Status429TooManyRequests);

        var challenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.SignInCodes.Add(new SignInCode { UserId = user.Id, ChallengeHash = Hash(challenge), CodeHash = Hash(code), CreatedAt = now, ExpiresAt = now + Lifetime });
        await db.SaveChangesAsync();
        await email.SendAsync(user.Email, $"Your TruckerReward sign-in code is {code}",
            $"Hi {user.Username},\n\nYour sign-in code is {code}. It works for ten minutes.\n\nIf you didn't just sign in, someone has your password. Reset it right away from the login page.");
        return Results.Json(new { challenge }, statusCode: StatusCodes.Status202Accepted);
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public record CodeRequest(string? Challenge, string? Code, string? IpAddress);

public record CodeSettingRequest(bool Enabled, string? CurrentPassword);
