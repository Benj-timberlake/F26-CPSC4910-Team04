using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Xunit;

namespace TruckerReward.Tests;

public sealed class CartEndpointTests
{
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
