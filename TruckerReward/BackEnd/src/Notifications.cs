using BackEnd.Models;

// user emails, each also saved to notifications_history. add a method per notification.
public sealed class Notifications(AppDbContext db, IEmailSender email, TimeProvider clock)
{
    public Task ResetLinkSent(User user, string link) =>
        Send(user, "Reset your TruckerReward password",
            $"Hi {user.Username},\n\nUse this link within the next hour to choose a new password:\n{link}\n\nIf you didn't ask for this, ignore this email and your password stays the same.",
            // the link works like a password, so it isn't stored
            stored: "A link to reset your password was emailed to you. It works for one hour.");

    public Task PasswordChanged(User user, bool byReset) => byReset
        ? Send(user, "Your TruckerReward password was reset",
            $"Hi {user.Username},\n\nYour password was just reset. If that wasn't you, contact your sponsor or an administrator.")
        : Send(user, "Your TruckerReward password was changed",
            $"Hi {user.Username},\n\nYour password was just changed. If that wasn't you, reset it right away from the login page.");

    // stored goes in the history instead of a body with a secret in it
    private async Task Send(User user, string subject, string body, string? stored = null)
    {
        db.NotificationsHistories.Add(new NotificationsHistory
        {
            UserId = user.Id,
            Subject = subject.Length > MaxSubjectLength ? subject[..MaxSubjectLength] : subject,
            Body = stored ?? body,
            Timestamp = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync();
        await email.SendAsync(user.Email, subject, body);
    }

    // notifications_history.subject is varchar(100)
    private const int MaxSubjectLength = 100;
}
