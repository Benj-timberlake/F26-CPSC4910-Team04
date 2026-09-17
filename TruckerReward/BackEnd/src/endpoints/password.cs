using System.Security.Cryptography;
using System.Text;
using BackEnd.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// change password while logged in, forgot/reset by email, and the admin view of the audit log
public static class PasswordEndpoints
{
    public static readonly TimeSpan ResetLinkLifetime = TimeSpan.FromHours(1);

    private static readonly PasswordHasher<User> hasher = new();

    public static void MapPasswordEndpoints(this WebApplication app)
    {
        app.MapPost("/users/{id:int}/password", async (int id, ChangePasswordRequest req, AppDbContext db, IEmailSender email, TimeProvider clock) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null)
                return Results.NotFound();
            // sso-only accounts have no password yet, so there's nothing to confirm
            if (user.Password is not null
                && hasher.VerifyHashedPassword(user, user.Password, req.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
                return Results.Json(new { message = "Current password is wrong." }, statusCode: StatusCodes.Status401Unauthorized);
            if (PasswordPolicy.Check(req.NewPassword) is { } weak)
                return Results.BadRequest(new { message = weak });

            await SetPassword(db, user, req.NewPassword, PasswordChange.Changed, req.IpAddress, clock);
            await email.SendAsync(user.Email, "Your TruckerReward password was changed",
                $"Hi {user.Username},\n\nYour password was just changed. If that wasn't you, reset it right away from the login page.");
            return Results.NoContent();
        });

        app.MapPost("/auth/forgot", async (ForgotPasswordRequest req, AppDbContext db, IEmailSender email, TimeProvider clock, IConfiguration config) =>
        {
            // same answer whether or not the email exists, so this can't be used to find accounts
            var reply = Results.Ok(new { message = "If that email has an account, a reset link is on its way." });
            var address = req.Email?.Trim() ?? "";
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == address);
            if (user is null)
                return reply;

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var now = clock.GetUtcNow().UtcDateTime;
            db.PasswordResets.Add(new PasswordReset { UserId = user.Id, TokenHash = Hash(token), ExpiresAt = now + ResetLinkLifetime });
            db.PasswordChanges.Add(new PasswordChange { UserId = user.Id, ChangeType = PasswordChange.ResetRequested, IpAddress = req.IpAddress, ChangedAt = now });
            await db.SaveChangesAsync();

            var link = $"{config["FRONTEND_URL"]?.TrimEnd('/')}/reset-password?token={token}";
            await email.SendAsync(user.Email, "Reset your TruckerReward password",
                $"Hi {user.Username},\n\nUse this link within the next hour to choose a new password:\n{link}\n\nIf you didn't ask for this, ignore this email and your password stays the same.");
            await Notify.Admins(db, email, $"Password reset requested for {user.Username}",
                $"A password reset was requested for {user.Username} ({user.Email}) from {req.IpAddress ?? "an unknown address"}.");
            return reply;
        });

        app.MapPost("/auth/reset", async (ResetPasswordRequest req, AppDbContext db, IEmailSender email, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var hash = Hash(req.Token ?? "");
            var reset = await db.PasswordResets.FirstOrDefaultAsync(r => r.TokenHash == hash && r.UsedAt == null && r.ExpiresAt > now);
            if (reset is null)
                return Results.BadRequest(new { message = "That reset link is invalid or has expired. Request a new one." });
            if (PasswordPolicy.Check(req.NewPassword) is { } weak)
                return Results.BadRequest(new { message = weak });

            var user = await db.Users.FindAsync(reset.UserId);
            reset.UsedAt = now;
            await SetPassword(db, user!, req.NewPassword, PasswordChange.ResetCompleted, req.IpAddress, clock);
            await email.SendAsync(user!.Email, "Your TruckerReward password was reset",
                $"Hi {user.Username},\n\nYour password was just reset. If that wasn't you, contact your sponsor or an administrator.");
            return Results.NoContent();
        });

        app.MapGet("/admin/password-changes", async (int? limit, AppDbContext db) =>
        {
            var changes = await db.PasswordChanges.AsNoTracking()
                .OrderByDescending(c => c.ChangedAt).ThenByDescending(c => c.Id)
                .Take(Paging.Limit(limit))
                .Join(db.Users, c => c.UserId, u => u.Id,
                    (c, u) => new PasswordChangeEntry(c.ChangedAt, u.Id, u.Username, c.ChangeType, c.IpAddress))
                .ToListAsync();
            return Results.Ok(changes);
        });
    }

    private static async Task SetPassword(AppDbContext db, User user, string password, string changeType, string? ip, TimeProvider clock)
    {
        user.Password = hasher.HashPassword(user, password);
        db.PasswordChanges.Add(new PasswordChange { UserId = user.Id, ChangeType = changeType, IpAddress = ip, ChangedAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync();
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public record ChangePasswordRequest(string? CurrentPassword, string NewPassword, string? IpAddress);

public record ForgotPasswordRequest(string Email, string? IpAddress);

public record ResetPasswordRequest(string Token, string NewPassword, string? IpAddress);

public record PasswordChangeEntry(DateTime ChangedAt, int UserId, string Username, string ChangeType, string? IpAddress);
