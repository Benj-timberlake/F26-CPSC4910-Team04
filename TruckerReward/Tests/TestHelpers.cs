using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace TruckerReward.Tests;

public static class PageSetup
{
    // role null leaves the user without one
    public static TestAuthorizationContext SignInAs(this TestContext ctx, string name, string? role, string id, params Claim[] extra)
    {
        var auth = ctx.AddTestAuthorization();
        auth.SetAuthorized(name);
        if (role is not null)
            auth.SetRoles(role);
        auth.SetClaims([new Claim(ClaimTypes.NameIdentifier, id), .. extra]);
        return auth;
    }

    public static void UseBackend(this TestContext ctx, HttpMessageHandler backend) =>
        ctx.Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
}

public sealed class BackendClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://backend/") };
}

public static class FrontendHost
{
    // frontend auth on a test server, backend calls go to the handler
    public static WebApplicationBuilder Builder(HttpMessageHandler backend)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddTruckerAuthentication();
        builder.Services.AddSingleton<IHttpClientFactory>(new BackendClientFactory(backend));
        return builder;
    }

    public static void MapSignIn(this WebApplication app, FrontEnd.Auth.UserProfile profile) =>
        app.MapGet("/signin", async (HttpContext http) =>
        {
            await TruckerSignIn.SignInAsync(http, profile);
            return Results.Ok();
        });

    // the test client doesn't keep cookies, so they're carried by hand
    public static string AuthCookie(this HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Cookies=")).Split(';')[0];

    public static Task<HttpResponseMessage> SendWithCookie(this HttpClient client, HttpMethod method, string url, string cookie, HttpContent? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body };
        request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request);
    }
}

public static class BackendClient
{
    public static Task<HttpResponseMessage> Register(this HttpClient client, string username, string firstName, string lastName) =>
        client.PostAsJsonAsync("/auth/register", new { username, firstName, lastName, email = $"{username}@example.com", password = "Hunter22x!", userType = "driver" });
}
