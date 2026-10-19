using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FrontEnd.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TruckerReward.Tests;

public sealed class DriverViewSessionTests
{
    [Fact]
    public async Task SwitchUsesDriverRoleAndReturnRestoresSponsor()
    {
        var backend = new Backend();
        var clock = new TestApp.FakeClock(DateTimeOffset.UtcNow);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.AddTruckerAuthentication();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<IHttpClientFactory>(new Clients(backend));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDriverViewEndpoints();
        app.MapGet("/test/signin", async (HttpContext http) =>
        {
            await TruckerSignIn.SignInAsync(http, Backend.Sponsor);
            return Results.Ok();
        });
        app.MapGet("/test/identity", (HttpContext http) => Results.Ok(new Identity(
            http.User.FindFirstValue(ClaimTypes.NameIdentifier),
            http.User.FindFirstValue(ClaimTypes.Role),
            http.User.FindFirstValue(TruckerSignIn.SponsorViewClaim)))).RequireAuthorization();
        await app.StartAsync();
        using var client = app.GetTestClient();

        var sponsorCookie = Cookie(await client.GetAsync("/test/signin"));
        var switched = await Post(client, "/account/driver-view/2", sponsorCookie);
        Assert.Equal(HttpStatusCode.Redirect, switched.StatusCode);
        var driverCookie = Cookie(switched);
        Assert.Equal(new Identity("2", "driver", "1"), await Who(client, driverCookie));

        // Revalidation must keep the original sponsor claim.
        clock.Advance(CookieRevalidation.Interval + TimeSpan.FromSeconds(1));
        Assert.Equal(new Identity("2", "driver", "1"), await Who(client, driverCookie));
        var restored = await Post(client, "/account/driver-view/return", driverCookie);
        Assert.Equal(HttpStatusCode.Redirect, restored.StatusCode);
        Assert.Equal(new Identity("1", "sponsor", null), await Who(client, Cookie(restored)));

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(client, "/account/driver-view/3", sponsorCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(client, "/account/driver-view/2", driverCookie)).StatusCode);

        backend.AllowView = false;
        using var revoked = new HttpRequestMessage(HttpMethod.Get, "/test/identity");
        revoked.Headers.Add("Cookie", driverCookie);
        Assert.Equal(HttpStatusCode.Redirect, (await client.SendAsync(revoked)).StatusCode);
    }

    private static string Cookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Last(c => c.StartsWith(".AspNetCore.Cookies=")).Split(';')[0];

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static async Task<Identity?> Who(HttpClient client, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/identity");
        request.Headers.Add("Cookie", cookie);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Identity>();
    }

    private sealed record Identity(string? Id, string? Role, string? SponsorId);
    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri("http://backend/") };
    }
    private sealed class Backend : HttpMessageHandler
    {
        public static readonly FrontEnd.Auth.UserProfile Sponsor = new(1, "sponsor", "sponsor", "s@example.com", "", "");
        public bool AllowView { get; set; } = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/users/1/drivers/2/view" && AllowView)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new FrontEnd.Auth.UserProfile(2, "driver", "driver", "d@example.com", "", "")) });
            if (path == "/users/1")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Sponsor) });
            if (path == "/users/1/status")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "active" }) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        }
    }
}
