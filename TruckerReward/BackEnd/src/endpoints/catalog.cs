using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
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
