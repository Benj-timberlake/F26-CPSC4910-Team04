using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/wishlist", async (int id, AppDbContext db) =>
        {
            var driver = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
            if (driver is null) return Results.NotFound();
            if (driver.UserType != "driver") return Results.StatusCode(403);
            var items = await db.WishlistItems.AsNoTracking().Where(w => w.UserId == id
                && db.CatalogItems.Any(c => c.ItemId == w.ItemId && db.Users.Any(s => s.Id == c.SponsorId
                    && s.UserType == "sponsor" && s.CompanyId == driver.CompanyId && driver.CompanyId != null)))
                .OrderBy(w => w.ItemId).Select(w => w.ItemId).ToListAsync();
            return Results.Ok(items);
        });
        app.MapPut("/users/{id:int}/wishlist", async (int id, UpdateWishlist request, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.ItemId) || request.ItemId.Length > 255) return Results.BadRequest();
            var driver = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
            if (driver is null) return Results.NotFound();
            if (driver.UserType != "driver") return Results.StatusCode(403);
            if (!request.Saved)
            {
                await db.WishlistItems.Where(w => w.UserId == id && w.ItemId == request.ItemId).ExecuteDeleteAsync();
                return Results.NoContent();
            }
            if (driver.CompanyId == null || !await db.CatalogItems.AnyAsync(c => c.ItemId == request.ItemId
                && db.Users.Any(s => s.Id == c.SponsorId && s.UserType == "sponsor" && s.CompanyId == driver.CompanyId)))
                return Results.NotFound();
            // Serialize saves for this driver so repeat requests cannot create duplicate favorites.
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(set => set.SetProperty(u => u.Points, u => u.Points));
            if (!await db.WishlistItems.AnyAsync(w => w.UserId == id && w.ItemId == request.ItemId))
            {
                db.WishlistItems.Add(new WishlistItem { UserId = id, ItemId = request.ItemId });
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            return Results.NoContent();
        });
        app.MapGet("/users/{id:int}/catalog", async (int id, AppDbContext db) =>
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id);
            if (user is null) return Results.NotFound();
            if (user.UserType == "sponsor")
                return Results.Ok(await db.CatalogItems.AsNoTracking().Where(item => item.SponsorId == id)
                    .OrderBy(item => item.Id).Select(item => item.ItemId).Distinct().ToListAsync());
            if (user.UserType != "driver")
                return Results.Json(new { message = "Only drivers can browse this catalog." }, statusCode: 403);
            var companyId = await CompanyMembershipQueries.FirstCompanyId(db, id);
            if (companyId is null) return Results.Ok(Array.Empty<string>());
            var itemIds = await db.CatalogItems.AsNoTracking()
                .Where(item => db.Users.Any(sponsor => sponsor.Id == item.SponsorId
                    && sponsor.UserType == "sponsor" && db.AccountsCompanies
                        .Any(membership => membership.AccountId == sponsor.Id && membership.CompanyId == companyId)))
                .OrderBy(item => item.Id).Select(item => item.ItemId).Distinct().ToListAsync();
            return Results.Ok(itemIds);
        });
        app.MapDelete("/users/{id:int}/catalog", async (int id, string itemId, AppDbContext db) =>
        {
            var sponsor = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id);
            if (sponsor is null) return Results.NotFound();
            if (sponsor.UserType != "sponsor")
                return Results.Json(new { message = "Only sponsors can remove catalog items." }, statusCode: 403);
            var removed = await db.CatalogItems.Where(item => item.SponsorId == id && item.ItemId == itemId)
                .ExecuteDeleteAsync();
            return removed == 0 ? Results.NotFound() : Results.NoContent();
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

public record UpdateWishlist(string ItemId, bool Saved);
