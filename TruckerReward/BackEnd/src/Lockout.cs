using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// MaxFailures wrong passwords in a row lock a username for Duration. derived from login_attempts,
// keyed by username so unknown names lock the same way as real ones.
public static class Lockout
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(3);

    // replays the recent attempts oldest first. attempts made while locked are skipped so the lock
    // ends Duration after it started no matter how many more come in.
    public static async Task<DateTime?> LockedUntil(AppDbContext db, string username, DateTime now)
    {
        var attempts = await db.LoginAttempts.AsNoTracking()
            .Where(a => a.Username == username && a.AttemptedAt > now - Duration - Duration)
            .OrderBy(a => a.AttemptedAt).ThenBy(a => a.Id)
            .Select(a => new { a.Succeeded, a.AttemptedAt })
            .ToListAsync();

        DateTime? lockedUntil = null;
        var streak = 0;
        var streakStart = DateTime.MinValue;
        foreach (var a in attempts)
        {
            if (a.AttemptedAt < lockedUntil)
                continue;
            if (a.Succeeded)
            {
                streak = 0;
                continue;
            }
            if (streak == 0 || a.AttemptedAt - streakStart > Duration)
            {
                streakStart = a.AttemptedAt;
                streak = 0;
            }
            if (++streak == MaxFailures)
            {
                lockedUntil = a.AttemptedAt + Duration;
                streak = 0;
            }
        }
        return lockedUntil > now ? lockedUntil : null;
    }

    public static async Task<List<LockedAccount>> All(AppDbContext db, DateTime now)
    {
        var since = now - Duration;
        var names = await db.LoginAttempts.AsNoTracking()
            .Where(a => a.AttemptedAt > since && !a.Succeeded)
            .GroupBy(a => a.Username)
            .Where(g => g.Count() >= MaxFailures)
            .Select(g => g.Key)
            .ToListAsync();
        var locked = new List<LockedAccount>();
        foreach (var name in names)
            if (await LockedUntil(db, name, now) is { } until)
                locked.Add(new LockedAccount(name, until));
        return locked.OrderBy(l => l.LockedUntil).ToList();
    }
}

public record LockedAccount(string Username, DateTime LockedUntil);
