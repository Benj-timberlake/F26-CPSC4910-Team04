using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// the site-wide banner admins set and clear
public static class BannerEndpoints
{
    public const int MaxLength = 500;
    public static readonly string[] Levels = ["info", "warning"];

    public static void MapBannerEndpoints(this WebApplication app)
    {
        app.MapGet("/site-banner", async (AppDbContext db) =>
            await db.SiteBanners.AsNoTracking().Where(b => b.ClearedAt == null)
                .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
                .Select(b => new BannerEntry(b.Message, b.Level, b.CreatedAt))
                .FirstOrDefaultAsync() is { } banner ? Results.Ok(banner) : Results.NoContent());

        app.MapPut("/admin/site-banner", async (SetBannerRequest req, AppDbContext db, TimeProvider clock) =>
        {
            if (!await AdminEndpoints.IsAdmin(db, req.AdminId))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var message = req.Message?.Trim() ?? "";
            if (message.Length is 0 or > MaxLength)
                return Results.BadRequest(new { message = $"Write a message of 1 to {MaxLength} characters." });
            if (req.Level is null || !Levels.Contains(req.Level))
                return Results.BadRequest(new { message = "The level has to be info or warning." });

            var now = clock.GetUtcNow().UtcDateTime;
            await db.SiteBanners.Where(b => b.ClearedAt == null).ExecuteUpdateAsync(s => s.SetProperty(b => b.ClearedAt, now));
            db.SiteBanners.Add(new SiteBanner { Message = message, Level = req.Level, CreatedAt = now, SetBy = req.AdminId });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        app.MapPost("/admin/site-banner/clear", async (ClearBannerRequest req, AppDbContext db, TimeProvider clock) =>
        {
            if (!await AdminEndpoints.IsAdmin(db, req.AdminId))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var now = clock.GetUtcNow().UtcDateTime;
            await db.SiteBanners.Where(b => b.ClearedAt == null).ExecuteUpdateAsync(s => s.SetProperty(b => b.ClearedAt, now));
            return Results.NoContent();
        });
    }
}

public record BannerEntry(string Message, string Level, DateTime CreatedAt);

public record SetBannerRequest(int AdminId, string? Message, string? Level);

public record ClearBannerRequest(int AdminId);
