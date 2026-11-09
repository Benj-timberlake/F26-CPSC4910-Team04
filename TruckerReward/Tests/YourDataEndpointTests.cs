using System.Net;
using FrontEnd.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace TruckerReward.Tests;

public sealed class YourDataEndpointTests : IAsyncDisposable
{
    private readonly JsonBackend backend = new();
    private WebApplication? app;

    private const string Bob = """{"id":8,"userType":"driver","username":"bob","email":"bob@example.com","phoneNumber":"","address":""}""";

    private async Task<(HttpClient Client, string Cookie)> Start(string type = "driver")
    {
        var builder = FrontendHost.Builder(backend);
        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapSignIn(new FrontEnd.Auth.UserProfile(5, type, "sue", "sue@example.com", "", ""));
        app.MapViewAs();
        app.MapAccountEndpoints();
        app.MapDataExport();
        await app.StartAsync();
        var client = app.GetTestClient();
        return (client, (await client.GetAsync("/signin")).AuthCookie());
    }

    private static FormUrlEncodedContent Confirm(string value) => new(new Dictionary<string, string> { ["confirmation"] = value });

    private static string? Cookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(c => c.StartsWith(".AspNetCore.Cookies=")).Select(c => c.Split(';')[0]).LastOrDefault()
            : null;

    [Fact]
    public async Task DownloadsTheSignedInUsersOwnJson()
    {
        backend.Routes["GET /users/5/export"] = (HttpStatusCode.OK, """{"profile":{"username":"sue"}}""");
        var (client, cookie) = await Start();

        var response = await client.SendWithCookie(HttpMethod.Get, "/account/data/export?id=9", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("truckerreward-data.json", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
        Assert.Equal("""{"profile":{"username":"sue"}}""", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(backend.Requests, r => r.Contains("/9/"));
    }

    [Fact]
    public async Task SignedOutVisitorsCantDownload()
    {
        await Start();
        var response = await app!.GetTestClient().GetAsync("/account/data/export");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(backend.Requests, r => r.Contains("export"));
    }

    [Fact]
    public async Task DeletingSignsOutAndSaysSo()
    {
        backend.Routes["DELETE /users/5/account"] = (HttpStatusCode.OK, """{"anonymized":true}""");
        var (client, cookie) = await Start();

        var response = await client.SendWithCookie(HttpMethod.Post, "/account/delete", cookie, Confirm("Hunter22x!"));

        Assert.Equal("/login?deleted=1", response.Headers.Location!.ToString());
        Assert.Contains("\"confirmation\":\"Hunter22x!\"", backend.Bodies["DELETE /users/5/account"]);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Cookies=;"));
    }

    [Fact]
    public async Task RefusedDeleteGoesBackWithTheMessage()
    {
        backend.Routes["DELETE /users/5/account"] = (HttpStatusCode.Unauthorized, """{"message":"Current password is wrong."}""");
        var (client, cookie) = await Start();

        var response = await client.SendWithCookie(HttpMethod.Post, "/account/delete", cookie, Confirm("nope"));

        Assert.Equal("/account/data?error=Current%20password%20is%20wrong.", response.Headers.Location!.ToString());
        Assert.Null(Cookie(response));
    }

    [Fact]
    public async Task ViewerCantDeleteOrDownload()
    {
        backend.Routes["POST /users/5/view-as/8"] = (HttpStatusCode.OK, Bob);
        var (client, admin) = await Start("admin");
        var viewing = Cookie(await client.SendWithCookie(HttpMethod.Post, "/account/view-as/8", admin))!;

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendWithCookie(HttpMethod.Post, "/account/delete", viewing, Confirm("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendWithCookie(HttpMethod.Get, "/account/data/export", viewing)).StatusCode);
        Assert.DoesNotContain(backend.Requests, r => r.Contains("/account") || r.Contains("/export"));
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
    }
}
