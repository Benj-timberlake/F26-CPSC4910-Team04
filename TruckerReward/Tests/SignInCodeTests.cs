using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class SignInCodeTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob;

    private async Task Start(bool codes = true)
    {
        client = await app.Start();
        await client.PostAsJsonAsync("/auth/register", new { username = "bob", firstName = "Bob", lastName = "Driver", email = "bob@example.com", password = "Hunter22x!", userType = "driver" });
        await app.ConfirmEmails();
        using var db = app.Db();
        var user = await db.Users.SingleAsync();
        user.EmailCodes = codes;
        await db.SaveChangesAsync();
        bob = user.Id;
    }

    private async Task<string> Challenge()
    {
        var response = await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = "Hunter22x!" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Pending>())!.Challenge;
    }

    private string LastCode() =>
        System.Text.RegularExpressions.Regex.Match(app.Email.Sent.Last().Subject, "[0-9]{6}").Value;

    private Task<HttpResponseMessage> Code(string challenge, string code) =>
        client.PostAsJsonAsync("/auth/login/code", new { challenge, code });

    [Fact]
    public async Task PasswordThenEmailedCodeSignsIn()
    {
        await Start();

        var challenge = await Challenge();

        Assert.Equal("bob@example.com", app.Email.Sent.Single().To);
        Assert.Empty(await app.Db().LoginAttempts.ToListAsync());
        var signedIn = await Code(challenge, LastCode());
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal("bob", (await signedIn.Content.ReadFromJsonAsync<UserProfile>())!.Username);
        Assert.True((await app.Db().LoginAttempts.SingleAsync()).Succeeded);
        Assert.Equal(HttpStatusCode.Gone, (await Code(challenge, LastCode())).StatusCode);
    }

    [Fact]
    public async Task WrongCodesCountAsFailedSignInsAndUseUpTheCode()
    {
        await Start();
        var challenge = await Challenge();
        var right = LastCode();
        var wrong = right == "000000" ? "111111" : "000000";

        for (var i = 1; i < SignInCodeEndpoints.MaxAttempts; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await Code(challenge, wrong)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await Code(challenge, wrong)).StatusCode);

        Assert.Equal(SignInCodeEndpoints.MaxAttempts, await app.Db().LoginAttempts.CountAsync(a => !a.Succeeded));
        Assert.NotEqual(HttpStatusCode.OK, (await Code(challenge, right)).StatusCode);
    }

    [Fact]
    public async Task CodesExpireAndCantBeAskedForEndlessly()
    {
        await Start();
        var challenge = await Challenge();
        var code = LastCode();
        app.Clock.Advance(SignInCodeEndpoints.Lifetime + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.Gone, (await Code(challenge, code)).StatusCode);

        for (var i = 0; i < SignInCodeEndpoints.MaxIssued; i++)
            await Challenge();
        var flooded = await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = "Hunter22x!" });

        Assert.Equal((HttpStatusCode)429, flooded.StatusCode);
        Assert.Equal(1 + SignInCodeEndpoints.MaxIssued, app.Email.Sent.Count);
    }

    [Fact]
    public async Task WithCodesOffOrViaSsoThePasswordIsEnough()
    {
        await Start(codes: false);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = "Hunter22x!" })).StatusCode);

        using (var db = app.Db())
            await db.Users.ExecuteUpdateAsync(u => u.SetProperty(x => x.EmailCodes, true));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/external", new { provider = "Google", email = "bob@example.com" })).StatusCode);
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task TurningCodesOnNeedsThePasswordAndAConfirmedEmail()
    {
        await Start(codes: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/users/{bob}/sign-in-codes", new { enabled = true, currentPassword = "nope" })).StatusCode);
        using (var db = app.Db())
            await db.Users.ExecuteUpdateAsync(u => u.SetProperty(x => x.EmailConfirmedAt, (DateTime?)null));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/users/{bob}/sign-in-codes", new { enabled = true, currentPassword = "Hunter22x!" })).StatusCode);
        await app.ConfirmEmails();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/users/{bob}/sign-in-codes", new { enabled = true, currentPassword = "Hunter22x!" })).StatusCode);
        Assert.True((await app.Db().Users.SingleAsync()).EmailCodes);
        Assert.Equal("Sign-in codes are on", Assert.Single(app.Email.Sent).Subject);
        await Challenge();
    }

    private sealed record Pending(string Challenge);

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
