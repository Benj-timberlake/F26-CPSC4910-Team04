using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class PointsEndpointTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob, amy, sue, zed, ann, acme;

    private async Task Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var a = new Company { Name = "Acme" };
        var o = new Company { Name = "Other" };
        db.AddRange(a, o);
        await db.SaveChangesAsync();
        var users = new[]
        {
            User("bob", "driver", a.Id, "Bob", "Baker", 100),
            User("amy", "driver", a.Id, "Amy", "Zane", 0),
            User("sue", "sponsor", a.Id),
            User("zed", "sponsor", o.Id),
            User("ann", "admin", null),
            User("tom", "driver", o.Id, "Tom", "Other", 0)
        };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        (bob, amy, sue, zed, ann, acme) = (users[0].Id, users[1].Id, users[2].Id, users[3].Id, users[4].Id, a.Id);
    }

    private static User User(string name, string type, int? companyId, string? first = null, string? last = null, int points = 0) => new()
    {
        Username = name, FirstName = first ?? name, LastName = last ?? "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x", Points = points,
        AccountsCompanies = companyId is int company ? [new AccountsCompany { CompanyId = company }] : []
    };

    private Task<HttpResponseMessage> Change(int actor, int driver, int points, string? reason) =>
        client.PostAsJsonAsync($"/users/{actor}/points/drivers/{driver}", new { points, reason });

    [Fact]
    public async Task SponsorAwardsPointsWithAReason()
    {
        await Start();

        var response = await Change(sue, bob, 250, "Safe driving week");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(350, (await response.Content.ReadFromJsonAsync<PointsDriver>())!.Points);
        Assert.Equal(350, (await app.Db().Users.SingleAsync(u => u.Id == bob)).Points);
        var history = await app.Db().PointsHistory.SingleAsync();
        Assert.Equal(250, history.PointsDelta);
        Assert.Equal(app.Clock.GetUtcNow().UtcDateTime, history.Timestamp);
        var audit = await app.Db().AuditHistories.SingleAsync();
        Assert.Equal(sue, audit.UserId);
        Assert.Equal(history.Id, audit.PointsHistoryId);
        Assert.Equal("Safe driving week", audit.Resoning);

        var note = await app.Db().NotificationsHistories.SingleAsync(n => n.UserId == bob);
        Assert.Equal("250 points added", note.Subject);
        Assert.Equal("Reason: Safe driving week. Your balance is now 350 points.", note.Body);
    }

    [Fact]
    public async Task DeductingBelowZeroAlertsTheSponsors()
    {
        await Start();

        Assert.Equal(HttpStatusCode.OK, (await Change(sue, bob, -150, "Speeding ticket.")).StatusCode);

        Assert.Equal(-50, (await app.Db().Users.SingleAsync(u => u.Id == bob)).Points);
        Assert.True(await app.Db().NotificationsHistories.AnyAsync(n => n.UserId == bob && n.Subject == "150 points deducted"));
        var mail = Assert.Single(app.Email.Sent);
        Assert.Equal("sue@example.com", mail.To);
        Assert.Equal("bob is negative on points", mail.Subject);
    }

    [Theory]
    [InlineData(0, "why")]
    [InlineData(1_000_001, "why")]
    [InlineData(10, "")]
    [InlineData(10, null)]
    [InlineData(10, "   ")]
    public async Task BadChangesAreRefused(int points, string? reason)
    {
        await Start();
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(sue, bob, points, reason)).StatusCode);
        Assert.Empty(await app.Db().PointsHistory.ToListAsync());
    }

    [Fact]
    public async Task OnlyTheDriversSponsorOrAnAdminCanChangePoints()
    {
        await Start();

        Assert.Equal(HttpStatusCode.Forbidden, (await Change(zed, bob, 5, "nope")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Change(amy, bob, 5, "nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Change(sue, sue, 5, "not a driver")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Change(ann, bob, 5, "admin fix")).StatusCode);
        Assert.Equal(105, (await app.Db().Users.SingleAsync(u => u.Id == bob)).Points);
    }

    [Fact]
    public async Task SponsorsSearchTheirOwnDrivers()
    {
        await Start();

        var all = await client.GetFromJsonAsync<List<PointsDriver>>($"/users/{sue}/points/drivers");
        var found = await client.GetFromJsonAsync<List<PointsDriver>>($"/users/{sue}/points/drivers?search=bob baker");
        var byLast = await client.GetFromJsonAsync<List<PointsDriver>>($"/users/{sue}/points/drivers?search=Zane");
        var admin = await client.GetFromJsonAsync<List<PointsDriver>>($"/users/{ann}/points/drivers");

        Assert.Equal(["bob", "amy"], all!.Select(d => d.Username));
        Assert.Equal("bob", Assert.Single(found!).Username);
        Assert.Equal("amy", Assert.Single(byLast!).Username);
        Assert.Equal(3, admin!.Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/users/{bob}/points/drivers")).StatusCode);
    }

    [Fact]
    public async Task SponsorsSeePointsEarnedBetweenDates()
    {
        await Start();
        using (var db = app.Db())
        {
            db.PointsHistory.AddRange(
                new PointsHistory { UserId = bob, PointsDelta = 40, Timestamp = new DateTime(2026, 9, 1) },
                new PointsHistory { UserId = bob, PointsDelta = -10, Timestamp = new DateTime(2026, 9, 3) },
                new PointsHistory { UserId = amy, PointsDelta = 70, Timestamp = new DateTime(2026, 9, 10, 23, 59, 0) },
                new PointsHistory { UserId = amy, PointsDelta = 500, Timestamp = new DateTime(2026, 9, 11) });
            await db.SaveChangesAsync();
        }

        var range = (await client.GetFromJsonAsync<List<PointsDriver>>($"/users/{sue}/points/drivers?from=2026-09-01&to=2026-09-10&sort=earned"))!;
        var since = (await client.GetFromJsonAsync<List<PointsDriver>>($"/users/{sue}/points/drivers?from=2026-09-05"))!;
        var plain = (await client.GetFromJsonAsync<List<PointsDriver>>($"/users/{sue}/points/drivers"))!;

        Assert.Equal(["amy 70", "bob 40"], range.Select(d => $"{d.Username} {d.Earned}"));
        Assert.Equal(["bob 0", "amy 570"], since.Select(d => $"{d.Username} {d.Earned}"));
        Assert.All(plain, d => Assert.Null(d.Earned));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/users/{sue}/points/drivers?from=2026-09-10&to=2026-09-01")).StatusCode);
    }

    [Fact]
    public async Task SponsorsSortDriversByBalanceOrSpending()
    {
        await Start();
        await client.PostAsJsonAsync($"/users/{amy}/cart", new { name = "Mug", price = 0.00m, description = "free" });
        await Change(sue, amy, 500, "Award");
        await client.PostAsJsonAsync($"/users/{amy}/cart", new { name = "Hat", price = 2.00m, description = "A hat" });
        await client.PostAsync($"/users/{amy}/cart/send-order", null);

        var byPoints = (await client.GetFromJsonAsync<List<PointsDriver>>($"/users/{sue}/points/drivers?sort=points"))!;
        var bySpent = (await client.GetFromJsonAsync<List<PointsDriver>>($"/users/{sue}/points/drivers?sort=spent"))!;

        Assert.Equal(["amy", "bob"], byPoints.Select(d => d.Username));
        Assert.Equal(300, byPoints[0].Points);
        Assert.Equal(["amy", "bob"], bySpent.Select(d => d.Username));
        Assert.Equal(200, bySpent[0].Spent);
        Assert.Equal(0, bySpent[1].Spent);
    }

    [Fact]
    public async Task DriverSeesHistoryWithTotals()
    {
        await Start();
        await Change(sue, bob, 300, "Safe week");
        await Change(sue, bob, -40, "Late delivery");
        using (var db = app.Db())
        {
            var old = new PointsHistory { UserId = bob, PointsDelta = 500, Timestamp = app.Clock.GetUtcNow().UtcDateTime.AddMonths(-2) };
            db.PointsHistory.Add(old);
            db.AuditHistories.Add(new AuditHistory { UserId = sue, PointsHistory = old, Resoning = "Old award" });
            await db.SaveChangesAsync();
        }
        await client.PostAsJsonAsync($"/users/{bob}/cart", new { name = "Mug", price = 1.00m, description = "A mug" });
        await client.PostAsync($"/users/{bob}/cart/send-order", null);

        var summary = await client.GetFromJsonAsync<PointsSummary>($"/users/{bob}/points");

        Assert.Equal(260, summary!.Balance);
        Assert.Equal(300, summary.EarnedThisMonth);
        Assert.Equal(800, summary.Earned);
        Assert.Equal(100, summary.Spent);
        Assert.Equal(40, summary.Deducted);
        Assert.Equal(4, summary.History.Count);
        Assert.Contains(summary.History, e => e.Points == -40 && e.Reason == "Late delivery" && e.By == "sue");
        Assert.Contains(summary.History, e => e.Points == -100 && e.Reason == "Order" && e.By == null);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
