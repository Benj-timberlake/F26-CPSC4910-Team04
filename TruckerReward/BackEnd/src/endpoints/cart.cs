using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public static class CartEndpoints
{
    public static void MapCartEndpoints(this WebApplication app)
    {
        // Placeholder submission: clear the cart without placing an external order.
        app.MapPost("/users/{id:int}/cart/send-order", async (int id, AppDbContext db) =>
        {
            if (!await db.Users.AnyAsync(user => user.Id == id))
                return Results.NotFound();
            var removed = await db.CartItems.Where(item => item.UserId == id).ExecuteDeleteAsync();
            return removed == 0
                ? Results.BadRequest(new { message = "Your cart is empty." })
                : Results.NoContent();
        });

        app.MapPost("/users/{id:int}/cart", async (int id, AddCartItem request, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 255
                || request.Price < 0 || request.Price > 99999999.99m
                || decimal.Round(request.Price, 2) != request.Price
                || (request.Description is not null && System.Text.Encoding.UTF8.GetByteCount(request.Description) > 65535))
                return Results.BadRequest(new { message = "Invalid product name, price, or description." });

            if (!await db.Users.AnyAsync(user => user.Id == id))
                return Results.NotFound();

            var item = new CartItem
            {
                UserId = id, Name = request.Name, Price = request.Price, Description = request.Description
            };
            db.CartItems.Add(item);
            await db.SaveChangesAsync();
            return Results.Created($"/users/{id}/cart", new CartItemDetails(item.Id, item.Name, item.Price, item.Description, item.Quantity));
        });

        app.MapGet("/users/{id:int}/cart", async (int id, AppDbContext db) =>
        {
            if (!await db.Users.AnyAsync(user => user.Id == id))
                return Results.NotFound();

            var items = await db.CartItems.AsNoTracking()
                .Where(item => item.UserId == id)
                .OrderBy(item => item.Id)
                .Select(item => new CartItemDetails(item.Id, item.Name, item.Price, item.Description, item.Quantity))
                .ToListAsync();

            return Results.Ok(items);
        });

        app.MapPut("/users/{id:int}/cart/{itemId:long}/quantity", async (int id, long itemId, UpdateCartQuantity request, AppDbContext db) =>
        {
            if (request.Quantity < 1 || request.Quantity > 999)
                return Results.BadRequest(new { message = "Quantity must be between 1 and 999." });
            var item = await db.CartItems.FirstOrDefaultAsync(item => item.UserId == id && item.Id == itemId);
            if (item is null) return Results.NotFound();
            item.Quantity = request.Quantity;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        app.MapDelete("/users/{id:int}/cart/{itemId:long}", async (int id, long itemId, AppDbContext db) =>
        {
            var item = await db.CartItems.FirstOrDefaultAsync(item => item.UserId == id && item.Id == itemId);
            if (item is null) return Results.NotFound();
            db.CartItems.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}

public record CartItemDetails(uint Id, string Name, decimal Price, string? Description, int Quantity = 1);
public record UpdateCartQuantity(int Quantity);
public record AddCartItem(string Name, decimal Price, string? Description);
