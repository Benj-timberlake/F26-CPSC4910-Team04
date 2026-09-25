using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Xunit;

namespace TruckerReward.Tests;

public sealed class CartEndpointTests
{
    [Fact]
    public async Task SendOrderClearsOnlyOwnersCartAndRejectsEmptyCart()
    {
        await using var app = new TestApp();
        using var client = await app.Start(builder => builder.Configuration["BACKEND_API_KEY"] = "test-key");
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        using var db = app.Db();
        var owner = NewUser("sender");
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
