using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class OrderEditTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int sue, bob, tom, order;

    private async Task Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var company = new Company { Name = "Acme" };
        db.Add(company);
        await db.SaveChangesAsync();
        var users = new[] { User("sue", "sponsor", company.Id, 0), User("bob", "driver", company.Id, 10_000), User("tom", "driver", company.Id, 0) };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        (sue, bob, tom) = (users[0].Id, users[1].Id, users[2].Id);
        db.CartItems.AddRange(
            new CartItem { UserId = bob, Name = "Gloves", Price = 20m, Quantity = 2 },
            new CartItem { UserId = bob, Name = "Hat", Price = 5m });
        await db.SaveChangesAsync();
        await client.PostAsJsonAsync($"/users/{bob}/cart/send-order", new { reason = "Winter" });
        order = (await app.Db().PointsHistory.SingleAsync()).Id;
    }

    private static User User(string name, string type, int? companyId, int points) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x", CompanyId = companyId, Points = points
    };

    [Fact]
    public async Task PendingOrderGoesBackToTheCartWithItsPoints()
    {
        await Start();
        Assert.Equal(5_500, (await app.Db().Users.SingleAsync(u => u.Id == bob)).Points);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/users/{bob}/orders/{order}/edit", null)).StatusCode);

        using var db = app.Db();
        Assert.Equal(10_000, (await db.Users.SingleAsync(u => u.Id == bob)).Points);
        Assert.Empty(await db.PointsHistory.ToListAsync());
        Assert.Empty(await db.AuditHistories.ToListAsync());
        var cart = await client.GetFromJsonAsync<List<CartItemDetails>>($"/users/{bob}/cart");
        Assert.Equal(["Gloves x2", "Hat x1"], cart!.Select(i => $"{i.Name} x{i.Quantity}").Order());
        Assert.Contains(await db.NotificationsHistories.Where(n => n.UserId == sue).Select(n => n.Subject).ToListAsync(), s => s == "bob pulled an order back to change it");
    }

    [Fact]
    public async Task OrderedOrdersCantBeChanged()
    {
        await Start();
        await client.PostAsync($"/users/{sue}/driver-orders/{order}/buy", null);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/users/{bob}/orders/{order}/edit", null)).StatusCode);
        Assert.Equal(5_500, (await app.Db().Users.SingleAsync(u => u.Id == bob)).Points);
    }

    [Fact]
    public async Task OnlyTheDriverCanChangeTheirOrder()
    {
        await Start();

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/users/{tom}/orders/{order}/edit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/users/{bob}/orders/999/edit", null)).StatusCode);
        Assert.Single(await app.Db().PointsHistory.ToListAsync());
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
