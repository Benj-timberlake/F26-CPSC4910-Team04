using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// admins view as any driver or sponsor, sponsors as their own drivers
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
        viewer.Id != user.Id && viewer.UserType switch
        {
            AuthEndpoints.Admin => user.UserType != AuthEndpoints.Admin,
            AuthEndpoints.Sponsor => viewer.CompanyId is not null && user.UserType == AuthEndpoints.Driver && user.CompanyId == viewer.CompanyId,
            _ => false
        };
}

public record ViewAsRequest(string? IpAddress);
