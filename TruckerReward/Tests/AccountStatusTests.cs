using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AccountStatusTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob, ann, max;

    private async Task Start()
    {
        client = await app.Start();
        foreach (var name in new[] { "bob", "ann", "max" })
            await client.PostAsJsonAsync("/auth/register", new { username = name, firstName = name, lastName = "Test", email = $"{name}@example.com", password = "Hunter22x!", userType = "driver" });
        using var db = app.Db();
        await db.Users.Where(u => u.Username != "bob").ExecuteUpdateAsync(u => u.SetProperty(x => x.UserType, AuthEndpoints.Admin));
        await db.NotificationsHistories.ExecuteDeleteAsync();
        var ids = await db.Users.ToDictionaryAsync(u => u.Username, u => u.Id);
        (bob, ann, max) = (ids["bob"], ids["ann"], ids["max"]);
    }

    private Task<HttpResponseMessage> SetStatus(int id, int adminId, string? status) =>
        client.PostAsJsonAsync($"/admin/accounts/{id}/status", new { adminId, status });

    private Task<HttpResponseMessage> Login(string username = "bob") =>
        client.PostAsJsonAsync("/auth/login", new { username, password = "Hunter22x!" });

    [Fact]
    public async Task DeactivatedAccountsCantSignIn()
    {
        await Start();

        Assert.Equal(HttpStatusCode.NoContent, (await SetStatus(bob, ann, "inactive")).StatusCode);

        var response = await Login();
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(User.Inactive, (await app.Db().Users.SingleAsync(u => u.Id == bob)).Status);
        Assert.False((await app.Db().LoginAttempts.OrderBy(a => a.Id).LastAsync()).Succeeded);
        Assert.Equal("inactive", (await client.GetFromJsonAsync<AccountStatus>($"/users/{bob}/status"))!.Status);
    }

    [Fact]
    public async Task WrongPasswordStillLooksLikeAWrongPassword()
    {
        await Start();
        await SetStatus(bob, ann, "inactive");

        var response = await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = "nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReactivatedAccountsCanSignInAgain()
    {
        await Start();
        await SetStatus(bob, ann, "inactive");

        Assert.Equal(HttpStatusCode.NoContent, (await SetStatus(bob, ann, "active")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Login()).StatusCode);
    }

    [Fact]
    public async Task DeactivatedSsoAccountsAreRefused()
    {
        await Start();
        await SetStatus(bob, ann, "inactive");

        var response = await client.PostAsJsonAsync("/auth/external", new { provider = "Google", email = "bob@example.com", name = "Bob Test" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminsAreToldOnTheSite()
    {
        await Start();

        await SetStatus(bob, ann, "inactive");
        await SetStatus(bob, ann, "active");

        var saved = await app.Db().NotificationsHistories.Include(n => n.User).OrderBy(n => n.Id).ToListAsync();
        Assert.Equal(["ann", "max", "ann", "max"], saved.Select(n => n.User.Username));
        Assert.Equal("Account deactivated: bob", saved[0].Subject);
        Assert.Equal("ann deactivated the driver account bob (bob@example.com).", saved[0].Body);
        Assert.Equal("Account reactivated: bob", saved[2].Subject);
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task BadRequestsChangeNothing()
    {
        await Start();

        Assert.Equal(HttpStatusCode.BadRequest, (await SetStatus(bob, ann, "gone")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetStatus(ann, ann, "inactive")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetStatus(ann, bob, "inactive")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetStatus(999, ann, "inactive")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SetStatus(bob, ann, "active")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/users/999/status")).StatusCode);
        Assert.All(await app.Db().Users.ToListAsync(), u => Assert.Equal(User.Active, u.Status));
        Assert.Empty(await app.Db().NotificationsHistories.ToListAsync());
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
