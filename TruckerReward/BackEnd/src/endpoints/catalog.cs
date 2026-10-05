using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/catalog", async (int id, AppDbContext db) =>
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id);
            if (user is null) return Results.NotFound();
            if (user.UserType != "driver")
                return Results.Json(new { message = "Only drivers can browse this catalog." }, statusCode: 403);
            if (user.CompanyId is null) return Results.Ok(Array.Empty<string>());
            var itemIds = await db.CatalogItems.AsNoTracking()
                .Where(item => db.Users.Any(sponsor => sponsor.Id == item.SponsorId
                    && sponsor.UserType == "sponsor" && sponsor.CompanyId == user.CompanyId))
                .OrderBy(item => item.Id).Select(item => item.ItemId).Distinct().ToListAsync();
            return Results.Ok(itemIds);
        });
        app.MapPost("/users/{id:int}/catalog", async (int id, AddCatalogItem request, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.ItemId) || request.ItemId.Length > 255)
                return Results.BadRequest(new { message = "Invalid item ID." });
            var itemId = request.ItemId.Trim();
            var sponsor = await db.Users.FindAsync(id);
            if (sponsor is null) return Results.NotFound();
            if (sponsor.UserType != "sponsor")
                return Results.Json(new { message = "Only sponsors can add catalog items." }, statusCode: 403);
            if (await db.CatalogItems.AnyAsync(item => item.SponsorId == id && item.ItemId == itemId))
                return Results.NoContent();
            db.CatalogItems.Add(new CatalogItem { SponsorId = id, ItemId = itemId });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}

public record AddCatalogItem(string ItemId);
