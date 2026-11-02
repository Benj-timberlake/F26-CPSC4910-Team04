using System.Security.Cryptography;
using System.Text;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// change password while logged in, forgot/reset by email
public static class PasswordEndpoints
{
    public static readonly TimeSpan ResetLinkLifetime = TimeSpan.FromHours(1);

    public static void MapPasswordEndpoints(this WebApplication app)
    {
        app.MapPost("/users/{id:int}/password", async (int id, ChangePasswordRequest req, AppDbContext db, Notifications notifications, TimeProvider clock) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null)
                return Results.NotFound();
            // sso-only accounts have no password yet, so there's nothing to confirm
            if (user.Password is not null && !Passwords.Verify(user, req.CurrentPassword))
                return Results.Json(new { message = "Current password is wrong." }, statusCode: StatusCodes.Status401Unauthorized);
            if (PasswordPolicy.Check(req.NewPassword) is { } weak)
                return Results.BadRequest(new { message = weak });

            await SetPassword(db, user, req.NewPassword, PasswordChange.Changed, req.IpAddress, clock, req.SessionKey);
            await notifications.PasswordChanged(user, byReset: false);
            // the version moved, so the caller refreshes its cookie from this
            return Results.Ok(UserProfile.Of(await db.Users.AsNoTracking().SingleAsync(u => u.Id == id)));
        });

        app.MapPost("/auth/forgot", async (ForgotPasswordRequest req, AppDbContext db, Notifications notifications, TimeProvider clock, IConfiguration config) =>
        {
            // same answer whether or not the email exists, so this can't be used to find accounts
            var reply = Results.Ok(new { message = "If that email has an account, a reset link is on its way." });
            var address = req.Email?.Trim() ?? "";
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == address);
            if (user is null)
                return reply;

            var now = clock.GetUtcNow().UtcDateTime;
            var link = ResetLink(db, user, now + ResetLinkLifetime, config);
            db.PasswordChanges.Add(new PasswordChange { UserId = user.Id, ChangeType = PasswordChange.ResetRequested, IpAddress = req.IpAddress, ChangedAt = now });
            await db.SaveChangesAsync();

            await notifications.ResetLinkSent(user, link);
            await notifications.ResetRequested(user, req.IpAddress);
            return reply;
        });

        app.MapPost("/admin/accounts/{id:int}/reset-link", async (int id, AdminResetRequest req, AppDbContext db, Notifications notifications, TimeProvider clock, IConfiguration config) =>
        {
            var admin = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.AdminId && u.UserType == AuthEndpoints.Admin);
            if (admin is null)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
                return Results.NotFound();
            if (user.Status != User.Active)
                return Results.Conflict(new { message = $"{user.Username} is deactivated, so reactivate the account first." });

            var now = clock.GetUtcNow().UtcDateTime;
            var link = ResetLink(db, user, now + ResetLinkLifetime, config);
            db.PasswordChanges.Add(new PasswordChange { UserId = user.Id, ChangeType = PasswordChange.ResetRequested, ChangedAt = now, RequestedBy = admin.Id });
            await db.SaveChangesAsync();

            await notifications.ResetLinkSent(user, link);
            return Results.Ok(new { message = $"A reset link was emailed to {user.Email}." });
        });

        app.MapPost("/auth/reset", async (ResetPasswordRequest req, AppDbContext db, Notifications notifications, TimeProvider clock) =>
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
            // the link reached their inbox, which is all a confirmation proves
            user!.EmailConfirmedAt ??= now;
            await SetPassword(db, user!, req.NewPassword, PasswordChange.ResetCompleted, req.IpAddress, clock);
            await notifications.PasswordChanged(user!, byReset: true);
            return Results.NoContent();
        });
    }

    // the caller saves
    internal static string ResetLink(AppDbContext db, User user, DateTime expiresAt, IConfiguration config)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        db.PasswordResets.Add(new PasswordReset { UserId = user.Id, TokenHash = Hash(token), ExpiresAt = expiresAt });
        return $"{config["FRONTEND_URL"]?.TrimEnd('/')}/reset-password?token={token}";
    }

    // signs out every other browser and voids any other reset links
    private static async Task SetPassword(AppDbContext db, User user, string password, string changeType, string? ip, TimeProvider clock, string? keepSession = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        user.Password = Passwords.Hash(user, password);
        db.PasswordChanges.Add(new PasswordChange { UserId = user.Id, ChangeType = changeType, IpAddress = ip, ChangedAt = now });
        await db.SaveChangesAsync();
        await db.PasswordResets.Where(r => r.UserId == user.Id && r.UsedAt == null)
            .ExecuteUpdateAsync(r => r.SetProperty(x => x.UsedAt, now));
        await SessionEndpoints.RevokeAll(db, user.Id, now, keepSession);
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public record ChangePasswordRequest(string? CurrentPassword, string NewPassword, string? IpAddress, string? SessionKey = null);

public record ForgotPasswordRequest(string Email, string? IpAddress);

public record AdminResetRequest(int AdminId);

public record ResetPasswordRequest(string Token, string NewPassword, string? IpAddress);
