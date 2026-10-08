using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class NotificationsTests : IAsyncDisposable
{
    private readonly TestApp app = new();

    private async Task<(HttpClient client, int id)> StartWithBob()
    {
        var client = await app.Start();
        await client.PostAsJsonAsync("/auth/register", new { username = "bob", firstName = "Bob", lastName = "Driver", email = "bob@example.com", password = "Hunter22x!", userType = "driver" });
        var id = (await app.Db().Users.SingleAsync(u => u.Username == "bob")).Id;
        return (client, id);
    }

    [Fact]
    public async Task ResetLinkIsEmailedButNeverStored()
    {
        var (client, id) = await StartWithBob();
        await client.PostAsJsonAsync("/auth/forgot", new { email = "bob@example.com" });

        var mail = Assert.Single(app.Email.Sent, m => m.To == "bob@example.com");
        Assert.Contains("reset-password?token=", mail.Body);

        var saved = await app.Db().NotificationsHistories.SingleAsync();
        Assert.Equal(id, saved.UserId);
        Assert.Equal(mail.Subject, saved.Subject);
        Assert.DoesNotContain("token=", saved.Body);
        Assert.False(saved.BeenRead);
        Assert.Equal(app.Clock.GetUtcNow().UtcDateTime, saved.Timestamp);
    }

    [Fact]
    public async Task PasswordChangeIsEmailedAndStored()
    {
        var (client, id) = await StartWithBob();
        var response = await client.PostAsJsonAsync($"/users/{id}/password", new { currentPassword = "Hunter22x!", newPassword = "NewPass99!" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var mail = Assert.Single(app.Email.Sent);
        var saved = await app.Db().NotificationsHistories.SingleAsync();
        Assert.Equal(id, saved.UserId);
        Assert.Equal(mail.Subject, saved.Subject);
        Assert.Equal(mail.Body, saved.Body);
    }

    [Fact]
    public async Task UnknownEmailGetsNoNotification()
    {
        var (client, _) = await StartWithBob();
        await client.PostAsJsonAsync("/auth/forgot", new { email = "nobody@example.com" });

        Assert.Empty(app.Email.Sent);
        Assert.Empty(await app.Db().NotificationsHistories.ToListAsync());
    }

    [Fact]
    public async Task AdminsAreToldAboutNewAccountsOnTheSiteOnly()
    {
        var client = await app.Start();
        foreach (var name in new[] { "ann", "max" })
            await client.PostAsJsonAsync("/auth/register", new { username = name, firstName = name, lastName = "Admin", email = $"{name}@example.com", password = "Hunter22x!", userType = "driver" });
        using (var db = app.Db())
        {
            await db.Users.ExecuteUpdateAsync(u => u.SetProperty(x => x.UserType, "admin"));
        }

        var response = await client.PostAsJsonAsync("/auth/register", new { username = "bob", firstName = "Bob", lastName = "Driver", email = "bob@example.com", password = "Hunter22x!", userType = "driver" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var saved = await app.Db().NotificationsHistories.Include(n => n.User).ToListAsync();
        Assert.Equal(["ann", "max"], saved.Select(n => n.User.Username).Order());
        Assert.All(saved, n => Assert.Equal("New driver account: bob", n.Subject));
        Assert.All(saved, n => Assert.Contains("Bob Driver (bob, bob@example.com)", n.Body));
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task NoAdminsMeansNoNewAccountNotification()
    {
        await StartWithBob();
        Assert.Empty(await app.Db().NotificationsHistories.ToListAsync());
    }

    [Fact]
    public async Task HistoryFailureStillEmailsAndSucceeds()
    {
        var (client, _) = await StartWithBob();
        await app.Db().Database.ExecuteSqlRawAsync("DROP TABLE notifications_history;");

        var response = await client.PostAsJsonAsync("/auth/forgot", new { email = "bob@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(app.Email.Sent, m => m.To == "bob@example.com" && m.Body.Contains("reset-password?token="));
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
