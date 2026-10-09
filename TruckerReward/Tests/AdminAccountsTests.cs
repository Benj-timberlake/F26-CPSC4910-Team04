using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AdminAccountsTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private readonly DateTime t0 = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private async Task Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var users = new[]
        {
            User("bob", "Bob", "Baker", "driver"),
            User("amy", "Amy", "Zane", "driver", BackEnd.Models.User.Inactive),
            User("sue", "Sue", "Moss", "sponsor"),
            User("ann", "Ann", "Admin", "admin")
        };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        // stand-in for the live users insert trigger
        for (var i = 0; i < users.Length; i++)
            db.AccountsHistories.Add(History(users[i], t0.AddDays(i)));
        db.AccountsHistories.Add(History(users[0], t0.AddDays(10)));
        db.LoginAttempts.AddRange(
            new LoginAttempt { Username = "bob", UserId = users[0].Id, Succeeded = true, IpAddress = "", AttemptedAt = t0.AddDays(5) },
            new LoginAttempt { Username = "bob", UserId = users[0].Id, Succeeded = false, IpAddress = "", AttemptedAt = t0.AddDays(9) },
            new LoginAttempt { Username = "sue", UserId = users[2].Id, Succeeded = true, IpAddress = "", AttemptedAt = t0.AddDays(7) });
        await db.SaveChangesAsync();
    }

    private static User User(string name, string first, string last, string type, string status = "active") => new()
    {
        Username = name, FirstName = first, LastName = last, Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x", Status = status
    };

    private static AccountsHistory History(User u, DateTime at) => new()
    {
        UserId = u.Id, Timestamp = at, UserType = u.UserType, Username = u.Username, Password = "x", Email = u.Email,
        FirstName = u.FirstName, LastName = u.LastName
    };

    private async Task<List<AdminAccount>> List(string query = "") =>
        (await client.GetFromJsonAsync<List<AdminAccount>>("/admin/accounts" + query))!;

    [Fact]
    public async Task ListsEveryAccountByNameWithCreatedAndLastLogin()
    {
        await Start();

        var all = await List();

        Assert.Equal(["ann", "bob", "sue", "amy"], all.Select(a => a.Username));
        var bob = all.Single(a => a.Username == "bob");
        Assert.Equal(t0, bob.CreatedAt);
        Assert.Equal(t0.AddDays(5), bob.LastLoginAt);
        Assert.Null(all.Single(a => a.Username == "amy").LastLoginAt);
        Assert.Equal("inactive", all.Single(a => a.Username == "amy").Status);
    }

    [Theory]
    [InlineData("?search=BAKER", "bob")]
    [InlineData("?search=bob baker", "bob")]
    [InlineData("?search=sue@example", "sue")]
    [InlineData("?search=amy", "amy")]
    [InlineData("?type=sponsor", "sue")]
    [InlineData("?status=inactive", "amy")]
    [InlineData("?type=driver&status=active", "bob")]
    public async Task SearchesAndFilters(string query, string expected)
    {
        await Start();
        Assert.Equal(expected, Assert.Single(await List(query)).Username);
    }

    [Fact]
    public async Task SortsByCreatedOrLastLogin()
    {
        await Start();

        Assert.Equal(["ann", "sue", "amy", "bob"], (await List("?sort=created")).Select(a => a.Username));
        Assert.Equal(["sue", "bob"], (await List("?sort=last-login")).Select(a => a.Username).Take(2));
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
