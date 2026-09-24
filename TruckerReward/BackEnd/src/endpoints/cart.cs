using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public static class CartEndpoints
{
    public static void MapCartEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/cart", async (int id, AppDbContext db) =>
        {
            if (!await db.Users.AnyAsync(user => user.Id == id))
                return Results.NotFound();

            var items = await db.CartItems.AsNoTracking()
                .Where(item => item.UserId == id)
                .OrderBy(item => item.Id)
                .Select(item => new CartItemDetails(item.Id, item.Name, item.Price, item.Description))
                .ToListAsync();

            return Results.Ok(items);
        });
    }
}

public record CartItemDetails(uint Id, string Name, decimal Price, string? Description);
