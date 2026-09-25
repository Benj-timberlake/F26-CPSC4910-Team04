using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public static class CartEndpoints
{
    public static void MapCartEndpoints(this WebApplication app)
    {
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
            return Results.Created($"/users/{id}/cart", new CartItemDetails(item.Id, item.Name, item.Price, item.Description));
        });

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
public record AddCartItem(string Name, decimal Price, string? Description);
