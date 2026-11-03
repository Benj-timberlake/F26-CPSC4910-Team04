using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FrontEnd.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TruckerReward.Tests;

public sealed class CookieRevalidationTests : IAsyncDisposable
{
    private readonly TestApp.FakeClock clock = new();
    private readonly StubBackend backend = new();
    private WebApplication? app;

    private async Task<HttpClient> Start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.AddTruckerAuthentication();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<IHttpClientFactory>(new BackendClientFactory(backend));
        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapSignIn(new FrontEnd.Auth.UserProfile(7, "driver", "bob", "bob@example.com", "", ""));
        app.MapGet("/whoami", (ClaimsPrincipal user) => Results.Ok(new
        {
            name = user.Identity!.Name,
            role = user.FindFirstValue(ClaimTypes.Role)
        })).RequireAuthorization();
        await app.StartAsync();
        return app.GetTestClient();
    }

    // the test client doesn't keep cookies, so carry the auth cookie over by hand
    private static async Task<string> SignIn(HttpClient client)
    {
        var response = await client.GetAsync("/signin");
        response.EnsureSuccessStatusCode();
        return response.AuthCookie();
    }

    private static async Task<(HttpResponseMessage response, string? role)> WhoAmI(HttpClient client, string cookie)
    {
        var response = await client.SendWithCookie(HttpMethod.Get, "/whoami", cookie);
        var role = response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<Identity>())?.Role
            : null;
        return (response, role);
    }

    [Fact]
    public async Task FreshCookieIsNotCheckedAgainstTheBackend()
    {
        var client = await Start();
        var cookie = await SignIn(client);
        clock.Advance(TimeSpan.FromMinutes(1));

        var (response, role) = await WhoAmI(client, cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("driver", role);
        // only the session recorded at sign in
        Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public async Task ChangedUserTypeShowsUpAfterTheInterval()
    {
        var client = await Start();
        var cookie = await SignIn(client);
        backend.Respond(HttpStatusCode.OK, """{"id":7,"userType":"sponsor","username":"bob","email":"bob@example.com","phoneNumber":"","address":""}""");
        clock.Advance(CookieRevalidation.Interval + TimeSpan.FromSeconds(1));

        var (response, role) = await WhoAmI(client, cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("sponsor", role);
        Assert.Equal(3, backend.Calls);
        Assert.Equal("users/7", backend.LastPath);
        // and the refreshed cookie carries the new role and check time forward
        var renewed = response.AuthCookie();
        var (again, roleAgain) = await WhoAmI(client, renewed);
        Assert.Equal("sponsor", roleAgain);
        Assert.Equal(3, backend.Calls);
    }

    [Fact]
    public async Task DeletedAccountIsSignedOut()
    {
        var client = await Start();
        var cookie = await SignIn(client);
        backend.Respond(HttpStatusCode.NotFound, "");
        clock.Advance(CookieRevalidation.Interval + TimeSpan.FromSeconds(1));

        var (response, _) = await WhoAmI(client, cookie);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task DeactivatedAccountIsSignedOut()
    {
        var client = await Start();
        var cookie = await SignIn(client);
        backend.AccountStatus = "inactive";
        clock.Advance(CookieRevalidation.Interval + TimeSpan.FromSeconds(1));

        var (response, _) = await WhoAmI(client, cookie);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
        Assert.Equal("users/7/status", backend.LastPath);
    }

    [Fact]
    public async Task SignOutEverywhereDropsOlderCookies()
    {
        var client = await Start();
        var cookie = await SignIn(client);
        backend.SessionVersion = 1;
        clock.Advance(CookieRevalidation.Interval + TimeSpan.FromSeconds(1));

        var (response, _) = await WhoAmI(client, cookie);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task SessionEndedFromTheDevicesPageIsSignedOut()
    {
        backend.SessionKey = "k1";
        var client = await Start();
        var cookie = await SignIn(client);
        backend.SessionEnded = true;
        clock.Advance(CookieRevalidation.Interval + TimeSpan.FromSeconds(1));

        var (response, _) = await WhoAmI(client, cookie);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("sessions/check", backend.LastPath);
    }

    [Fact]
    public async Task BackendOutageKeepsTheExistingCookie()
    {
        var client = await Start();
        var cookie = await SignIn(client);
        backend.Fail();
        clock.Advance(CookieRevalidation.Interval + TimeSpan.FromSeconds(1));

        var (response, role) = await WhoAmI(client, cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("driver", role);
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
    }

    private sealed record Identity(string Name, string? Role);

    private sealed class StubBackend : HttpMessageHandler
    {
        private HttpStatusCode status = HttpStatusCode.OK;
        private string body = "{}";
        private bool fail;
        public int Calls { get; private set; }
        public string? LastPath { get; private set; }
        public string AccountStatus { get; set; } = "active";
        public int SessionVersion { get; set; }
        public string? SessionKey { get; set; }
        public bool SessionEnded { get; set; }

        public void Respond(HttpStatusCode s, string b) { status = s; body = b; fail = false; }
        public void Fail() => fail = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastPath = request.RequestUri!.PathAndQuery.TrimStart('/');
            if (fail) throw new HttpRequestException("backend down");
            if (SessionKey is not null && LastPath.EndsWith("/sessions"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"key":"{{SessionKey}}"}""", Encoding.UTF8, "application/json") });
            if (LastPath == "sessions/check")
                return Task.FromResult(new HttpResponseMessage(SessionEnded ? HttpStatusCode.Gone : HttpStatusCode.NoContent));
            if (LastPath.EndsWith("/status"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"status":"{{AccountStatus}}","sessionVersion":{{SessionVersion}}}""", Encoding.UTF8, "application/json") });
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
