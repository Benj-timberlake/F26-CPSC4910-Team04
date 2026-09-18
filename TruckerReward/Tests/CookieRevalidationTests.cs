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
using Xunit;

namespace TruckerReward.Tests;

public sealed class CookieRevalidationTests : IAsyncDisposable
{
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly StubBackend backend = new();
    private WebApplication? app;

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
            await TruckerSignIn.SignInAsync(http, new FrontEnd.Auth.UserProfile(7, "driver", "bob", "bob@example.com", "", ""));
            return Results.Ok();
        });
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
        return response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Cookies=")).Split(';')[0];
    }

    private static async Task<(HttpResponseMessage response, string? role)> WhoAmI(HttpClient client, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(request);
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
        Assert.Equal(0, backend.Calls);
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
        Assert.Equal(1, backend.Calls);
        Assert.Equal("users/7", backend.LastPath);
        // and the refreshed cookie carries the new role and check time forward
        var renewed = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Cookies=")).Split(';')[0];
        var (again, roleAgain) = await WhoAmI(client, renewed);
        Assert.Equal("sponsor", roleAgain);
        Assert.Equal(1, backend.Calls);
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

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset now = now;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://backend/") };
    }

    private sealed class StubBackend : HttpMessageHandler
    {
        private HttpStatusCode status = HttpStatusCode.OK;
        private string body = "{}";
        private bool fail;
        public int Calls { get; private set; }
        public string? LastPath { get; private set; }

        public void Respond(HttpStatusCode s, string b) { status = s; body = b; fail = false; }
        public void Fail() => fail = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastPath = request.RequestUri!.PathAndQuery.TrimStart('/');
            if (fail) throw new HttpRequestException("backend down");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
