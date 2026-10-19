using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// one method per notification, each saved to notifications_history
public sealed class Notifications(AppDbContext db, IEmailSender email, TimeProvider clock, ILogger<Notifications> log)
{
    public Task ResetLinkSent(User user, string link) =>
        Send(user, "Reset your TruckerReward password",
            $"Hi {user.Username},\n\nUse this link within the next hour to choose a new password:\n{link}\n\nIf you didn't ask for this, ignore this email and your password stays the same.",
            null, emailed: true,
            // keeps the reset link out of the database
            stored: "A link to reset your password was emailed to you. It works for one hour.");

    public Task PasswordChanged(User user, bool byReset) => byReset
        ? Send(user, "Your TruckerReward password was reset",
            $"Hi {user.Username},\n\nYour password was just reset. If that wasn't you, contact your sponsor or an administrator.",
            null, emailed: true)
        : Send(user, "Your TruckerReward password was changed",
            $"Hi {user.Username},\n\nYour password was just changed. If that wasn't you, reset it right away from the login page.",
            null, emailed: true);

    public Task AccountCreated(User user) =>
        ToAdmins($"New {user.UserType} account: {user.Username}",
            $"{user.FirstName} {user.LastName} ({user.Username}, {user.Email}) created a {user.UserType} account.",
            NotificationCategory.NewAccounts);

    public Task AccountLocked(string username, string? ip, DateTime until) =>
        ToAdmins($"Account locked: {username}",
            $"{Lockout.MaxFailures} failed sign-in attempts in a row for {username} from {ip ?? "an unknown address"}. The account is locked until {until:u}.",
            NotificationCategory.SecurityAlerts);

    public Task ResetRequested(User user, string? ip) =>
        ToAdmins($"Password reset requested for {user.Username}",
            $"A password reset was requested for {user.Username} ({user.Email}) from {ip ?? "an unknown address"}.",
            NotificationCategory.SecurityAlerts);

    public Task AccountStatusChanged(User account, User admin) =>
        ToAdmins($"Account {(account.Status == User.Active ? "reactivated" : "deactivated")}: {account.Username}",
            $"{admin.Username} {(account.Status == User.Active ? "reactivated" : "deactivated")} the {account.UserType} account {account.Username} ({account.Email}).",
            NotificationCategory.AccountStatus);

    public async Task CompanyApplicationSubmitted(User applicant, int companyId, string companyName)
    {
        const string subject = "Company Application";
        var applicantIdentity = FormatIdentity(applicant);
        var reviewerBody = $"A {applicant.UserType} has applied to {companyName}. Please review the application.\n\nSubmitted by: {applicantIdentity}";
        var admins = await db.Users.AsNoTracking().Where(u => u.UserType == AuthEndpoints.Admin).ToListAsync();
        foreach (var admin in admins)
            await Send(admin, subject, reviewerBody, category: null, emailed: false);

        var sponsors = await db.Users.AsNoTracking()
            .Where(u => u.UserType == AuthEndpoints.Sponsor && u.CompanyId == companyId)
            .ToListAsync();
        foreach (var sponsor in sponsors)
            await Send(sponsor, subject, reviewerBody, category: null, emailed: false);

        await Send(applicant, subject,
            $"We received your application to {companyName}. We'll let you know when it's reviewed.\n\nSubmitted by: You",
            category: null, emailed: false);
    }

    public async Task CompanyApplicationCanceled(User applicant, int companyId, string companyName, string reasoning)
    {
        var subject = $"Company Application Canceled: {applicant.FirstName} {applicant.LastName}";
        var identity = FormatIdentity(applicant);
        var reviewerBody = WithReasoning(
            $"The {applicant.UserType}'s application to {companyName} was canceled.\n\nCanceled by: {identity}", reasoning);
        var admins = await db.Users.AsNoTracking().Where(u => u.UserType == AuthEndpoints.Admin).ToListAsync();
        foreach (var admin in admins)
            await Send(admin, subject, reviewerBody, category: null, emailed: false);

        var sponsors = await db.Users.AsNoTracking()
            .Where(u => u.UserType == AuthEndpoints.Sponsor && u.CompanyId == companyId)
            .ToListAsync();
        foreach (var sponsor in sponsors)
            await Send(sponsor, subject, reviewerBody, category: null, emailed: false);

        await Send(applicant, subject,
            WithReasoning($"Your application to {companyName} has been canceled.\n\nCanceled by: You", reasoning),
            category: null, emailed: false);
    }

    public Task CompanyApplicationReviewed(User applicant, User reviewer, string previous, string decision, string companyName, string reasoning)
    {
        var action = decision == "active" ? "reverted" : decision == "approved" ? "approved" : "denied";
        var subject = $"Company Application {TitleCase(action)}: {applicant.FirstName} {applicant.LastName}";
        var message = decision switch
        {
            "approved" => $"Your application to {companyName} was approved.",
            "rejected" => $"Your application to {companyName} was not approved.",
            _ when previous == "approved" => $"Your approval for {companyName} was reversed, and your application is back under review.",
            _ => $"Your application to {companyName} is back under review."
        };
        var body = $"{message}\n\nReviewed by: {FormatIdentity(reviewer)}\nReason given: {reasoning}";
        return Send(applicant, subject, body, NotificationCategory.Applications);
    }

    private static string FormatIdentity(User user) =>
        $"{TitleCase(user.UserType)}, {user.FirstName} {user.LastName} ({user.Username}, {user.Email})";
    private static string TitleCase(string value) => char.ToUpperInvariant(value[0]) + value[1..];
    private static string WithReasoning(string body, string? reasoning) =>
        string.IsNullOrWhiteSpace(reasoning) ? body : $"{body}\nReason given: {reasoning}";

    public async Task PointsChanged(User user, int delta, string reason)
    {
        await Send(user, delta >= 0 ? $"{delta:N0} points added" : $"{-delta:N0} points deducted",
            $"{reason} Your balance is now {user.Points:N0} points.",
            NotificationCategory.Points);
        if (user.Points < 0)
            await ToSponsorsOf(user.CompanyId, $"{user.Username} is negative on points",
                $"{user.FirstName} {user.LastName} ({user.Username}) has a balance of {user.Points:N0} points.",
                NotificationCategory.NegativeBalances);
    }

    public Task DriverPurchased(User driver, IReadOnlyList<CartItem> items, int points) =>
        ToSponsorsOf(driver.CompanyId, $"{driver.Username} placed an order",
            $"{driver.FirstName} {driver.LastName} ({driver.Username}) spent {points:N0} points on " +
            string.Join(", ", items.Select(i => i.Quantity > 1 ? $"{i.Name} x{i.Quantity}" : i.Name)) + ".",
            NotificationCategory.Purchases);

    public Task DriverRefunded(User driver, IReadOnlyList<CartItem> items, int points) =>
        ToSponsorsOf(driver.CompanyId, $"{driver.Username} refunded an order",
            $"{driver.FirstName} {driver.LastName} ({driver.Username}) got {points:N0} points back for " +
            string.Join(", ", items.Select(i => i.Quantity > 1 ? $"{i.Name} x{i.Quantity}" : i.Name)) + ".",
            NotificationCategory.Purchases);

    private async Task<string> CompanyName(int companyId) =>
        await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync() ?? "the company";

    private async Task ToAdmins(string subject, string body, NotificationCategory? category)
    {
        var admins = await db.Users.AsNoTracking().Where(u => u.UserType == AuthEndpoints.Admin).ToListAsync();
        foreach (var admin in admins)
            await Send(admin, subject, body, category);
    }

    // a driver's sponsors are the sponsor accounts in the driver's company
    private async Task ToSponsorsOf(int? companyId, string subject, string body, NotificationCategory? category)
    {
        if (companyId is null)
            return;
        var sponsors = await db.Users.AsNoTracking()
            .Where(u => u.UserType == AuthEndpoints.Sponsor && u.CompanyId == companyId)
            .ToListAsync();
        foreach (var sponsor in sponsors)
            await Send(sponsor, subject, body, category);
    }

    private Task Send(User user, string subject, string body, NotificationCategory? category) =>
        Send(user, subject, body, category, category?.Emailed ?? false);

    // no category means it can't be switched off
    private async Task Send(User user, string subject, string body, NotificationCategory? category, bool emailed, string? stored = null)
    {
        if (category is not null)
        {
            try
            {
                var pref = await db.NotificationPreferences.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.UserId == user.Id && p.Category == category.Key);
                if (pref is { Enabled: false })
                    return;
                emailed = pref?.Emailed ?? emailed;
            }
            catch (System.Data.Common.DbException ex)
            {
                // settings that can't be read fall back to the defaults
                log.LogError(ex, "could not read notification settings for user {Id}", user.Id);
            }
        }

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
