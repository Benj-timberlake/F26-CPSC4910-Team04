using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// admin uptime from UptimeService's minute checks
public static class UptimeEndpoints
{
    public const double Target = 95;

    public static void MapUptimeEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/uptime", async (AppDbContext db, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var failures = await db.UptimeChecks.AsNoTracking().Where(c => !c.Ok)
                .OrderByDescending(c => c.CheckedAt).ThenByDescending(c => c.Id).Take(20)
                .Select(c => new UptimeFailure(c.CheckedAt, c.LatencyMs, c.Error))
                .ToListAsync();
            return Results.Ok(new UptimeReport(Target,
                await Window(db, now, TimeSpan.FromHours(24)),
                await Window(db, now, TimeSpan.FromDays(7)),
                await Window(db, now, TimeSpan.FromDays(30)),
                failures));
        });
    }

    // a minute with no sample counts as down.
    // time before the first sample ever recorded is left out
    public static async Task<UptimeWindow> Window(AppDbContext db, DateTime now, TimeSpan span)
    {
        var first = await db.UptimeChecks.MinAsync(c => (DateTime?)c.CheckedAt);
        if (first is null)
            return new UptimeWindow(null, 0, 0);
        var start = now - span;
        if (first > start)
            start = first.Value;
        var expected = Math.Max(1, (int)(now - start).TotalMinutes + 1);
        var up = await db.UptimeChecks.CountAsync(c => c.Ok && c.CheckedAt >= start && c.CheckedAt <= now);
        up = Math.Min(up, expected);
        return new UptimeWindow(Math.Round(100.0 * up / expected, 2), expected, up);
    }
}

public record UptimeWindow(double? Percent, int Expected, int Up);

public record UptimeFailure(DateTime CheckedAt, int LatencyMs, string? Error);

public record UptimeReport(double Target, UptimeWindow Day, UptimeWindow Week, UptimeWindow Month, List<UptimeFailure> Failures);
