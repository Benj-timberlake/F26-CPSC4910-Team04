using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class UsernameEndpointTests : IAsyncDisposable
{
    private readonly TestApp app = new();

    private async Task<(HttpClient client, int id)> StartWithBob()
    {
        var client = await app.Start();
        await client.PostAsJsonAsync("/auth/register", new { username = "bob", firstName = "Bob", lastName = "Driver", email = "bob@example.com", password = "Hunter22x!", userType = "driver" });
        await client.PostAsJsonAsync("/auth/register", new { username = "sue", firstName = "Sue", lastName = "Driver", email = "sue@example.com", password = "Hunter22x!", userType = "driver" });
        var id = (await app.Db().Users.SingleAsync(u => u.Username == "bob")).Id;
        return (client, id);
    }

    private static Task<HttpResponseMessage> Rename(HttpClient client, int id, string? newUsername, string? currentPassword = "Hunter22x!") =>
        client.PostAsJsonAsync($"/users/{id}/username", new { currentPassword, newUsername });

    private static async Task<HttpStatusCode> Login(HttpClient client, string username) =>
        (await client.PostAsJsonAsync("/auth/login", new { username, password = "Hunter22x!" })).StatusCode;

    private async Task<string> UsernameOf(int id) => (await app.Db().Users.SingleAsync(u => u.Id == id)).Username;

    [Fact]
    public async Task RenameChangesTheLoginNameAndEmailsTheUser()
    {
        var (client, id) = await StartWithBob();
        var response = await Rename(client, id, "  bobby  ");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content.ReadFromJsonAsync<UserProfile>();
        Assert.Equal("bobby", profile!.Username);
        Assert.Equal(id, profile.Id);
        Assert.Equal("bobby", await UsernameOf(id));
        Assert.Equal(HttpStatusCode.OK, await Login(client, "bobby"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Login(client, "bob"));

        var mail = Assert.Single(app.Email.Sent);
        Assert.Equal("bob@example.com", mail.To);
        Assert.Contains("from bob to bobby", mail.Body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nope")]
    public async Task RenameNeedsTheCurrentPassword(string? currentPassword)
    {
        var (client, id) = await StartWithBob();
        var response = await Rename(client, id, "bobby", currentPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("bob", await UsernameOf(id));
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task TakenUsernameIsRejected()
    {
        var (client, id) = await StartWithBob();
        var response = await Rename(client, id, "sue");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("bob", await UsernameOf(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bob")]
    public async Task BlankOrUnchangedUsernameIsRejected(string? newUsername)
    {
        var (client, id) = await StartWithBob();
        var response = await Rename(client, id, newUsername);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bob", await UsernameOf(id));
    }

    [Theory]
    [InlineData(255, HttpStatusCode.OK)]
    [InlineData(256, HttpStatusCode.BadRequest)]
    public async Task UsernameFitsTheColumn(int length, HttpStatusCode expected)
    {
        var (client, id) = await StartWithBob();
        var response = await Rename(client, id, new string('b', length));
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task UnknownUserIsNotFound()
    {
        var (client, _) = await StartWithBob();
        var response = await Rename(client, 999, "bobby");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
