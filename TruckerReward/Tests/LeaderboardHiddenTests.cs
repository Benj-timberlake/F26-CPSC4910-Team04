using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class LeaderboardHiddenTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob, amy, cal, sue;

    private async Task Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var acme = new Company { Name = "Acme" };
        db.Add(acme);
        await db.SaveChangesAsync();
        User[] users = [User("bob", "driver", acme.Id), User("amy", "driver", acme.Id), User("cal", "driver", acme.Id), User("sue", "sponsor", acme.Id)];
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        (bob, amy, cal, sue) = (users[0].Id, users[1].Id, users[2].Id, users[3].Id);
        db.PointsHistory.AddRange(Entry(bob, acme.Id, 100), Entry(amy, acme.Id, 300), Entry(cal, acme.Id, 200));
        await db.SaveChangesAsync();
    }

    private static PointsHistory Entry(int user, int company, int delta) =>
        new() { UserId = user, CompanyId = company, PointsDelta = delta, Timestamp = new DateTime(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc) };

    private static User User(string name, string type, int companyId) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x",
        AccountsCompanies = [new AccountsCompany { CompanyId = companyId }]
    };

    private Task<Leaderboard> Board(int viewer) => client.GetFromJsonAsync<Leaderboard>($"/users/{viewer}/leaderboard")!;

    private async Task Hide(int driver, bool hidden = true) =>
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/users/{driver}/leaderboard/hidden", new { hidden })).StatusCode);

    [Fact]
    public async Task HiddenIsOffByDefault()
    {
        await Start();
        var board = await Board(bob);
        Assert.False(board.YouHidden);
        Assert.All(board.Rows, r => Assert.False(r.Hidden));
    }

    [Fact]
    public async Task OtherDriversSeeAHiddenDriverAsAnonymousWithTheirRankAndPoints()
    {
        await Start();
        await Hide(amy);

        var board = await Board(bob);

        Assert.Equal(["Anonymous driver 300 1 True", "cal 200 2 False", "bob 100 3 False"],
            board.Rows.Select(r => $"{r.Username} {r.Points} {r.Rank} {r.Hidden}"));
        var anonymous = board.Rows[0];
        Assert.Equal(0, anonymous.Id);
        Assert.Equal("", anonymous.FirstName + anonymous.LastName);
    }

    [Fact]
    public async Task HiddenDriversStillSeeThemselves()
    {
        await Start();
        await Hide(amy);

        var board = await Board(amy);

        Assert.True(board.YouHidden);
        Assert.Equal(1, board.YourRank);
        Assert.Equal("amy", board.Rows[0].Username);
    }

    [Fact]
    public async Task SponsorsStillSeeNames()
    {
        await Start();
        await Hide(amy);

        var board = await Board(sue);

        Assert.Equal("amy", board.Rows[0].Username);
        Assert.True(board.Rows[0].Hidden);
    }

    [Fact]
    public async Task ShowingAgainClearsTheFlag()
    {
        await Start();
        await Hide(amy);
        await Hide(amy, false);

        Assert.False((await app.Db().Users.SingleAsync(u => u.Id == amy)).LeaderboardHidden);
        Assert.Equal("amy", (await Board(bob)).Rows[0].Username);
    }

    [Fact]
    public async Task OnlyDriversCanHide()
    {
        await Start();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/users/{sue}/leaderboard/hidden", new { hidden = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/users/9999/leaderboard/hidden", new { hidden = true })).StatusCode);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
