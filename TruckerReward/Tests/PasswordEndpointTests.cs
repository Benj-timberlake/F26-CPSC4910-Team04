using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class PasswordEndpointTests : IAsyncDisposable
{
    private readonly TestApp app = new();

    private async Task<(HttpClient client, int id)> StartWithBob()
    {
        var client = await app.Start();
        await client.PostAsJsonAsync("/auth/register", new { username = "bob", firstName = "Bob", lastName = "Driver", email = "bob@example.com", password = "Hunter22x!", userType = "driver" });
        await client.PostAsJsonAsync("/auth/register", new { username = "root", firstName = "Root", lastName = "Admin", email = "root@example.com", password = "Hunter22x!", userType = "driver" });
        await app.ConfirmEmails();
        using (var db = app.Db())
        {
            (await db.Users.SingleAsync(u => u.Username == "root")).UserType = "admin";
            await db.SaveChangesAsync();
        }
        var id = (await app.Db().Users.SingleAsync(u => u.Username == "bob")).Id;
        return (client, id);
    }

    private async Task<HttpStatusCode> Login(HttpClient client, string password) =>
        (await client.PostAsJsonAsync("/auth/login", new { username = "bob", password })).StatusCode;

    [Fact]
    public async Task ChangeNeedsTheCurrentPassword()
    {
        var (client, id) = await StartWithBob();
        var response = await client.PostAsJsonAsync($"/users/{id}/password", new { currentPassword = "nope", newPassword = "NewPass99!" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await Login(client, "Hunter22x!"));
        Assert.Empty(await app.Db().PasswordChanges.ToListAsync());
    }

    [Fact]
    public async Task ChangeAppliesThePolicy()
    {
        var (client, id) = await StartWithBob();
        var response = await client.PostAsJsonAsync($"/users/{id}/password", new { currentPassword = "Hunter22x!", newPassword = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangeSwapsThePasswordLogsItAndEmailsTheUser()
    {
        var (client, id) = await StartWithBob();
        var response = await client.PostAsJsonAsync($"/users/{id}/password", new { currentPassword = "Hunter22x!", newPassword = "NewPass99!", ipAddress = "10.0.0.5" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await Login(client, "Hunter22x!"));
        Assert.Equal(HttpStatusCode.OK, await Login(client, "NewPass99!"));

        var change = await app.Db().PasswordChanges.SingleAsync();
        Assert.Equal(PasswordChange.Changed, change.ChangeType);
        Assert.Equal(id, change.UserId);
        Assert.Equal("10.0.0.5", change.IpAddress);

        var mail = Assert.Single(app.Email.Sent);
        Assert.Equal("bob@example.com", mail.To);
        Assert.Contains("changed", mail.Subject);
    }

    [Fact]
    public async Task SsoOnlyAccountCanSetAFirstPassword()
    {
        var client = await app.Start();
        await client.PostAsJsonAsync("/auth/external", new { provider = "Google", email = "sue@gmail.com" });
        var id = (await app.Db().Users.SingleAsync()).Id;
        var response = await client.PostAsJsonAsync($"/users/{id}/password", new { newPassword = "NewPass99!" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/login", new { username = "sue", password = "NewPass99!" })).StatusCode);
    }

    [Fact]
    public async Task ForgotAnswersTheSameForUnknownEmailsAndSendsNothing()
    {
        var (client, _) = await StartWithBob();
        var known = await client.PostAsJsonAsync("/auth/forgot", new { email = "bob@example.com" });
        var unknown = await client.PostAsJsonAsync("/auth/forgot", new { email = "nobody@example.com" });
        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(1, await app.Db().PasswordResets.CountAsync());
    }

    [Fact]
    public async Task ForgotEmailsALinkLogsTheRequestAndTellsAdmins()
    {
        var (client, id) = await StartWithBob();
        await client.PostAsJsonAsync("/auth/forgot", new { email = "bob@example.com", ipAddress = "10.0.0.5" });

        var toBob = Assert.Single(app.Email.Sent, m => m.To == "bob@example.com");
        Assert.Contains("http://frontend/reset-password?token=", toBob.Body);
        var toAdmin = Assert.Single(app.Email.Sent, m => m.To == "root@example.com");
        Assert.Contains("bob", toAdmin.Subject);
        Assert.True(await app.Db().NotificationsHistories.AnyAsync(n => n.User.Username == "root" && n.Subject == toAdmin.Subject));

        var change = await app.Db().PasswordChanges.SingleAsync();
        Assert.Equal(PasswordChange.ResetRequested, change.ChangeType);
        Assert.Equal(id, change.UserId);

        // the token itself never touches the database
        var token = TokenFrom(toBob.Body);
        var reset = await app.Db().PasswordResets.SingleAsync();
        Assert.DoesNotContain(token, reset.TokenHash);
        Assert.Equal(64, reset.TokenHash.Length);
    }

    [Fact]
    public async Task ResetWithTheLinkSetsANewPasswordOnce()
    {
        var (client, _) = await StartWithBob();
        await client.PostAsJsonAsync("/auth/forgot", new { email = "bob@example.com" });
        var token = TokenFrom(app.Email.Sent.Single(m => m.To == "bob@example.com").Body);

        var first = await client.PostAsJsonAsync("/auth/reset", new { token, newPassword = "NewPass99!", ipAddress = "10.0.0.5" });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await Login(client, "NewPass99!"));
        Assert.Contains(app.Email.Sent, m => m.To == "bob@example.com" && m.Subject.Contains("reset", StringComparison.OrdinalIgnoreCase) && !m.Body.Contains("token="));

        var second = await client.PostAsJsonAsync("/auth/reset", new { token, newPassword = "Another11!" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await Login(client, "NewPass99!"));

        var types = await app.Db().PasswordChanges.OrderBy(c => c.Id).Select(c => c.ChangeType).ToListAsync();
        Assert.Equal([PasswordChange.ResetRequested, PasswordChange.ResetCompleted], types);
    }

    [Fact]
    public async Task ResetLinkExpiresAfterAnHour()
    {
        var (client, _) = await StartWithBob();
        await client.PostAsJsonAsync("/auth/forgot", new { email = "bob@example.com" });
        var token = TokenFrom(app.Email.Sent.Single(m => m.To == "bob@example.com").Body);
        app.Clock.Advance(PasswordEndpoints.ResetLinkLifetime + TimeSpan.FromSeconds(1));

        var response = await client.PostAsJsonAsync("/auth/reset", new { token, newPassword = "NewPass99!" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await Login(client, "Hunter22x!"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task ResetWithABadTokenIsRejected(string token)
    {
        var (client, _) = await StartWithBob();
        var response = await client.PostAsJsonAsync("/auth/reset", new { token, newPassword = "NewPass99!" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminListShowsChangesNewestFirstWithUsernames()
    {
        var (client, id) = await StartWithBob();
        await client.PostAsJsonAsync($"/users/{id}/password", new { currentPassword = "Hunter22x!", newPassword = "NewPass99!" });
        app.Clock.Advance(TimeSpan.FromMinutes(1));
        await client.PostAsJsonAsync("/auth/forgot", new { email = "bob@example.com" });

        var list = await client.GetFromJsonAsync<List<PasswordChangeEntry>>("/admin/password-changes");
        Assert.Equal(2, list!.Count);
        Assert.Equal(PasswordChange.ResetRequested, list[0].ChangeType);
        Assert.Equal("bob", list[0].Username);
        Assert.Equal(PasswordChange.Changed, list[1].ChangeType);
    }

    [Fact]
    public async Task AdminSendsAResetLinkThatWorksAndIsLoggedWithTheirName()
    {
        var (client, id) = await StartWithBob();
        var rootId = (await app.Db().Users.SingleAsync(u => u.Username == "root")).Id;

        var sent = await client.PostAsJsonAsync($"/admin/accounts/{id}/reset-link", new { adminId = rootId });

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var token = TokenFrom(Assert.Single(app.Email.Sent, m => m.To == "bob@example.com").Body);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/auth/reset", new { token, newPassword = "NewPass99!" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await Login(client, "NewPass99!"));
        var changes = await client.GetFromJsonAsync<List<PasswordChangeEntry>>("/admin/password-changes");
        var request = Assert.Single(changes!, c => c.ChangeType == PasswordChange.ResetRequested);
        Assert.Equal("root", request.RequestedBy);
        var audit = await client.GetFromJsonAsync<List<AuditEntry>>($"/users/{rootId}/audit?category=passwords");
        Assert.Contains(audit!, e => e.Username == "bob" && e.By == "root");
    }

    [Theory]
    [InlineData(false, "active", HttpStatusCode.Forbidden)]
    [InlineData(true, "inactive", HttpStatusCode.Conflict)]
    public async Task OnlyAdminsSendResetLinksToActiveAccounts(bool asAdmin, string status, HttpStatusCode expected)
    {
        var (client, id) = await StartWithBob();
        using (var db = app.Db())
        {
            (await db.Users.SingleAsync(u => u.Id == id)).Status = status;
            await db.SaveChangesAsync();
        }
        var actor = (await app.Db().Users.SingleAsync(u => u.Username == (asAdmin ? "root" : "bob"))).Id;

        var response = await client.PostAsJsonAsync($"/admin/accounts/{id}/reset-link", new { adminId = actor });

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(app.Email.Sent);
        Assert.Empty(await app.Db().PasswordResets.ToListAsync());
    }

    [Fact]
    public async Task ChangeSignsOutOtherBrowsersButNotThisOne()
    {
        var (client, id) = await StartWithBob();
        var here = await Session(client, id);
        var other = await Session(client, id);
        var before = (await app.Db().Users.SingleAsync(u => u.Id == id)).SessionVersion;

        var response = await client.PostAsJsonAsync($"/users/{id}/password", new { currentPassword = "Hunter22x!", newPassword = "NewPass99!", sessionKey = here });

        var profile = await response.Content.ReadFromJsonAsync<UserProfile>();
        Assert.Equal(before + 1, profile!.SessionVersion);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/sessions/check", new { key = here })).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync("/sessions/check", new { key = other })).StatusCode);
    }

    [Fact]
    public async Task ResetSignsOutEveryBrowserAndVoidsOtherLinks()
    {
        var (client, id) = await StartWithBob();
        var session = await Session(client, id);
        await client.PostAsJsonAsync("/auth/forgot", new { email = "bob@example.com" });
        await client.PostAsJsonAsync("/auth/forgot", new { email = "bob@example.com" });
        var links = app.Email.Sent.Where(m => m.To == "bob@example.com").Select(m => TokenFrom(m.Body)).ToList();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/auth/reset", new { token = links[1], newPassword = "NewPass99!" })).StatusCode);

        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync("/sessions/check", new { key = session })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/auth/reset", new { token = links[0], newPassword = "Another11!" })).StatusCode);
    }

    private async Task<string> Session(HttpClient client, int id)
    {
        var response = await client.PostAsJsonAsync($"/users/{id}/sessions", new { ipAddress = "10.0.0.1", userAgent = "test" });
        return (await response.Content.ReadFromJsonAsync<Started>())!.Key;
    }

    private sealed record Started(string Key);

    private static string TokenFrom(string body) =>
        body.Split("token=")[1].Split('\n')[0].Trim();

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
