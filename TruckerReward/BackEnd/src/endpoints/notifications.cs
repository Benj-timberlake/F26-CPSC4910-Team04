using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// what the notification bell and page read
public static class NotificationEndpoints
{
    public const int MaxShown = 50;

    public static void MapNotificationEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/notifications", async (int id, AppDbContext db) =>
        {
            if (!await db.Users.AnyAsync(u => u.Id == id))
                return Results.NotFound();
            return Results.Ok(await db.NotificationsHistories.AsNoTracking()
                .Where(n => n.UserId == id)
                .OrderByDescending(n => n.Timestamp).ThenByDescending(n => n.Id)
                .Take(MaxShown)
                .Select(n => new NotificationEntry(n.Id, n.Subject, n.Body, n.Timestamp, n.BeenRead))
                .ToListAsync());
        });

        app.MapGet("/users/{id:int}/notifications/unread", async (int id, AppDbContext db) =>
            Results.Ok(new UnreadNotifications(await db.NotificationsHistories.CountAsync(n => n.UserId == id && !n.BeenRead))));

        app.MapPost("/users/{id:int}/notifications/read", async (int id, AppDbContext db) =>
        {
            await db.NotificationsHistories
                .Where(n => n.UserId == id && !n.BeenRead)
                .ExecuteUpdateAsync(update => update.SetProperty(n => n.BeenRead, true));
            return Results.NoContent();
        });

        app.MapGet("/users/{id:int}/notification-settings", async (int id, AppDbContext db) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
                return Results.NotFound();
            var saved = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == id).ToListAsync();
            return Results.Ok(NotificationCategory.For(user.UserType).Select(c =>
            {
                var pref = saved.FirstOrDefault(p => p.Category == c.Key);
                return new NotificationSetting(c.Key, c.Label, pref?.Enabled ?? true, pref?.Emailed ?? c.Emailed);
            }).ToList());
        });

        app.MapPut("/users/{id:int}/notification-settings", async (int id, List<NotificationSettingUpdate> updates, AppDbContext db) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
                return Results.NotFound();
            var allowed = NotificationCategory.For(user.UserType).Select(c => c.Key).ToHashSet();
            if (updates.Any(u => !allowed.Contains(u.Category)))
                return Results.BadRequest(new { message = "Unknown notification category." });

            var saved = await db.NotificationPreferences.Where(p => p.UserId == id).ToListAsync();
            foreach (var update in updates)
            {
                var pref = saved.FirstOrDefault(p => p.Category == update.Category);
                if (pref is null)
                {
                    pref = new NotificationPreference { UserId = id, Category = update.Category };
                    db.NotificationPreferences.Add(pref);
                    saved.Add(pref);
                }
                pref.Enabled = update.Enabled;
                pref.Emailed = update.Emailed;
            }
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}

public record NotificationSetting(string Category, string Label, bool Enabled, bool Emailed);

public record NotificationSettingUpdate(string Category, bool Enabled, bool Emailed);

public record NotificationEntry(int Id, string Subject, string Body, DateTime Timestamp, bool Read);

public record UnreadNotifications(int Count);
