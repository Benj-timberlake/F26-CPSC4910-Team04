using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public static class CartEndpoints
{
    public static void MapCartEndpoints(this WebApplication app)
    {
        app.MapPost("/users/{id:int}/cart/send-order", async (int id, AppDbContext db, TimeProvider clock) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            // Lock this user's balance before reading the cart so duplicate submissions
            // cannot both spend the same points. All changes commit or roll back together.
            await db.Users.Where(user => user.Id == id)
                .ExecuteUpdateAsync(update => update.SetProperty(user => user.Points, user => user.Points));
            var user = await db.Users.SingleOrDefaultAsync(user => user.Id == id);
            if (user is null)
                return Results.NotFound();

            var items = await db.CartItems
                .Where(item => item.UserId == id && item.InCart && !item.WasOrdered).ToListAsync();
            if (items.Count == 0)
                return Results.BadRequest(new { message = "Your cart is empty." });
            if (items.Any(item => item.Price < 0 || decimal.Round(item.Price, 2) != item.Price || item.Quantity < 1))
                return Results.BadRequest(new { message = "Your cart contains an invalid price or quantity." });

            // Stored prices are USD; one cent is one point. Use decimal before the
            // balance comparison so large carts cannot overflow the integer balance.
            var totalPoints = items.Sum(item => item.Price * 100m * item.Quantity);
            if (totalPoints > user.Points)
                return Results.BadRequest(new { message = $"Not enough points. Your order costs {totalPoints:N0} points, but you have {user.Points:N0} points." });

            user.Points -= (int)totalPoints;
            var history = new PointsHistory
            {
                UserId = id, PointsDelta = -(int)totalPoints,
                Timestamp = clock.GetUtcNow().UtcDateTime
            };
            db.PointsHistory.Add(history);
            foreach (var item in items)
            {
                item.InCart = false;
                item.WasOrdered = true;
                item.PointsHistory = history;
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.NoContent();
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
                .Where(item => item.UserId == id && item.InCart && !item.WasOrdered)
                .OrderBy(item => item.Id)
                .Select(item => new CartItemDetails(item.Id, item.Name, item.Price, item.Description, item.Quantity))
                .ToListAsync();

            return Results.Ok(items);
        });

        app.MapPut("/users/{id:int}/cart/{itemId:long}/quantity", async (int id, long itemId, UpdateCartQuantity request, AppDbContext db) =>
        {
            if (request.Quantity < 1 || request.Quantity > 999)
                return Results.BadRequest(new { message = "Quantity must be between 1 and 999." });
            var changed = await db.CartItems
                .Where(item => item.UserId == id && item.Id == itemId && item.InCart && !item.WasOrdered)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.Quantity, request.Quantity));
            return changed == 0 ? Results.NotFound() : Results.NoContent();
        });

        app.MapDelete("/users/{id:int}/cart/{itemId:long}", async (int id, long itemId, AppDbContext db) =>
        {
            var removed = await db.CartItems
                .Where(item => item.UserId == id && item.Id == itemId && item.InCart && !item.WasOrdered)
                .ExecuteDeleteAsync();
            return removed == 0 ? Results.NotFound() : Results.NoContent();
        });
    }
}

// Price in cart requests/responses is USD per item, not reward points.
public record CartItemDetails(uint Id, string Name, decimal Price, string? Description, int Quantity = 1);
public record UpdateCartQuantity(int Quantity);
public record AddCartItem(string Name, decimal Price, string? Description);
