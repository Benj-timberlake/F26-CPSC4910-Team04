using System.Security.Cryptography;
using System.Text;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// email alerts wait until the user clicks the link sent to their address
public static class EmailConfirmEndpoints
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(2);

    public static void MapEmailConfirmEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/confirm-email", async (ConfirmEmailRequest req, AppDbContext db, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var hash = Hash(req.Token ?? "");
            var link = await db.EmailConfirmations.FirstOrDefaultAsync(c => c.TokenHash == hash && c.UsedAt == null && c.ExpiresAt > now);
            var user = link is null ? null : await db.Users.FindAsync(link.UserId);
            // a link for an address the user has since changed away from is dead
            if (link is null || user is null || !string.Equals(user.Email, link.Email, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "That confirmation link is invalid or has expired. Send a new one from your profile." });

            link.UsedAt = now;
            user.EmailConfirmedAt ??= now;
            await db.SaveChangesAsync();
            return Results.Ok(new { message = $"{user.Email} is confirmed." });
        });

        app.MapPost("/users/{id:int}/email/confirmation", async (int id, AppDbContext db, IEmailSender email, TimeProvider clock, IConfiguration config) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null)
                return Results.NotFound();
            if (user.EmailConfirmedAt is not null)
                return Results.Conflict(new { message = "Your email is already confirmed." });
            await Send(db, user, email, clock, config);
            return Results.Ok(new { message = $"A confirmation link was sent to {user.Email}." });
        });
    }

    // for a new or changed address. emailed only, so the link never sits in the inbox table
    public static async Task Send(AppDbContext db, User user, IEmailSender email, TimeProvider clock, IConfiguration config)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        db.EmailConfirmations.Add(new EmailConfirmation
        {
            UserId = user.Id,
            Email = user.Email,
            TokenHash = Hash(token),
            ExpiresAt = clock.GetUtcNow().UtcDateTime + LinkLifetime
        });
        await db.SaveChangesAsync();
        var link = $"{config["FRONTEND_URL"]?.TrimEnd('/')}/confirm-email?token={token}";
        await email.SendAsync(user.Email, "Confirm your TruckerReward email",
            $"Hi {user.Username},\n\nUse this link within two days to confirm this is your address:\n{link}\n\nEmail alerts start once it's confirmed. If you didn't make a TruckerReward account, ignore this email.");
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public record ConfirmEmailRequest(string? Token);
