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
    }
}

public record NotificationEntry(int Id, string Subject, string Body, DateTime Timestamp, bool Read);

public record UnreadNotifications(int Count);
