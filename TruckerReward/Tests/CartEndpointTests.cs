using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class CartEndpointTests
{
    [Theory]
    [InlineData(5000, 0)]
    [InlineData(6000, 1000)]
    public async Task SendOrderDeductsPointsAndPreservesOrderedItems(int balance, int remaining)
    {
        await using var app = new TestApp();
        using var client = await app.Start(builder => builder.Configuration["BACKEND_API_KEY"] = "test-key");
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        using var db = app.Db();
        // Match the existing database schema independently of EF's generated model.
        Assert.Equal("Points_History", db.Model.FindEntityType(typeof(PointsHistory))!.GetTableName());
        await db.Database.ExecuteSqlRawAsync("DROP TABLE Points_History;");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE Points_History (
                points_history_id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INT NOT NULL REFERENCES users(id),
                timestamp DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
                points_delta INT NOT NULL
            );
            """);
        var owner = NewUser("sender");
        owner.Points = balance;
        var other = NewUser("non-sender");
        db.Users.AddRange(owner, other);
        await db.SaveChangesAsync();
        db.CartItems.AddRange(
            new CartItem { UserId = owner.Id, Name = "Mug", Price = 10m, Quantity = 3 },
            new CartItem { UserId = owner.Id, Name = "GPS", Price = 20m },
            new CartItem { UserId = other.Id, Name = "Other mug", Price = 10m });
        await db.SaveChangesAsync();
        var path = $"/users/{owner.Id}/cart/send-order";
        client.DefaultRequestHeaders.Remove("X-Api-Key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(path, null)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(path, null)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<CartItemDetails>>($"/users/{owner.Id}/cart"))!);
        Assert.Single((await client.GetFromJsonAsync<List<CartItemDetails>>($"/users/{other.Id}/cart"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/users/999999/cart/send-order", null)).StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(remaining, (await db.Users.FindAsync(owner.Id))!.Points);
        var history = Assert.Single(await db.PointsHistory.ToListAsync());
        Assert.Equal(owner.Id, history.UserId);
        Assert.Equal(-5000, history.PointsDelta);
        Assert.Equal(app.Clock.GetUtcNow().UtcDateTime, history.Timestamp);
        var ordered = await db.CartItems.Where(item => item.UserId == owner.Id).ToListAsync();
        Assert.Equal(2, ordered.Count);
        foreach (var item in ordered)
        {
            Assert.False(item.InCart);
            Assert.True(item.WasOrdered);
            Assert.Equal(history.Id, item.PointsHistoryId);
            var itemPath = $"/users/{owner.Id}/cart/{item.Id}";
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(itemPath)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(itemPath + "/quantity", new UpdateCartQuantity(2))).StatusCode);
        }
        Assert.Equal(0, (await db.Users.FindAsync(other.Id))!.Points);
        var otherItem = await db.CartItems.SingleAsync(item => item.UserId == other.Id);
        Assert.True(otherItem.InCart);
        Assert.False(otherItem.WasOrdered);
        Assert.Null(otherItem.PointsHistoryId);
    }

    [Theory]
    [InlineData(0, 0.01, 1)]
    [InlineData(3699, 12.34, 3)]
    [InlineData(2147483647, 99999999.99, 999)]
    public async Task InsufficientPointsLeaveBalanceCartAndHistoryUnchanged(int balance, decimal price, int quantity)
    {
        await using var app = new TestApp();
        using var client = await app.Start();
        using var db = app.Db();
        var owner = NewUser("insufficient");
        owner.Points = balance;
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        db.CartItems.Add(new CartItem { UserId = owner.Id, Name = "Mug", Price = price, Quantity = quantity });
        await db.SaveChangesAsync();

        var response = await client.PostAsync($"/users/{owner.Id}/cart/send-order", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Not enough points", await response.Content.ReadAsStringAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(balance, (await db.Users.FindAsync(owner.Id))!.Points);
        Assert.Empty(await db.PointsHistory.ToListAsync());
        var item = Assert.Single(await db.CartItems.ToListAsync());
        Assert.True(item.InCart);
        Assert.False(item.WasOrdered);
        Assert.Equal(quantity, item.Quantity);
        Assert.Null(item.PointsHistoryId);
    }

    [Fact]
    public async Task HistoryFailureRollsBackDeductionAndOrderStatus()
    {
        await using var app = new TestApp();
        using var client = await app.Start();
        using var db = app.Db();
        var owner = NewUser("rollback");
        owner.Points = 5000;
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        db.CartItems.Add(new CartItem { UserId = owner.Id, Name = "Mug", Price = 12.34m });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_history BEFORE INSERT ON Points_History BEGIN SELECT RAISE(ABORT, 'test failure'); END;");

        await Assert.ThrowsAnyAsync<Exception>(() => client.PostAsync($"/users/{owner.Id}/cart/send-order", null));
        db.ChangeTracker.Clear();
        Assert.Equal(5000, (await db.Users.FindAsync(owner.Id))!.Points);
        Assert.Empty(await db.PointsHistory.ToListAsync());
        var item = Assert.Single(await db.CartItems.ToListAsync());
        Assert.True(item.InCart);
        Assert.False(item.WasOrdered);
        Assert.Null(item.PointsHistoryId);
    }

    [Fact]
    public async Task QuantityAndRemovalPersistAndRespectItemOwner()
    {
        await using var app = new TestApp();
        using var client = await app.Start(builder => builder.Configuration["BACKEND_API_KEY"] = "test-key");
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        using var db = app.Db();
        var owner = NewUser("quantity-buyer");
        var other = NewUser("other-buyer");
        db.Users.AddRange(owner, other);
        await db.SaveChangesAsync();
        var item = new CartItem { UserId = owner.Id, Name = "Mug", Price = 10m };
        db.CartItems.Add(item);
        await db.SaveChangesAsync();
        var path = $"/users/{owner.Id}/cart/{item.Id}";
        var wrongOwner = $"/users/{other.Id}/cart/{item.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(wrongOwner + "/quantity", new UpdateCartQuantity(2))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(wrongOwner)).StatusCode);
        foreach (var invalid in new[] { 0, -1, 1000 })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path + "/quantity", new UpdateCartQuantity(invalid))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(path + "/quantity", new UpdateCartQuantity(3))).StatusCode);
        var saved = Assert.Single((await client.GetFromJsonAsync<List<CartItemDetails>>($"/users/{owner.Id}/cart"))!);
        Assert.Equal(3, saved.Quantity);
        Assert.Equal(30m, saved.Price * saved.Quantity);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<CartItemDetails>>($"/users/{owner.Id}/cart"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(path)).StatusCode);
    }

    [Fact]
    public async Task AddSavesProductToUsersCartAndRejectsInvalidPrice()
    {
        await using var app = new TestApp();
        using var client = await app.Start(builder => builder.Configuration["BACKEND_API_KEY"] = "test-key");
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        using var db = app.Db();
        var owner = NewUser("buyer");
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var response = await client.PostAsJsonAsync($"/users/{owner.Id}/cart", new AddCartItem("GPS", 29.50m, "Navigation"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var items = await client.GetFromJsonAsync<List<CartItemDetails>>($"/users/{owner.Id}/cart");
        var item = Assert.Single(items!);
        Assert.Equal("GPS", item.Name);
        Assert.Equal(29.50m, item.Price);
        Assert.Equal("Navigation", item.Description);

        var invalid = await client.PostAsJsonAsync($"/users/{owner.Id}/cart", new AddCartItem("GPS", -1m, null));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<List<CartItemDetails>>($"/users/{owner.Id}/cart"))!);
        client.DefaultRequestHeaders.Remove("X-Api-Key");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"/users/{owner.Id}/cart", new AddCartItem("GPS", 29.50m, null))).StatusCode);
    }

    [Fact]
    public async Task FetchReturnsOnlyRequestedUsersItemsAndSupportsEmptyCart()
    {
        await using var app = new TestApp();
        using var client = await app.Start();
        using var db = app.Db();
        var owner = NewUser("owner");
        var other = NewUser("other");
        db.Users.AddRange(owner, other);
        await db.SaveChangesAsync();

        var empty = await client.GetFromJsonAsync<List<CartItemDetails>>($"/users/{owner.Id}/cart");
        Assert.Empty(empty!);

        db.CartItems.AddRange(
            new CartItem { UserId = owner.Id, Name = "Travel mug", Price = 12.34m, Description = "Insulated mug" },
            new CartItem { UserId = other.Id, Name = "Other user's item", Price = 50m });
        await db.SaveChangesAsync();

        var items = await client.GetFromJsonAsync<List<CartItemDetails>>($"/users/{owner.Id}/cart");
        var item = Assert.Single(items!);
        Assert.Equal("Travel mug", item.Name);
        Assert.Equal(12.34m, item.Price);
        Assert.Equal("Insulated mug", item.Description);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/users/999999/cart")).StatusCode);
    }

    [Fact]
    public async Task FetchRequiresConfiguredBackendApiKey()
    {
        await using var app = new TestApp();
        using var client = await app.Start(builder => builder.Configuration["BACKEND_API_KEY"] = "test-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/users/1/cart")).StatusCode);
    }

    private static User NewUser(string name) => new()
    {
        Username = name, Email = $"{name}@example.com", UserType = "driver",
        PhoneNumber = "", Address = ""
    };
}
