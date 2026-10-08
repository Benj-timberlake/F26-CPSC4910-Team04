using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TruckerReward.Tests;

public sealed class PointsNotificationTests : IAsyncDisposable
{
    private readonly TestApp app = new();

    private async Task<(HttpClient client, int id)> StartWithBob(int points)
    {
        var client = await app.Start();
        await client.PostAsJsonAsync("/auth/register", new { username = "bob", firstName = "Bob", lastName = "Driver", email = "bob@example.com", password = "Hunter22x!", userType = "driver" });
        using var db = app.Db();
        var bob = await db.Users.SingleAsync();
        bob.Points = points;
        await db.SaveChangesAsync();
        return (client, bob.Id);
    }

    private async Task<List<NotificationsHistory>> NotificationsFor(int userId) =>
        await app.Db().NotificationsHistories.Where(n => n.UserId == userId).OrderBy(n => n.Id).ToListAsync();

    [Fact]
    public async Task PlacingAnOrderTellsTheDriverOnTheSite()
    {
        var (client, id) = await StartWithBob(1000);
        await client.PostAsJsonAsync($"/users/{id}/cart", new { name = "Mug", price = 3.50m, description = "A mug" });

        var response = await client.PostAsync($"/users/{id}/cart/send-order", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var saved = Assert.Single(await NotificationsFor(id));
        Assert.Equal("350 points deducted", saved.Subject);
        Assert.Equal("Your order was placed. Your balance is now 650 points.", saved.Body);
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task RefundingTellsTheDriverThePointsCameBack()
    {
        var (client, id) = await StartWithBob(1000);
        await client.PostAsJsonAsync($"/users/{id}/cart", new { name = "Mug", price = 3.50m, description = "A mug" });
        await client.PostAsync($"/users/{id}/cart/send-order", null);
        var historyId = (await app.Db().PointsHistory.SingleAsync()).Id;

        var response = await client.PostAsync($"/users/{id}/orders/{historyId}/refund", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var refund = (await NotificationsFor(id)).Last();
        Assert.Equal("350 points added", refund.Subject);
        Assert.Equal("Your order was refunded. Your balance is now 1,000 points.", refund.Body);
    }

    [Fact]
    public async Task FailedOrderSendsNothing()
    {
        var (client, id) = await StartWithBob(10);
        await client.PostAsJsonAsync($"/users/{id}/cart", new { name = "Mug", price = 3.50m, description = "A mug" });

        var response = await client.PostAsync($"/users/{id}/cart/send-order", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await NotificationsFor(id));
    }

    [Fact]
    public async Task NegativeBalanceAlertsOnlyThatCompanysSponsors()
    {
        var client = await app.Start();
        using (var db = app.Db())
        {
            var acme = new Company { Name = "Acme" };
            var other = new Company { Name = "Other" };
            db.AddRange(acme, other);
            await db.SaveChangesAsync();
            db.Users.AddRange(
                User("bob", "driver", acme.Id, points: -25),
                User("sue", "sponsor", acme.Id),
                User("tom", "sponsor", acme.Id),
                User("zed", "sponsor", other.Id));
            await db.SaveChangesAsync();
        }

        using (var db = app.Db())
        {
            var bob = await db.Users.SingleAsync(u => u.Username == "bob");
            await new Notifications(db, app.Email, app.Clock, NullLogger<Notifications>.Instance)
                .PointsChanged(bob, -125, "Points were deducted.");
        }

        var saved = await app.Db().NotificationsHistories.Include(n => n.User).ToListAsync();
        Assert.Equal(["bob", "sue", "tom"], saved.Select(n => n.User.Username).Order());
        Assert.Contains(saved, n => n.User.Username == "sue" && n.Subject == "bob is negative on points" && n.Body.Contains("-25 points"));
        Assert.Equal(["sue@example.com", "tom@example.com"], app.Email.Sent.Select(m => m.To).Order());
    }

    [Fact]
    public async Task SponsorsAreToldOnTheSiteWhenADriverBuys()
    {
        var (client, id) = await StartWithBob(1000);
        using (var db = app.Db())
        {
            var acme = new Company { Name = "Acme" };
            db.Add(acme);
            await db.SaveChangesAsync();
            db.Users.Add(User("sue", "sponsor", acme.Id));
            (await db.Users.SingleAsync(u => u.Id == id)).CompanyId = acme.Id;
            await db.SaveChangesAsync();
        }
        await client.PostAsJsonAsync($"/users/{id}/cart", new { name = "Mug", price = 3.50m, description = "A mug" });
        await client.PostAsJsonAsync($"/users/{id}/cart", new { name = "Hat", price = 1.00m, description = "A hat" });

        await client.PostAsync($"/users/{id}/cart/send-order", null);

        var sponsor = await app.Db().NotificationsHistories.Include(n => n.User).SingleAsync(n => n.User.Username == "sue");
        Assert.Equal("bob placed an order", sponsor.Subject);
        Assert.Equal("Bob Driver (bob) spent 450 points on Mug, Hat.", sponsor.Body);
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task DriverWithoutACompanyAlertsNoSponsor()
    {
        var (client, id) = await StartWithBob(1000);
        using (var db = app.Db())
        {
            var acme = new Company { Name = "Acme" };
            db.Add(acme);
            await db.SaveChangesAsync();
            db.Users.Add(User("sue", "sponsor", acme.Id));
            await db.SaveChangesAsync();
        }
        await client.PostAsJsonAsync($"/users/{id}/cart", new { name = "Mug", price = 3.50m, description = "A mug" });

        await client.PostAsync($"/users/{id}/cart/send-order", null);

        Assert.Single(await app.Db().NotificationsHistories.ToListAsync());
    }

    private static User User(string name, string type, int companyId, int points = 0) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x", CompanyId = companyId, Points = points
    };

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
