using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public static class CartEndpoints
{
    public static void MapCartEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/driver-orders", async (int id, AppDbContext db) =>
        {
            var sponsor = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id);
            if (sponsor is null) return Results.NotFound();
            if (sponsor.UserType != "sponsor") return Results.StatusCode(403);
            if (sponsor.CompanyId is null) return Results.Ok(Array.Empty<SponsorOrderDetails>());
            var items = await db.CartItems.AsNoTracking().Include(item => item.User).Include(item => item.PointsHistory)
                .Where(item => item.User.UserType == "driver" && item.User.CompanyId == sponsor.CompanyId
                    && !item.WasOrdered && !item.InCart && item.PointsHistory != null
                    && item.PointsHistory.UserId == item.UserId).ToListAsync();
            return Results.Ok(items.GroupBy(item => item.PointsHistoryId!.Value).Select(group =>
            {
                var first = group.First();
                return new SponsorOrderDetails(group.Key, first.UserId,
                    first.User.FirstName + " " + first.User.LastName, first.PointsHistory!.Timestamp,
                    group.Sum(item => item.Price * item.Quantity),
                    group.Select(item => new CartItemDetails(item.Id, item.Name, item.Price, item.Description, item.Quantity)).ToList());
            }).OrderByDescending(order => order.Timestamp).ThenByDescending(order => order.OrderId));
        });
        app.MapPost("/users/{id:int}/driver-orders/{orderId:int}/buy", async (int id, int orderId, AppDbContext db) =>
        {
            var sponsor = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id);
            if (sponsor is null) return Results.NotFound();
            if (sponsor.UserType != "sponsor") return Results.StatusCode(403);
            if (sponsor.CompanyId is null) return Results.NotFound();
            // Materialize IDs before updating: MySQL forbids reading the target
            // cart table from a subquery inside the same UPDATE statement.
            var eligible = await db.CartItems.Where(item => item.PointsHistoryId == orderId
                    && !item.InCart && !item.WasOrdered && item.User.UserType == "driver"
                    && item.User.CompanyId == sponsor.CompanyId && item.PointsHistory != null
                    && item.PointsHistory.UserId == item.UserId).Select(item => item.Id).ToListAsync();
            if (eligible.Count == 0) return Results.NotFound();
            var changed = await db.CartItems.Where(item => eligible.Contains(item.Id)
                    && item.PointsHistoryId == orderId && !item.InCart && !item.WasOrdered)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.WasOrdered, true));
            return changed == 0 ? Results.NotFound() : Results.NoContent();
        });

        app.MapGet("/users/{id:int}/orders", async (int id, AppDbContext db, TimeProvider clock) =>
        {
            if (!await db.Users.AnyAsync(user => user.Id == id)) return Results.NotFound();
            var items = await db.CartItems.AsNoTracking().Include(item => item.PointsHistory)
                .Where(item => item.UserId == id && !item.InCart
                    && item.PointsHistory != null && item.PointsHistory.UserId == id).ToListAsync();
            var now = clock.GetUtcNow().UtcDateTime;
            return Results.Ok(items.GroupBy(item => item.PointsHistoryId!.Value).Select(group =>
            {
                var history = group.First().PointsHistory!;
                return new PastOrderDetails(history.Id, history.Timestamp, -(long)history.PointsDelta,
                    CanRefund(history, now), group.Select(item => new CartItemDetails(item.Id, item.Name, item.Price, item.Description, item.Quantity)).ToList());
            }).OrderByDescending(order => order.Timestamp).ThenByDescending(order => order.PointsHistoryId));
        });

        app.MapPost("/users/{id:int}/orders/{historyId:int}/refund", async (int id, int historyId, AppDbContext db, TimeProvider clock) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            // Use the same balance lock as checkout to serialize refunds and purchases.
            await db.Users.Where(user => user.Id == id)
                .ExecuteUpdateAsync(update => update.SetProperty(user => user.Points, user => user.Points));
            var user = await db.Users.SingleOrDefaultAsync(user => user.Id == id);
            if (user is null) return Results.NotFound();
            var history = await db.PointsHistory.SingleOrDefaultAsync(h => h.Id == historyId && h.UserId == id);
            if (history is null) return Results.NotFound(new { message = "This order was not found or has already been refunded." });
            if (!CanRefund(history, clock.GetUtcNow().UtcDateTime))
                return Results.BadRequest(new { message = "Orders can only be refunded within 24 hours of purchase." });
            var items = await db.CartItems.Where(item => item.PointsHistoryId == historyId).ToListAsync();
            if (items.Count == 0 || items.Any(item => item.UserId != id || item.InCart))
                return Results.BadRequest(new { message = "This history entry is not a refundable order." });
            var balance = (long)user.Points - history.PointsDelta;
            if (balance > int.MaxValue)
                return Results.BadRequest(new { message = "The refund would exceed the maximum points balance." });
            user.Points = (int)balance;
            db.CartItems.RemoveRange(items);
            db.PointsHistory.Remove(history);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.NoContent();
        });

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
                item.WasOrdered = false;
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
    private static bool CanRefund(PointsHistory history, DateTime now) =>
        history.PointsDelta <= 0 && history.Timestamp is DateTime timestamp
        && timestamp <= now && now - timestamp < TimeSpan.FromHours(24);
}

// Price in cart requests/responses is USD per item, not reward points.
public record CartItemDetails(uint Id, string Name, decimal Price, string? Description, int Quantity = 1);
public record UpdateCartQuantity(int Quantity);
public record AddCartItem(string Name, decimal Price, string? Description);
public record SponsorOrderDetails(int OrderId, int DriverId, string DriverName, DateTime? Timestamp, decimal TotalPrice, List<CartItemDetails> Items);
public record PastOrderDetails(int PointsHistoryId, DateTime? Timestamp, long TotalPoints, bool CanRefund, List<CartItemDetails> Items);
