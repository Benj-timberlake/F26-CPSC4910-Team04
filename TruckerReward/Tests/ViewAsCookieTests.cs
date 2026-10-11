using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FrontEnd.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ViewAsCookieTests : IAsyncDisposable
{
    private readonly JsonBackend backend = new();
    private readonly TestApp.FakeClock clock = new(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));
    private WebApplication? app;

    private const string Bob = """{"id":8,"userType":"driver","username":"bob","email":"bob@example.com","phoneNumber":"","address":""}""";
    private const string Ann = """{"id":4,"userType":"admin","username":"ann","email":"ann@example.com","phoneNumber":"","address":""}""";

    private async Task<HttpClient> Start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddTruckerAuthentication();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<IHttpClientFactory>(new StubClientFactory(backend));
        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/signin", async (HttpContext http) =>
        {
            await TruckerSignIn.SignInAsync(http, new FrontEnd.Auth.UserProfile(4, "admin", "ann", "ann@example.com", "", ""));
            return Results.Ok();
        });
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

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, string cookie, HttpContent? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body };
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private async Task<(HttpClient Client, string Cookie)> ViewingAsBob()
    {
        backend.Routes["POST /users/4/view-as/8"] = (HttpStatusCode.OK, Bob);
        var client = await Start();
        var admin = Cookie(await client.GetAsync("/signin"))!;
        var response = await Send(client, HttpMethod.Post, "/account/view-as/8", admin);
        Assert.Equal("/dashboard", response.Headers.Location!.ToString());
        return (client, Cookie(response)!);
    }

    [Fact]
    public async Task ViewingSwapsTheUserAndKeepsTheViewer()
    {
        var (client, cookie) = await ViewingAsBob();

        var who = await (await Send(client, HttpMethod.Get, "/whoami", cookie)).Content.ReadFromJsonAsync<Who>();

        Assert.Equal(new Who("bob", "ann"), who);
    }

    [Fact]
    public async Task RevalidationKeepsTheViewerAndEndsAfterHalfAnHour()
    {
        var (client, cookie) = await ViewingAsBob();
        backend.Routes["GET /users/8/status"] = (HttpStatusCode.OK, """{"status":"active"}""");
        backend.Routes["GET /users/8"] = (HttpStatusCode.OK, Bob);
        clock.Advance(CookieRevalidation.Interval + TimeSpan.FromSeconds(1));

        var response = await Send(client, HttpMethod.Get, "/whoami", cookie);
        Assert.Equal(new Who("bob", "ann"), await response.Content.ReadFromJsonAsync<Who>());
        Assert.Contains("GET /users/8", backend.Requests);

        clock.Advance(ViewAs.Lifetime);
        var later = await Send(client, HttpMethod.Get, "/whoami", Cookie(response) ?? cookie);
        Assert.Equal(HttpStatusCode.Redirect, later.StatusCode);
        Assert.Contains("/login", later.Headers.Location!.ToString());
    }

    [Fact]
    public async Task StoppingSignsTheViewerBackIn()
    {
        var (client, cookie) = await ViewingAsBob();
        backend.Routes["GET /users/4/status"] = (HttpStatusCode.OK, """{"status":"active"}""");
        backend.Routes["GET /users/4"] = (HttpStatusCode.OK, Ann);

        var response = await Send(client, HttpMethod.Post, "/account/view-as/stop", cookie);

        Assert.Equal("/admin/accounts", response.Headers.Location!.ToString());
        var who = await (await Send(client, HttpMethod.Get, "/whoami", Cookie(response)!)).Content.ReadFromJsonAsync<Who>();
        Assert.Equal(new Who("ann", null), who);
    }

    [Fact]
    public async Task ViewerCantChangeSignInDetailsOrViewFurther()
    {
        var (client, cookie) = await ViewingAsBob();
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["password"] = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, HttpMethod.Post, "/account/change-password", cookie, form)).StatusCode);
        // bob is a driver, so the role check turns this away first
        Assert.Contains("/login", (await Send(client, HttpMethod.Post, "/account/view-as/9", cookie)).Headers.Location!.ToString());
        Assert.DoesNotContain(backend.Requests, r => r.Contains("/password") || r.Contains("view-as/9"));
    }

    [Fact]
    public async Task RefusedViewKeepsTheAdminSignedIn()
    {
        backend.Routes["POST /users/4/view-as/5"] = (HttpStatusCode.Forbidden, "");
        var client = await Start();
        var admin = Cookie(await client.GetAsync("/signin"))!;

        var response = await Send(client, HttpMethod.Post, "/account/view-as/5", admin);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(Cookie(response));
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
    }

    private sealed record Who(string Name, string? Viewer);

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://backend/") };
    }
}
