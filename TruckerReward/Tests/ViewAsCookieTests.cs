using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FrontEnd.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ViewAsCookieTests : IAsyncDisposable
{
    private readonly JsonBackend backend = new();
    private readonly TestApp.FakeClock clock = new();
    private WebApplication? app;

    private const string Bob = """{"id":8,"userType":"driver","username":"bob","email":"bob@example.com","phoneNumber":"","address":""}""";
    private const string Ann = """{"id":4,"userType":"admin","username":"ann","email":"ann@example.com","phoneNumber":"","address":""}""";

    private async Task<HttpClient> Start()
    {
        var builder = FrontendHost.Builder(backend);
        builder.Services.AddSingleton<TimeProvider>(clock);
        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapSignIn(new FrontEnd.Auth.UserProfile(4, "admin", "ann", "ann@example.com", "", ""));
        app.MapGet("/whoami", (ClaimsPrincipal user) => Results.Ok(new Who(user.Identity!.Name!, user.FindFirstValue(ViewAs.ViewerNameClaim))))
            .RequireAuthorization();
        app.MapViewAs();
        app.MapAccountEndpoints();
        await app.StartAsync();
        return app.GetTestClient();
    }

    private static string? Cookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(c => c.StartsWith(".AspNetCore.Cookies=")).Select(c => c.Split(';')[0]).LastOrDefault()
            : null;

    private async Task<(HttpClient Client, string Cookie)> ViewingAsBob()
    {
        backend.Routes["POST /users/4/view-as/8"] = (HttpStatusCode.OK, Bob);
        var client = await Start();
        var admin = Cookie(await client.GetAsync("/signin"))!;
        var response = await client.SendWithCookie(HttpMethod.Post, "/account/view-as/8", admin);
        Assert.Equal("/dashboard", response.Headers.Location!.ToString());
        return (client, Cookie(response)!);
    }

    [Fact]
    public async Task ViewingSwapsTheUserAndKeepsTheViewer()
    {
        var (client, cookie) = await ViewingAsBob();

        var who = await (await client.SendWithCookie(HttpMethod.Get, "/whoami", cookie)).Content.ReadFromJsonAsync<Who>();

        Assert.Equal(new Who("bob", "ann"), who);
    }

    [Fact]
    public async Task RevalidationKeepsTheViewerAndEndsAfterHalfAnHour()
    {
        var (client, cookie) = await ViewingAsBob();
        backend.Routes["GET /users/8/status"] = (HttpStatusCode.OK, """{"status":"active"}""");
        backend.Routes["GET /users/8"] = (HttpStatusCode.OK, Bob);
        clock.Advance(TimeSpan.FromMinutes(20));

        // only the view-as limit ends this before the renewed cookie expires
        var response = await client.SendWithCookie(HttpMethod.Get, "/whoami", cookie);
        Assert.Equal(new Who("bob", "ann"), await response.Content.ReadFromJsonAsync<Who>());
        Assert.Contains("GET /users/8", backend.Requests);
        var renewed = Cookie(response)!;

        clock.Advance(TimeSpan.FromMinutes(20));
        var later = await client.SendWithCookie(HttpMethod.Get, "/whoami", renewed);
        Assert.Equal(HttpStatusCode.Redirect, later.StatusCode);
        Assert.Contains("/login", later.Headers.Location!.ToString());
    }

    [Fact]
    public async Task StoppingSignsTheViewerBackIn()
    {
        var (client, cookie) = await ViewingAsBob();
        backend.Routes["GET /users/4/status"] = (HttpStatusCode.OK, """{"status":"active"}""");
        backend.Routes["GET /users/4"] = (HttpStatusCode.OK, Ann);

        var response = await client.SendWithCookie(HttpMethod.Post, "/account/view-as/stop", cookie);

        Assert.Equal("/admin/accounts", response.Headers.Location!.ToString());
        var who = await (await client.SendWithCookie(HttpMethod.Get, "/whoami", Cookie(response)!)).Content.ReadFromJsonAsync<Who>();
        Assert.Equal(new Who("ann", null), who);
    }

    [Fact]
    public async Task ViewerCantChangeSignInDetailsOrViewFurther()
    {
        var (client, cookie) = await ViewingAsBob();
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["password"] = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendWithCookie(HttpMethod.Post, "/account/change-password", cookie, form)).StatusCode);
        // the driver role check turns this away first
        Assert.Contains("/login", (await client.SendWithCookie(HttpMethod.Post, "/account/view-as/9", cookie)).Headers.Location!.ToString());
        Assert.DoesNotContain(backend.Requests, r => r.Contains("/password") || r.Contains("view-as/9"));
    }

    [Fact]
    public async Task PasswordChangeRefreshesTheCookieWithTheNewVersion()
    {
        backend.Routes["POST /users/4/password"] = (HttpStatusCode.OK,
            """{"id":4,"userType":"admin","username":"ann","email":"ann@example.com","phoneNumber":"","address":"","sessionVersion":1}""");
        var client = await Start();
        var admin = Cookie(await client.GetAsync("/signin"))!;
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["currentPassword"] = "old", ["password"] = "NewPass99!" });

        var response = await client.SendWithCookie(HttpMethod.Post, "/account/change-password", admin, form);

        Assert.Equal("/profile?password=changed", response.Headers.Location!.ToString());
        Assert.NotNull(Cookie(response));
        Assert.Contains("\"sessionKey\"", backend.Bodies["POST /users/4/password"]);
    }

    [Fact]
    public async Task RefusedViewKeepsTheAdminSignedIn()
    {
        backend.Routes["POST /users/4/view-as/5"] = (HttpStatusCode.Forbidden, "");
        var client = await Start();
        var admin = Cookie(await client.GetAsync("/signin"))!;

        var response = await client.SendWithCookie(HttpMethod.Post, "/account/view-as/5", admin);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(Cookie(response));
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
    }

    private sealed record Who(string Name, string? Viewer);
}
