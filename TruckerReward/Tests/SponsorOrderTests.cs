using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace TruckerReward.Tests;

public sealed class SponsorOrderTests
{
    [Fact]
    public async Task SponsorSeesOnlyPlacedDriverOrdersInTheirCompanyWithPrices()
    {
        await using var app = new TestApp();
        using var client = await app.Start(builder => builder.Logging.ClearProviders());
        using var db = app.Db();
        var company = new Company { Name = "Company" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var sponsor = NewUser("sponsor", "sponsor", company.Id);
        var driver = NewUser("driver", "driver", company.Id);
        var other = NewUser("other", "driver", null);
        db.Users.AddRange(sponsor, driver, other);
        await db.SaveChangesAsync();
        var history = new PointsHistory { UserId = driver.Id, PointsDelta = -2500, Timestamp = DateTime.UtcNow };
        var otherHistory = new PointsHistory { UserId = other.Id, PointsDelta = -1000, Timestamp = DateTime.UtcNow };
        db.PointsHistory.AddRange(history, otherHistory);
        await db.SaveChangesAsync();
        db.CartItems.AddRange(
            new CartItem { UserId = driver.Id, Name = "Mug", Price = 12.50m, Quantity = 2, WasOrdered = false, InCart = false, PointsHistoryId = history.Id },
            new CartItem { UserId = driver.Id, Name = "Unplaced", Price = 99m },
            new CartItem { UserId = other.Id, Name = "Other", Price = 10m, WasOrdered = true, InCart = false, PointsHistoryId = otherHistory.Id });
        await db.SaveChangesAsync();
        var orders = await client.GetFromJsonAsync<List<SponsorOrderDetails>>($"/users/{sponsor.Id}/driver-orders");
        var order = Assert.Single(orders!);
        Assert.Equal(driver.Id, order.DriverId);
        Assert.Equal(25m, order.TotalPrice);
        Assert.Equal("Mug", Assert.Single(order.Items).Name);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/users/{driver.Id}/driver-orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/users/{driver.Id}/driver-orders/{history.Id}/buy", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/users/{sponsor.Id}/driver-orders/{otherHistory.Id}/buy", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/users/{sponsor.Id}/driver-orders/{history.Id}/buy", null)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<SponsorOrderDetails>>($"/users/{sponsor.Id}/driver-orders"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/users/{sponsor.Id}/driver-orders/{history.Id}/buy", null)).StatusCode);
        db.ChangeTracker.Clear();
        Assert.True((await db.CartItems.SingleAsync(item => item.PointsHistoryId == history.Id)).WasOrdered);
        Assert.Single((await client.GetFromJsonAsync<List<PastOrderDetails>>($"/users/{driver.Id}/orders"))!);
    }

    private static User NewUser(string name, string role, int? companyId) => new()
    {
        Username = name, Email = name + "@example.com", UserType = role, CompanyId = companyId,
        Password = "hash", PhoneNumber = "", FirstName = name, LastName = "User", Address = ""
    };
}
