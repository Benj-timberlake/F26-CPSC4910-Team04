using System.Net;
using FrontEnd.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class LoginCodeFlowTests : IAsyncDisposable
{
    private readonly JsonBackend backend = new();
    private WebApplication? app;

    private const string Bob = """{"id":8,"userType":"driver","username":"bob","email":"bob@example.com","phoneNumber":"","address":""}""";

    private async Task<HttpClient> Start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddTruckerAuthentication();
        builder.Services.AddSingleton<IHttpClientFactory>(new StubClientFactory(backend));
        app = builder.Build();
        app.UseAuthentication();
        app.MapAccountEndpoints();
        await app.StartAsync();
        return app.GetTestClient();
    }

    private static string? Cookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(c => c.StartsWith(name + "=")).Select(c => c.Split(';')[0]).LastOrDefault()
            : null;

    private static HttpRequestMessage Form(string url, string? cookie, params (string, string)[] fields)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields.Select(f => KeyValuePair.Create(f.Item1, f.Item2))) };
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        return request;
    }

    private async Task<(HttpClient Client, string Pending)> PasswordStep()
    {
        backend.Routes["POST /auth/login"] = (HttpStatusCode.Accepted, """{"challenge":"abc"}""");
        var client = await Start();
        var response = await client.SendAsync(Form("/account/login", null, ("username", "bob"), ("password", "Hunter22x!"), ("returnUrl", "/points")));
        Assert.Equal("/login/code", response.Headers.Location!.ToString());
        Assert.Null(Cookie(response, ".AspNetCore.Cookies"));
        var pending = Cookie(response, "tr_code")!;
        Assert.DoesNotContain("abc", pending);
        return (client, pending);
    }

    [Fact]
    public async Task RightCodeSignsInAndKeepsTheReturnUrl()
    {
        var (client, pending) = await PasswordStep();
        backend.Routes["POST /auth/login/code"] = (HttpStatusCode.OK, Bob);

        var response = await client.SendAsync(Form("/account/login/code", pending, ("code", "123456")));

        Assert.Equal("/points", response.Headers.Location!.ToString());
        Assert.NotNull(Cookie(response, ".AspNetCore.Cookies"));
        Assert.Contains("\"challenge\":\"abc\"", backend.Bodies["POST /auth/login/code"]);
        Assert.Contains("\"code\":\"123456\"", backend.Bodies["POST /auth/login/code"]);
    }

    [Fact]
    public async Task WrongCodeStaysOnTheCodePage()
    {
        var (client, pending) = await PasswordStep();
        backend.Routes["POST /auth/login/code"] = (HttpStatusCode.BadRequest, """{"message":"wrong"}""");

        var response = await client.SendAsync(Form("/account/login/code", pending, ("code", "000000")));

        Assert.Equal("/login/code?error=wrong", response.Headers.Location!.ToString());
        Assert.Null(Cookie(response, ".AspNetCore.Cookies"));
    }

    [Fact]
    public async Task NoPendingSignInGoesBackToTheLoginPage()
    {
        var client = await Start();
        var response = await client.SendAsync(Form("/account/login/code", "tr_code=forged", ("code", "123456")));
        Assert.Equal("/login?error=code", response.Headers.Location!.ToString());
        Assert.Empty(backend.Requests);
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
    }

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://backend/") };
    }
}
