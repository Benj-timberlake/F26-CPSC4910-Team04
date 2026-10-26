using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class OrderReasonTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int sue, bob;

    private async Task Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var company = new Company { Name = "Acme" };
        db.Add(company);
        await db.SaveChangesAsync();
        var users = new[] { User("sue", "sponsor", company.Id, 0), User("bob", "driver", company.Id, 10_000) };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        (sue, bob) = (users[0].Id, users[1].Id);
        db.CartItems.Add(new CartItem { UserId = bob, Name = "Gloves", Price = 20m, Quantity = 1 });
        await db.SaveChangesAsync();
    }

    private static User User(string name, string type, int? companyId, int points) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x", Points = points,
        AccountsCompanies = companyId is int company ? [new AccountsCompany { CompanyId = company }] : []
    };

    [Fact]
    public async Task SponsorSeesTheDriversReason()
    {
        await Start();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/users/{bob}/cart/send-order", new { reason = " Mine tore " })).StatusCode);

        var order = Assert.Single((await client.GetFromJsonAsync<List<SponsorOrderDetails>>($"/users/{sue}/driver-orders"))!);
        Assert.Equal("Mine tore", order.Reason);
        var audit = Assert.Single((await client.GetFromJsonAsync<List<AuditEntry>>($"/users/{sue}/audit?category=points"))!);
        Assert.Equal("-2,000 points: order: Mine tore", audit.Detail);
    }

    [Fact]
    public async Task ReasonIsOptional()
    {
        await Start();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/users/{bob}/cart/send-order", null)).StatusCode);

        Assert.Null(Assert.Single((await client.GetFromJsonAsync<List<SponsorOrderDetails>>($"/users/{sue}/driver-orders"))!).Reason);
        Assert.Empty(await app.Db().AuditHistories.ToListAsync());
    }

    [Fact]
    public async Task LongReasonIsRefused()
    {
        await Start();

        var response = await client.PostAsJsonAsync($"/users/{bob}/cart/send-order", new { reason = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await app.Db().PointsHistory.ToListAsync());
    }

    [Fact]
    public async Task RefundClearsTheReason()
    {
        await Start();
        await client.PostAsJsonAsync($"/users/{bob}/cart/send-order", new { reason = "Mine tore" });
        var order = (await app.Db().PointsHistory.SingleAsync()).Id;

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/users/{bob}/orders/{order}/refund", null)).StatusCode);

        Assert.Empty(await app.Db().AuditHistories.ToListAsync());
        Assert.Equal(10_000, (await app.Db().Users.SingleAsync(u => u.Id == bob)).Points);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
