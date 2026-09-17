using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// after MaxFailures wrong passwords in a row a username is refused for Duration, counted from the
// last failure. nothing is stored beyond login_attempts: a lock is just "the last MaxFailures
// attempts within Duration were all failures". keyed by username so unknown names lock the same way.
public static class Lockout
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(3);

    // when the lock on this username lifts, or null if it isn't locked. Replays the recent attempts
    // oldest first: a success clears the streak, MaxFailures failures within Duration of each other
    // start a lock, and attempts made while a lock is on are skipped so hammering can't extend it.
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

    // every username with a live lock, for the admin page
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
