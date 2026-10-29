using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Xunit;

namespace TruckerReward.Tests;

public sealed class LeaderboardTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob, amy, sue, ann, loner, acme;

    // the week of the thursday 2026-09-17 test clock starts on the 14th
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
            User("bob", "driver", a.Id, 100), User("amy", "driver", a.Id, 300), User("cal", "driver", a.Id, 100),
            User("dee", "driver", a.Id, 999, BackEnd.Models.User.Inactive), User("tom", "driver", o.Id, 500),
            User("sue", "sponsor", a.Id), User("ann", "admin", null), User("lou", "driver", null)
        };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        (bob, amy, sue, ann, loner, acme) = (users[0].Id, users[1].Id, users[5].Id, users[6].Id, users[7].Id, a.Id);
        db.PointsHistory.AddRange(
            History(bob, 50, 15), History(bob, -30, 16), History(users[2].Id, 40, 16),
            History(amy, 200, 5), new PointsHistory { UserId = amy, PointsDelta = 500, Timestamp = new DateTime(2026, 8, 20) });
        await db.SaveChangesAsync();
    }

    private static PointsHistory History(int user, int delta, int day) =>
        new() { UserId = user, PointsDelta = delta, Timestamp = new DateTime(2026, 9, day, 9, 0, 0, DateTimeKind.Utc) };

    private static User User(string name, string type, int? companyId, int points = 0, string status = "active") => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x", Points = points, Status = status,
        AccountsCompanies = companyId is int company ? [new AccountsCompany { CompanyId = company }] : []
    };

    private async Task<Leaderboard> Board(int viewer, string query = "") =>
        (await client.GetFromJsonAsync<Leaderboard>($"/users/{viewer}/leaderboard{query}"))!;

    [Fact]
    public async Task DriverSeesTheirSponsorsBoardAndTheirRank()
    {
        await Start();

        var board = await Board(bob);

        Assert.Equal(["amy 300 1", "bob 100 2", "cal 100 2"], board.Rows.Select(r => $"{r.Username} {r.Points} {r.Rank}"));
        Assert.Equal(2, board.YourRank);
    }

    [Theory]
    [InlineData("?period=month", "amy 200,bob 50,cal 40")]
    [InlineData("?period=week", "bob 50,cal 40,amy 0")]
    public async Task PeriodsCountOnlyPointsEarnedInThem(string query, string expected)
    {
        await Start();

        var board = await Board(sue, query);

        Assert.Equal(expected, string.Join(",", board.Rows.Select(r => $"{r.Username} {r.Points}")));
        Assert.Null(board.YourRank);
    }

    [Fact]
    public async Task CategoriesRankBySpendingOrOrders()
    {
        await Start();
        using (var db = app.Db())
        {
            var orders = new[] { History(bob, -200, 15), History(bob, -100, 16), History(amy, -250, 2) };
            db.PointsHistory.AddRange(orders);
            await db.SaveChangesAsync();
            db.CartItems.AddRange(orders.Select(o => new CartItem { UserId = o.UserId, Name = "Mug", Price = 1m, PointsHistoryId = o.Id }));
            await db.SaveChangesAsync();
        }

        var spent = await Board(sue, "?category=spent");
        var weekOrders = await Board(sue, "?period=week&category=orders");

        Assert.Equal("bob 300,amy 250,cal 0", string.Join(",", spent.Rows.Select(r => $"{r.Username} {r.Points}")));
        Assert.Equal("bob 2,amy 0,cal 0", string.Join(",", weekOrders.Rows.Select(r => $"{r.Username} {r.Points}")));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/users/{sue}/leaderboard?category=miles")).StatusCode);
    }

    [Fact]
    public async Task AdminsPickTheSponsor()
    {
        await Start();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/users/{ann}/leaderboard")).StatusCode);
        Assert.Equal(3, (await Board(ann, $"?companyId={acme}")).Rows.Count);
    }

    [Fact]
    public async Task BadRequestsAreRefused()
    {
        await Start();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/users/{bob}/leaderboard?period=year")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/users/{loner}/leaderboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/users/999/leaderboard")).StatusCode);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
