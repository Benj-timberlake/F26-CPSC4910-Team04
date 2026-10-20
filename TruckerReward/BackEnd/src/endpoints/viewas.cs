using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// lets admins view the site as another user. sponsors use the driver view instead
public static class ViewAsEndpoints
{
    public static void MapViewAsEndpoints(this WebApplication app)
    {
        app.MapPost("/users/{viewerId:int}/view-as/{userId:int}", async (int viewerId, int userId, ViewAsRequest req, AppDbContext db, Notifications notifications, TimeProvider clock) =>
        {
            var viewer = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == viewerId && u.Status == User.Active);
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.Status == User.Active);
            if (viewer is null || user is null)
                return Results.NotFound();
            if (!Allowed(viewer, user))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            db.Impersonations.Add(new Impersonation { ViewerId = viewer.Id, UserId = user.Id, StartedAt = clock.GetUtcNow().UtcDateTime, IpAddress = req.IpAddress });
            await db.SaveChangesAsync();
            await notifications.AccountViewed(user, viewer);
            return Results.Ok(UserProfile.Of(user));
        });
    }

    internal static bool Allowed(User viewer, User user) =>
        viewer.Id != user.Id && viewer.UserType == AuthEndpoints.Admin && user.UserType != AuthEndpoints.Admin;
}

public record ViewAsRequest(string? IpAddress);
