using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class OrderStatusTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int sue, bob, order;

    private async Task Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var company = new Company { Name = "Acme" };
        db.Add(company);
        await db.SaveChangesAsync();
        var users = new[] { User("sue", "sponsor", company.Id), User("bob", "driver", company.Id) };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        var history = new PointsHistory { UserId = users[1].Id, PointsDelta = -2500, Timestamp = app.Clock.GetUtcNow().UtcDateTime };
        db.PointsHistory.Add(history);
        await db.SaveChangesAsync();
        db.CartItems.AddRange(
            new CartItem { UserId = users[1].Id, Name = "Mug", Price = 10m, Quantity = 2, InCart = false, PointsHistoryId = history.Id },
            new CartItem { UserId = users[1].Id, Name = "Hat", Price = 5m, InCart = false, PointsHistoryId = history.Id });
        await db.SaveChangesAsync();
        (sue, bob, order) = (users[0].Id, users[1].Id, history.Id);
    }

    private static User User(string name, string type, int? companyId) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x",
        AccountsCompanies = companyId is int company ? [new AccountsCompany { CompanyId = company }] : []
    };

    private async Task<string> Status() =>
        Assert.Single((await client.GetFromJsonAsync<List<PastOrderDetails>>($"/users/{bob}/orders"))!).Status;

    [Fact]
    public async Task DriverSeesTheStatusChangeAndIsAlerted()
    {
        await Start();
        Assert.Equal(CartEndpoints.OrderPending, await Status());

        await client.PostAsync($"/users/{sue}/driver-orders/{order}/buy", null);

        Assert.Equal(CartEndpoints.OrderOrdered, await Status());
        var notice = await app.Db().NotificationsHistories.SingleAsync(n => n.UserId == bob);
        Assert.Equal(("Your sponsor ordered your items", "Your sponsor placed the order for Mug x2, Hat."), (notice.Subject, notice.Body));
    }

    [Fact]
    public async Task PlacingAnOrderAlertsThatItIsPending()
    {
        await Start();
        using (var db = app.Db())
        {
            await db.Users.Where(u => u.Id == bob).ExecuteUpdateAsync(u => u.SetProperty(x => x.Points, 5000));
            db.CartItems.Add(new CartItem { UserId = bob, Name = "Gloves", Price = 12m, Quantity = 3, InCart = true });
            await db.SaveChangesAsync();
        }

        await client.PostAsync($"/users/{bob}/cart/send-order", null);

        var notice = await app.Db().NotificationsHistories.SingleAsync(n => n.UserId == bob && n.Subject == "Your order is pending");
        Assert.Equal("Your order for Gloves x3 is waiting on your sponsor.", notice.Body);
    }

    [Fact]
    public async Task DriversCanSwitchOrderAlertsOff()
    {
        await Start();
        await client.PutAsJsonAsync($"/users/{bob}/notification-settings", new[] { new { category = "orders", enabled = false, emailed = false } });

        await client.PostAsync($"/users/{sue}/driver-orders/{order}/buy", null);

        Assert.Equal(CartEndpoints.OrderOrdered, await Status());
        Assert.Empty(await app.Db().NotificationsHistories.Where(n => n.UserId == bob).ToListAsync());
    }

    [Fact]
    public async Task RejectionPreservesOrderAndRefundsOnlyOnceEvenAfter24Hours()
    {
        await Start();
        app.Clock.Advance(TimeSpan.FromDays(2));
        var url = $"/users/{sue}/driver-orders/{order}/reject";
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, new RejectOrderRequest(" "))).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/users/{bob}/driver-orders/{order}/reject", new RejectOrderRequest("No"))).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await client.PostAsJsonAsync(url, new RejectOrderRequest("  Out of stock  "))).StatusCode);
        var rejected = Assert.Single((await client.GetFromJsonAsync<List<PastOrderDetails>>($"/users/{bob}/orders"))!);
        Assert.Equal(CartEndpoints.OrderRejected, rejected.Status);
        Assert.Equal("Out of stock", rejected.RejectionReason);
        Assert.False(rejected.CanRefund);
        Assert.Equal(2, rejected.Items.Count);
        Assert.Empty((await client.GetFromJsonAsync<List<SponsorOrderDetails>>($"/users/{sue}/driver-orders"))!);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, (await client.PostAsJsonAsync(url, new RejectOrderRequest("Again"))).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsync($"/users/{sue}/driver-orders/{order}/buy", null)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, (await client.PostAsync($"/users/{bob}/orders/{order}/edit", null)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await client.PostAsync($"/users/{bob}/orders/{order}/refund", null)).StatusCode);
        using var db = app.Db();
        Assert.Equal(2500, (await db.Users.SingleAsync(u => u.Id == bob)).Points);
        Assert.Equal(2500, (await db.PointsHistory.SingleAsync(h => h.UserId == bob && h.PointsDelta > 0)).PointsDelta);
    }

    [Fact]
    public async Task PurchasedOrdersCannotBeRejected()
    {
        await Start();
        await client.PostAsync($"/users/{sue}/driver-orders/{order}/buy", null);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/users/{sue}/driver-orders/{order}/reject", new RejectOrderRequest("No"))).StatusCode);
        using var db = app.Db();
        Assert.Equal(0, (await db.Users.SingleAsync(u => u.Id == bob)).Points);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(86399)]
    public async Task SponsorPurchaseDisablesRefundAndStaleRefundRequestsAreBlocked(int ageSeconds)
    {
        await Start();
        app.Clock.Advance(TimeSpan.FromSeconds(ageSeconds));
        var before = Assert.Single((await client.GetFromJsonAsync<List<PastOrderDetails>>($"/users/{bob}/orders"))!);
        Assert.True(before.CanRefund);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await client.PostAsync($"/users/{sue}/driver-orders/{order}/buy", null)).StatusCode);
        var after = Assert.Single((await client.GetFromJsonAsync<List<PastOrderDetails>>($"/users/{bob}/orders"))!);
        Assert.False(after.CanRefund);
        var response = await client.PostAsync($"/users/{bob}/orders/{order}/refund", null);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("once your sponsor has placed", await response.Content.ReadAsStringAsync());
        using var db = app.Db();
        Assert.Equal(0, (await db.Users.SingleAsync(u => u.Id == bob)).Points);
        Assert.Equal(2, await db.CartItems.CountAsync(i => i.PointsHistoryId == order));
        Assert.NotNull(await db.PointsHistory.FindAsync(order));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too long")]
    public async Task InvalidRejectionReasonsLeaveTheOrderAndPointsUnchanged(string? reason)
    {
        await Start();
        if (reason == "too long") reason = new string('x', 501);
        var response = await client.PostAsJsonAsync($"/users/{sue}/driver-orders/{order}/reject", new RejectOrderRequest(reason));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CartEndpoints.OrderPending, await Status());
        using var db = app.Db();
        Assert.Equal(0, (await db.Users.SingleAsync(u => u.Id == bob)).Points);
        Assert.Null((await db.PointsHistory.FindAsync(order))!.RejectionReason);
        Assert.Single(await db.PointsHistory.ToListAsync());
        Assert.Empty(await db.NotificationsHistories.ToListAsync());
    }

    [Fact]
    public async Task SponsorCannotRejectAnotherCompanysOrder()
    {
        await Start();
        using var db = app.Db();
        var company = new Company { Name = "Other company" };
        db.Add(company);
        await db.SaveChangesAsync();
        var outsider = User("outsider", "sponsor", company.Id);
        db.Users.Add(outsider);
        await db.SaveChangesAsync();
        var response = await client.PostAsJsonAsync($"/users/{outsider.Id}/driver-orders/{order}/reject", new RejectOrderRequest("Not approved"));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(CartEndpoints.OrderPending, await Status());
        db.ChangeTracker.Clear();
        Assert.Equal(0, (await db.Users.SingleAsync(u => u.Id == bob)).Points);
        Assert.Single(await db.PointsHistory.ToListAsync());
    }

    [Fact]
    public async Task RejectionThatWouldOverflowBalanceDoesNotPartiallySave()
    {
        await Start();
        using var db = app.Db();
        var driver = await db.Users.SingleAsync(u => u.Id == bob);
        driver.Points = int.MaxValue;
        await db.SaveChangesAsync();
        var response = await client.PostAsJsonAsync($"/users/{sue}/driver-orders/{order}/reject", new RejectOrderRequest("Out of stock"));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CartEndpoints.OrderPending, await Status());
        db.ChangeTracker.Clear();
        Assert.Equal(int.MaxValue, (await db.Users.SingleAsync(u => u.Id == bob)).Points);
        Assert.Null((await db.PointsHistory.FindAsync(order))!.RejectionReason);
        Assert.Single(await db.PointsHistory.ToListAsync());
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
