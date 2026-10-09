using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// one method per notification, each saved to notifications_history
public sealed class Notifications(AppDbContext db, IEmailSender email, TimeProvider clock, ILogger<Notifications> log)
{
    public Task ResetLinkSent(User user, string link) =>
        Send(user, "Reset your TruckerReward password",
            $"Hi {user.Username},\n\nUse this link within the next hour to choose a new password:\n{link}\n\nIf you didn't ask for this, ignore this email and your password stays the same.",
            emailed: true,
            // keeps the reset link out of the database
            stored: "A link to reset your password was emailed to you. It works for one hour.");

    public Task PasswordChanged(User user, bool byReset) => byReset
        ? Send(user, "Your TruckerReward password was reset",
            $"Hi {user.Username},\n\nYour password was just reset. If that wasn't you, contact your sponsor or an administrator.",
            emailed: true)
        : Send(user, "Your TruckerReward password was changed",
            $"Hi {user.Username},\n\nYour password was just changed. If that wasn't you, reset it right away from the login page.",
            emailed: true);

    public Task AccountCreated(User user) =>
        ToAdmins($"New {user.UserType} account: {user.Username}",
            $"{user.FirstName} {user.LastName} ({user.Username}, {user.Email}) created a {user.UserType} account.",
            emailed: false);

    public Task AccountLocked(string username, string? ip, DateTime until) =>
        ToAdmins($"Account locked: {username}",
            $"{Lockout.MaxFailures} failed sign-in attempts in a row for {username} from {ip ?? "an unknown address"}. The account is locked until {until:u}.",
            emailed: true);

    public Task ResetRequested(User user, string? ip) =>
        ToAdmins($"Password reset requested for {user.Username}",
            $"A password reset was requested for {user.Username} ({user.Email}) from {ip ?? "an unknown address"}.",
            emailed: true);

    public async Task PointsChanged(User user, int delta, string reason)
    {
        await Send(user, delta >= 0 ? $"{delta:N0} points added" : $"{-delta:N0} points deducted",
            $"{reason} Your balance is now {user.Points:N0} points.",
            emailed: false);
        if (user.Points < 0)
            await ToSponsorsOf(user.CompanyId, $"{user.Username} is negative on points",
                $"{user.FirstName} {user.LastName} ({user.Username}) has a balance of {user.Points:N0} points.",
                emailed: true);
    }

    public Task DriverPurchased(User driver, IReadOnlyList<CartItem> items, int points) =>
        ToSponsorsOf(driver.CompanyId, $"{driver.Username} placed an order",
            $"{driver.FirstName} {driver.LastName} ({driver.Username}) spent {points:N0} points on " +
            string.Join(", ", items.Select(i => i.Quantity > 1 ? $"{i.Name} x{i.Quantity}" : i.Name)) + ".",
            emailed: false);

    private async Task ToAdmins(string subject, string body, bool emailed)
    {
        var admins = await db.Users.AsNoTracking().Where(u => u.UserType == AuthEndpoints.Admin).ToListAsync();
        foreach (var admin in admins)
            await Send(admin, subject, body, emailed);
    }

    // a driver's sponsors are the sponsor accounts in the driver's company
    private async Task ToSponsorsOf(int? companyId, string subject, string body, bool emailed)
    {
        if (companyId is null)
            return;
        var sponsors = await db.Users.AsNoTracking()
            .Where(u => u.UserType == AuthEndpoints.Sponsor && u.CompanyId == companyId)
            .ToListAsync();
        foreach (var sponsor in sponsors)
            await Send(sponsor, subject, body, emailed);
    }

    // stored goes in the history instead of a body with a secret in it
    private async Task Send(User user, string subject, string body, bool emailed, string? stored = null)
    {
        var row = new NotificationsHistory
        {
            UserId = user.Id,
            Subject = subject.Length > MaxSubjectLength ? subject[..MaxSubjectLength] : subject,
            Body = stored ?? body,
            Timestamp = clock.GetUtcNow().UtcDateTime
        };
        db.NotificationsHistories.Add(row);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // a lost history row shouldn't fail what triggered it
            db.Entry(row).State = EntityState.Detached;
            log.LogError(ex, "could not save notification for user {Id}: {Subject}", user.Id, subject);
        }
        if (emailed)
            await email.SendAsync(user.Email, subject, body);
    }

    // notifications_history.subject is varchar(100)
    private const int MaxSubjectLength = 100;
}
