using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// the devices page: each sign in records a session the user can later end
public static class SessionEndpoints
{
    // the frontend cookie slides for 8 hours, so anything quieter than that is already signed out
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromHours(8);

    public static void MapSessionEndpoints(this WebApplication app)
    {
        app.MapPost("/users/{id:int}/sessions", async (int id, StartSessionRequest req, AppDbContext db, TimeProvider clock) =>
        {
            if (!await db.Users.AnyAsync(u => u.Id == id))
                return Results.NotFound();
            var now = clock.GetUtcNow().UtcDateTime;
            var key = Tokens.New();
            db.UserSessions.Add(new UserSession
            {
                UserId = id, KeyHash = Tokens.Hash(key), CreatedAt = now, LastSeenAt = now,
                IpAddress = Clip(req.IpAddress, 45), UserAgent = Clip(req.UserAgent, 255)
            });
            await db.SaveChangesAsync();
            return Results.Ok(new { key });
        });

        // called on each cookie check, so it also keeps last seen fresh
        app.MapPost("/sessions/check", async (SessionKeyRequest req, AppDbContext db, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var hash = Tokens.Hash(req.Key ?? "");
            var touched = await db.UserSessions.Where(s => s.KeyHash == hash && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.LastSeenAt, now)
                    .SetProperty(x => x.IpAddress, x => req.IpAddress == null ? x.IpAddress : req.IpAddress));
            return touched == 0 ? Results.StatusCode(StatusCodes.Status410Gone) : Results.NoContent();
        });

        app.MapPost("/sessions/end", async (SessionKeyRequest req, AppDbContext db, TimeProvider clock) =>
        {
            var hash = Tokens.Hash(req.Key ?? "");
            await db.UserSessions.Where(s => s.KeyHash == hash && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow().UtcDateTime));
            return Results.NoContent();
        });

        app.MapGet("/users/{id:int}/sessions", async (int id, string? current, AppDbContext db, TimeProvider clock) =>
        {
            var since = clock.GetUtcNow().UtcDateTime - ActiveWindow;
            var currentHash = current is null ? null : Tokens.Hash(current);
            var sessions = await db.UserSessions.AsNoTracking()
                .Where(s => s.UserId == id && s.RevokedAt == null && s.LastSeenAt > since)
                .OrderByDescending(s => s.LastSeenAt).ThenByDescending(s => s.Id)
                .ToListAsync();
            return Results.Ok(sessions.Select(s => new SessionEntry(s.Id, s.CreatedAt, s.LastSeenAt, s.IpAddress, Device(s.UserAgent), s.KeyHash == currentHash)));
        });

        app.MapDelete("/users/{id:int}/sessions/{sessionId:int}", async (int id, int sessionId, AppDbContext db, TimeProvider clock) =>
        {
            var ended = await db.UserSessions.Where(s => s.Id == sessionId && s.UserId == id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow().UtcDateTime));
            return ended == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    // "Chrome on macOS" from a user agent, good enough to tell devices apart
    public static string Device(string? userAgent)
    {
        var ua = userAgent ?? "";
        var browser = ua.Contains("Edg/") ? "Edge"
            : ua.Contains("Firefox/") ? "Firefox"
            : ua.Contains("Chrome/") || ua.Contains("CriOS/") ? "Chrome"
            : ua.Contains("Safari/") ? "Safari"
            : "Unknown browser";
        var os = ua.Contains("iPhone") ? "iPhone"
            : ua.Contains("iPad") ? "iPad"
            : ua.Contains("Android") ? "Android"
            : ua.Contains("Windows") ? "Windows"
            : ua.Contains("Mac OS X") ? "macOS"
            : ua.Contains("Linux") ? "Linux"
            : null;
        return os is null ? browser : $"{browser} on {os}";
    }

    // old cookies fail their next check. keepKey is the browser that asked, so it stays signed in
    internal static async Task<int> RevokeAll(AppDbContext db, int userId, DateTime now, string? keepKey = null)
    {
        var changed = await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.SessionVersion, x => x.SessionVersion + 1));
        var keep = keepKey is null ? null : Tokens.Hash(keepKey);
        await db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null && s.KeyHash != keep)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
        return changed;
    }

    private static string? Clip(string? value, int max) =>
        value is null ? null : value.Length > max ? value[..max] : value;
}

public record StartSessionRequest(string? IpAddress, string? UserAgent);

public record SessionKeyRequest(string? Key, string? IpAddress = null);

public record SessionEntry(int Id, DateTime CreatedAt, DateTime LastSeenAt, string? IpAddress, string Device, bool Current);
