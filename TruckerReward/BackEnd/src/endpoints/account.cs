using BackEnd.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// a user's own data download and account delete
public static class AccountDataEndpoints
{
    public static void MapAccountDataEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/export", Export);
        app.MapDelete("/users/{id:int}/account", DeleteOwnAccount);
    }

    // everything kept about the user, minus hashes, tokens and key material
    private static async Task<IResult> Export(int id, AppDbContext db, TimeProvider clock)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return Results.NotFound();

        var history = await db.AccountsHistories.AsNoTracking().Where(h => h.UserId == id)
            .OrderBy(h => h.Timestamp).ThenBy(h => h.Id)
            .Select(h => new { h.Timestamp, h.UserType, h.Username, h.Email, h.FirstName, h.LastName, h.PhoneNumber, h.Address, h.Points })
            .ToListAsync();
        var sessions = await db.UserSessions.AsNoTracking().Where(s => s.UserId == id)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Select(s => new { s.CreatedAt, s.LastSeenAt, s.RevokedAt, s.IpAddress, s.UserAgent })
            .ToListAsync();

        return Results.Ok(new
        {
            ExportedAt = clock.GetUtcNow().UtcDateTime,
            Profile = new
            {
                user.Id, user.UserType, user.Username, user.FirstName, user.LastName, user.Email, user.PhoneNumber,
                Address = user.Address ?? "", user.Status, user.Points, user.EmailConfirmedAt, user.EmailCodes, user.LeaderboardHidden,
                HasPassword = user.Password is not null,
                CreatedAt = history.Count == 0 ? (DateTime?)null : history[0].Timestamp
            },
            AccountHistory = history,
            Companies = await db.AccountsCompanies.AsNoTracking().Where(m => m.AccountId == id)
                .OrderBy(m => m.Company.Name)
                .Select(m => new { m.CompanyId, m.Company.Name, m.CurrentCompanyViewed })
                .ToListAsync(),
            Points = await db.PointsHistory.AsNoTracking().Where(h => h.UserId == id)
                .OrderByDescending(h => h.Timestamp).ThenByDescending(h => h.Id)
                .Select(h => new
                {
                    h.Id, h.Timestamp, h.CompanyId, Company = h.Company.Name, Points = h.PointsDelta,
                    Reason = h.AuditHistories.Select(a => a.Resoning).FirstOrDefault(), h.RejectionReason
                })
                .ToListAsync(),
            Orders = await db.CartItems.AsNoTracking().Where(c => c.UserId == id)
                .OrderBy(c => c.Id)
                .Select(c => new { c.Name, c.Description, c.Price, c.Quantity, c.InCart, c.WasOrdered, c.PointsHistoryId })
                .ToListAsync(),
            Wishlist = await db.WishlistItems.AsNoTracking().Where(w => w.UserId == id)
                .OrderBy(w => w.ItemId).Select(w => w.ItemId).ToListAsync(),
            Notifications = await db.NotificationsHistories.AsNoTracking().Where(n => n.UserId == id)
                .OrderByDescending(n => n.Timestamp).ThenByDescending(n => n.Id)
                .Select(n => new { n.Timestamp, n.Subject, n.Body, Read = n.BeenRead })
                .ToListAsync(),
            NotificationPreferences = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == id)
                .OrderBy(p => p.Category).Select(p => new { p.Category, p.Enabled, p.Emailed }).ToListAsync(),
            Sessions = sessions.Select(s => new { s.CreatedAt, s.LastSeenAt, s.RevokedAt, s.IpAddress, Device = SessionEndpoints.Device(s.UserAgent) }),
            LoginAttempts = await db.LoginAttempts.AsNoTracking().Where(a => a.UserId == id)
                .OrderByDescending(a => a.AttemptedAt).ThenByDescending(a => a.Id)
                .Select(a => new { a.AttemptedAt, a.Succeeded, a.IpAddress }).ToListAsync(),
            PasswordChanges = await db.PasswordChanges.AsNoTracking().Where(c => c.UserId == id)
                .OrderByDescending(c => c.ChangedAt).ThenByDescending(c => c.Id)
                .Select(c => new { c.ChangedAt, c.ChangeType, c.IpAddress }).ToListAsync(),
            EmailConfirmations = await db.EmailConfirmations.AsNoTracking().Where(c => c.UserId == id)
                .OrderByDescending(c => c.ExpiresAt).ThenByDescending(c => c.Id)
                .Select(c => new { c.Email, c.ExpiresAt, c.UsedAt }).ToListAsync(),
            Passkeys = await db.PasskeyCredentials.AsNoTracking().Where(p => p.UserId == id)
                .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
                .Select(p => new { p.Name, p.CreatedAt, p.LastUsedAt }).ToListAsync(),
            Badges = await db.EarnedBadges.AsNoTracking().Where(b => b.UserId == id)
                .Join(db.Achievements, b => b.AchievementId, a => a.Id, (b, a) => new { a.Name, a.Description, b.EarnedAt })
                .OrderBy(b => b.EarnedAt).ToListAsync(),
            Applications = await db.Applications.AsNoTracking().Where(a => a.ApplicantId == id)
                .OrderByDescending(a => a.ApplicationTimestamp)
                .Select(a => new
                {
                    a.CompanyId, Company = db.Companies.Where(c => c.Id == a.CompanyId).Select(c => c.Name).FirstOrDefault(),
                    a.Status, a.ApplicantExtraInfo, a.ReviewerReasoning, AppliedAt = a.ApplicationTimestamp, RespondedAt = a.ResponseTimestamp
                })
                .ToListAsync(),
            ApplicationsReviewed = await db.Applications.AsNoTracking().Where(a => a.ReviewerId == id)
                .OrderByDescending(a => a.ResponseTimestamp)
                .Select(a => new { a.CompanyId, a.Status, a.ReviewerReasoning, RespondedAt = a.ResponseTimestamp })
                .ToListAsync(),
            PointsChangesMade = await db.AuditHistories.AsNoTracking().Where(a => a.UserId == id)
                .OrderByDescending(a => a.PointsHistory.Timestamp).ThenByDescending(a => a.Id)
                .Select(a => new { a.PointsHistory.Timestamp, a.PointsHistory.CompanyId, Points = a.PointsHistory.PointsDelta, Reason = a.Resoning })
                .ToListAsync(),
            Announcements = await db.Announcements.AsNoTracking().Where(a => a.AuthorId == id)
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
                .Select(a => new { a.CompanyId, a.Title, a.Body, a.CreatedAt }).ToListAsync(),
            Achievements = await db.Achievements.AsNoTracking().Where(a => a.CreatedBy == id)
                .OrderBy(a => a.Id).Select(a => new { a.SponsorCompanyId, a.Name, a.Description, a.Metric, a.Threshold, a.Active }).ToListAsync(),
            Banners = await db.SiteBanners.AsNoTracking().Where(b => b.SetBy == id)
                .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
                .Select(b => new { b.Message, b.Level, b.CreatedAt, b.ClearedAt }).ToListAsync(),
            CatalogItems = await db.CatalogItems.AsNoTracking().Where(c => c.SponsorId == id)
                .OrderBy(c => c.ItemId).Select(c => c.ItemId).ToListAsync(),
            ViewedBy = await db.Impersonations.AsNoTracking().Where(i => i.UserId == id)
                .OrderByDescending(i => i.StartedAt).ThenByDescending(i => i.Id)
                .Select(i => new { i.StartedAt, Viewer = db.Users.Where(u => u.Id == i.ViewerId).Select(u => u.Username).FirstOrDefault() })
                .ToListAsync()
        });
    }

    private static async Task<IResult> DeleteOwnAccount(int id, [FromBody] DeleteAccountRequest req, AppDbContext db, TimeProvider clock)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return Results.NotFound();
        if (user.UserType == AuthEndpoints.Admin)
            return Results.Json(new { message = "Admins can't delete their own account. Ask another admin to do it." }, statusCode: StatusCodes.Status403Forbidden);
        // google/microsoft-only accounts confirm with their username
        var confirmed = user.Password is not null
            ? Passwords.Verify(user, req.Confirmation)
            : string.Equals(req.Confirmation?.Trim(), user.Username, StringComparison.Ordinal);
        if (!confirmed)
            return Results.Json(new { message = user.Password is not null ? "Current password is wrong." : "Type your username exactly to confirm." },
                statusCode: StatusCodes.Status401Unauthorized);

        if (!await AdminEndpoints.HasHistory(db, id) && await AdminEndpoints.HardDelete(db, id))
            return Results.Ok(new { anonymized = false });
        await Anonymize(db, user, clock.GetUtcNow().UtcDateTime);
        return Results.Ok(new { anonymized = true });
    }

    // strips identity but keeps rows others rely on
    internal static async Task Anonymize(AppDbContext db, User user, DateTime now)
    {
        var id = user.Id;
        var username = $"deleted-{id}";
        for (var n = 2; await db.Users.AnyAsync(u => u.Username == username && u.Id != id); n++)
            username = $"deleted-{id}-{n}";

        await using var transaction = await db.Database.BeginTransactionAsync();
        user.Username = username;
        user.Email = $"{username}@deleted.invalid";
        user.FirstName = "Deleted";
        user.LastName = "user";
        user.PhoneNumber = "";
        user.Address = "";
        user.Password = null;
        user.Status = User.Inactive;
        user.EmailConfirmedAt = null;
        user.EmailCodes = false;
        user.LeaderboardHidden = true;
        await db.SaveChangesAsync();

        await SessionEndpoints.RevokeAll(db, id, now);
        await db.PasskeyCredentials.Where(p => p.UserId == id).ExecuteDeleteAsync();
        await db.SignInCodes.Where(c => c.UserId == id).ExecuteDeleteAsync();
        await db.EmailConfirmations.Where(c => c.UserId == id).ExecuteDeleteAsync();
        await db.PasswordResets.Where(r => r.UserId == id).ExecuteDeleteAsync();
        await db.NotificationPreferences.Where(p => p.UserId == id).ExecuteDeleteAsync();
        await db.WishlistItems.Where(w => w.UserId == id).ExecuteDeleteAsync();
        await db.NotificationsHistories.Where(n => n.UserId == id).ExecuteDeleteAsync();
        // only the open cart goes
        await db.CartItems.Where(c => c.UserId == id && c.InCart && !c.WasOrdered && c.PointsHistoryId == null).ExecuteDeleteAsync();
        // old snapshots still hold the name, email and password hash
        await db.AccountsHistories.Where(h => h.UserId == id).ExecuteUpdateAsync(h => h
            .SetProperty(x => x.Username, username)
            .SetProperty(x => x.Email, user.Email)
            .SetProperty(x => x.Password, "")
            .SetProperty(x => x.FirstName, user.FirstName)
            .SetProperty(x => x.LastName, user.LastName)
            .SetProperty(x => x.PhoneNumber, (string?)null)
            .SetProperty(x => x.Address, (string?)null));
        await db.LoginAttempts.Where(a => a.UserId == id).ExecuteUpdateAsync(a => a.SetProperty(x => x.Username, username));
        await db.LockoutResets.Where(r => r.UserId == id).ExecuteUpdateAsync(r => r.SetProperty(x => x.Username, username));
        await transaction.CommitAsync();
    }
}

public record DeleteAccountRequest(string? Confirmation);
