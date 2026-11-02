using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class EmailConfirmationTests : IAsyncDisposable
{
    private readonly TestApp app = new();

    private async Task<(HttpClient client, int id)> Register()
    {
        var client = await app.Start();
        await client.Register("bob", "Bob", "Driver");
        return (client, (await app.Db().Users.SingleAsync()).Id);
    }

    private static string TokenFrom(string body) =>
        System.Text.RegularExpressions.Regex.Match(body, "token=([A-Za-z0-9_-]+)").Groups[1].Value;

    [Fact]
    public async Task SignUpEmailsALinkThatConfirmsTheAddressOnce()
    {
        var (client, id) = await Register();
        var mail = Assert.Single(app.Email.Sent);
        Assert.Equal(("bob@example.com", "Confirm your TruckerReward email"), (mail.To, mail.Subject));
        Assert.Null((await app.Db().Users.SingleAsync()).EmailConfirmedAt);
        Assert.Empty(await app.Db().NotificationsHistories.Where(n => n.Body.Contains("token=")).ToListAsync());

        var token = TokenFrom(mail.Body);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/confirm-email", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/auth/confirm-email", new { token })).StatusCode);
        Assert.Equal(app.Clock.GetUtcNow().UtcDateTime, (await app.Db().Users.SingleAsync()).EmailConfirmedAt);
        Assert.True((await client.GetFromJsonAsync<UserDetails>($"/users/{id}"))!.EmailConfirmed);
    }

    [Fact]
    public async Task LinksExpireAndDieWhenTheAddressChanges()
    {
        var (client, id) = await Register();
        var first = TokenFrom(app.Email.Sent.Single().Body);
        app.Clock.Advance(EmailConfirmEndpoints.LinkLifetime + TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/auth/confirm-email", new { token = first })).StatusCode);

        await client.PostAsync($"/users/{id}/email/confirmation", null);
        var second = TokenFrom(app.Email.Sent.Last().Body);
        await client.PostAsJsonAsync($"/users/{id}/email", new { currentPassword = "Hunter22x!", newEmail = "rob@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/auth/confirm-email", new { token = second })).StatusCode);
        Assert.Equal("rob@example.com", app.Email.Sent.Last().To);
        Assert.Null((await app.Db().Users.SingleAsync()).EmailConfirmedAt);
    }

    [Fact]
    public async Task AlertsWaitForAConfirmedAddressButSecurityMailDoesNot()
    {
        var (client, root) = await Register();
        using (var db = app.Db())
        {
            await db.Users.ExecuteUpdateAsync(u => u.SetProperty(x => x.UserType, AuthEndpoints.Admin));
            db.NotificationPreferences.Add(new NotificationPreference { UserId = root, Category = NotificationCategory.NewAccounts.Key, Enabled = true, Emailed = true });
            await db.SaveChangesAsync();
        }
        app.Email.Sent.Clear();

        await client.Register("amy", "Amy", "Driver");
        await client.PostAsJsonAsync("/auth/forgot", new { email = "bob@example.com" });

        // amy's own confirmation and bob's reset link, but no new-account alert to unconfirmed bob
        Assert.Equal(["amy@example.com", "bob@example.com"], app.Email.Sent.Select(m => m.To));
        Assert.Contains(await app.Db().NotificationsHistories.ToListAsync(), n => n.UserId == root && n.Subject == "New driver account: amy");

        await app.ConfirmEmails();
        await client.Register("cal", "Cal", "Driver");
        Assert.Contains(app.Email.Sent, m => m.To == "bob@example.com" && m.Subject == "New driver account: cal");
    }

    [Fact]
    public async Task ResettingThePasswordFromTheEmailAlsoConfirmsIt()
    {
        var (client, _) = await Register();
        await client.PostAsJsonAsync("/auth/forgot", new { email = "bob@example.com" });
        var token = TokenFrom(app.Email.Sent.Single(m => m.Subject.Contains("Reset")).Body);

        await client.PostAsJsonAsync("/auth/reset", new { token, newPassword = "NewPass99!" });

        Assert.NotNull((await app.Db().Users.SingleAsync()).EmailConfirmedAt);
    }

    [Fact]
    public async Task SsoAddressesCountAsConfirmed()
    {
        var client = await app.Start();
        await client.PostAsJsonAsync("/auth/external", new { provider = "Google", email = "sue@gmail.com" });
        Assert.NotNull((await app.Db().Users.SingleAsync()).EmailConfirmedAt);
        Assert.Empty(app.Email.Sent);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
