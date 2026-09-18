using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class LockoutTests : IAsyncDisposable
{
    private readonly TestApp app = new();

    private async Task<HttpClient> Start()
    {
        var client = await app.Start();
        await client.PostAsJsonAsync("/auth/register", new { username = "bob", email = "bob@example.com", password = "Hunter22x", userType = "driver" });
        await client.PostAsJsonAsync("/auth/register", new { username = "root", email = "root@example.com", password = "Hunter22x", userType = "driver" });
        using var db = app.Db();
        (await db.Users.SingleAsync(u => u.Username == "root")).UserType = "admin";
        await db.SaveChangesAsync();
        return client;
    }

    private static async Task<HttpStatusCode> Login(HttpClient client, string password, string username = "bob") =>
        (await client.PostAsJsonAsync("/auth/login", new { username, password })).StatusCode;

    private async Task Fail(HttpClient client, int times)
    {
        for (var i = 0; i < times; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await Login(client, "wrong"));
            app.Clock.Advance(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task FourFailuresDoNotLock()
    {
        var client = await Start();
        await Fail(client, Lockout.MaxFailures - 1);
        Assert.Equal(HttpStatusCode.OK, await Login(client, "Hunter22x"));
    }

    [Fact]
    public async Task FifthFailureLocksEvenTheRightPassword()
    {
        var client = await Start();
        await Fail(client, Lockout.MaxFailures);
        var response = await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = "Hunter22x" });
        Assert.Equal(HttpStatusCode.Locked, response.StatusCode);
        // the refused attempt is still logged as a failure
        Assert.Equal(Lockout.MaxFailures + 1, await app.Db().LoginAttempts.CountAsync(a => !a.Succeeded));
    }

    [Fact]
    public async Task LockLiftsAfterThreeMinutes()
    {
        var client = await Start();
        await Fail(client, Lockout.MaxFailures);
        app.Clock.Advance(Lockout.Duration - TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.Locked, await Login(client, "Hunter22x"));
        app.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(HttpStatusCode.OK, await Login(client, "Hunter22x"));
    }

    [Fact]
    public async Task AttemptsWhileLockedDoNotExtendTheLock()
    {
        var client = await Start();
        await Fail(client, Lockout.MaxFailures);
        var lockStart = app.Clock.GetUtcNow().UtcDateTime - TimeSpan.FromSeconds(1);
        // keep hammering for most of the lock
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.Locked, await Login(client, "wrong"));
            app.Clock.Advance(TimeSpan.FromSeconds(8));
        }
        app.Clock.Advance(lockStart + Lockout.Duration + TimeSpan.FromSeconds(1) - app.Clock.GetUtcNow().UtcDateTime);
        Assert.Equal(HttpStatusCode.OK, await Login(client, "Hunter22x"));
    }

    [Fact]
    public async Task ASuccessfulLoginResetsTheCount()
    {
        var client = await Start();
        await Fail(client, Lockout.MaxFailures - 1);
        Assert.Equal(HttpStatusCode.OK, await Login(client, "Hunter22x"));
        await Fail(client, Lockout.MaxFailures - 1);
        Assert.Equal(HttpStatusCode.OK, await Login(client, "Hunter22x"));
    }

    [Fact]
    public async Task OldFailuresDoNotCount()
    {
        var client = await Start();
        await Fail(client, Lockout.MaxFailures - 1);
        app.Clock.Advance(Lockout.Duration + TimeSpan.FromSeconds(1));
        await Fail(client, 1);
        Assert.Equal(HttpStatusCode.OK, await Login(client, "Hunter22x"));
    }

    [Fact]
    public async Task LockoutIsPerUsername()
    {
        var client = await Start();
        await Fail(client, Lockout.MaxFailures);
        Assert.Equal(HttpStatusCode.OK, await Login(client, "Hunter22x", username: "root"));
    }

    [Fact]
    public async Task UnknownUsernamesLockTooSoTheyCannotBeToldApart()
    {
        var client = await Start();
        for (var i = 0; i < Lockout.MaxFailures; i++)
            await Login(client, "wrong", username: "ghost");
        Assert.Equal(HttpStatusCode.Locked, await Login(client, "wrong", username: "ghost"));
    }

    [Fact]
    public async Task AdminsGetOneEmailWhenTheLockStarts()
    {
        var client = await Start();
        await Fail(client, Lockout.MaxFailures);
        await Login(client, "wrong");
        await Login(client, "wrong");
        var mail = Assert.Single(app.Email.Sent);
        Assert.Equal("root@example.com", mail.To);
        Assert.Contains("bob", mail.Subject);
    }

    [Fact]
    public async Task LockedAccountsListShowsWhoIsLockedAndUntilWhen()
    {
        var client = await Start();
        await Fail(client, Lockout.MaxFailures);
        var locked = await client.GetFromJsonAsync<List<LockedAccount>>("/admin/locked-accounts");
        var entry = Assert.Single(locked!);
        Assert.Equal("bob", entry.Username);
        Assert.True(entry.LockedUntil > app.Clock.GetUtcNow().UtcDateTime);

        app.Clock.Advance(Lockout.Duration + TimeSpan.FromSeconds(1));
        Assert.Empty((await client.GetFromJsonAsync<List<LockedAccount>>("/admin/locked-accounts"))!);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
