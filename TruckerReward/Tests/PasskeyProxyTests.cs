using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FrontEnd.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class PasskeyProxyTests : IAsyncDisposable
{
    private readonly JsonBackend backend = new();
    private WebApplication? app;

    private async Task<HttpClient> Start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddTruckerAuthentication();
        builder.Services.AddSingleton<IHttpClientFactory>(new StubClientFactory(backend));
        app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/signin", async (HttpContext http) =>
        {
            await TruckerSignIn.SignInAsync(http, new FrontEnd.Auth.UserProfile(8, "driver", "bob", "bob@example.com", "", ""));
            return Results.Ok();
        });
        app.MapPasskeyProxy();
        await app.StartAsync();
        return app.GetTestClient();
    }

    private static bool SetsCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) && values.Any(c => c.StartsWith(".AspNetCore.Cookies="));

    [Theory]
    [InlineData("/points", "/points")]
    [InlineData("//evil.example", "/dashboard")]
    [InlineData(null, "/dashboard")]
    public async Task GoodPasskeySignsInAndRedirectsSafely(string? returnUrl, string redirect)
    {
        backend.Routes["POST /auth/passkey/complete"] = (HttpStatusCode.OK, """{"id":8,"userType":"driver","username":"bob","email":"bob@example.com","phoneNumber":"","address":""}""");
        var client = await Start();

        var response = await client.PostAsJsonAsync("/account/passkey-login", new { requestId = "r1", returnUrl, response = new { id = "abc" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(SetsCookie(response));
        Assert.Equal(redirect, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirect").GetString());
        var sent = JsonDocument.Parse(backend.Bodies["POST /auth/passkey/complete"]).RootElement;
        Assert.Equal("r1", sent.GetProperty("requestId").GetString());
        Assert.Equal("abc", sent.GetProperty("response").GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("driver", false)]
    public async Task AdminsGetTheDashboardImageAfterAPasskeyToo(string type, bool dance)
    {
        backend.Routes["POST /auth/passkey/complete"] = (HttpStatusCode.OK, $$"""{"id":4,"userType":"{{type}}","username":"ann","email":"ann@example.com","phoneNumber":"","address":""}""");
        var client = await Start();

        var response = await client.PostAsJsonAsync("/account/passkey-login", new { requestId = "r1", response = new { id = "abc" } });

        Assert.Equal(dance, response.Headers.GetValues("Set-Cookie").Any(c => c.StartsWith("adminDance=1; ") && c.Contains("path=/dashboard")));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task BadPasskeyDoesNotSignIn(HttpStatusCode fromBackend, HttpStatusCode expected)
    {
        backend.Routes["POST /auth/passkey/complete"] = (fromBackend, """{"message":"x"}""");
        var client = await Start();

        var response = await client.PostAsJsonAsync("/account/passkey-login", new { requestId = "r1", response = new { } });

        Assert.Equal(expected, response.StatusCode);
        Assert.False(SetsCookie(response));
    }

    [Fact]
    public async Task OptionsComeStraightFromTheBackend()
    {
        backend.Routes["POST /auth/passkey/begin"] = (HttpStatusCode.OK, """{"requestId":"r1","options":{"challenge":"abc"}}""");
        var client = await Start();

        var response = await client.PostAsync("/account/passkey-login/options", null);

        Assert.Equal("""{"requestId":"r1","options":{"challenge":"abc"}}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RegisteringIsForTheSignedInUser()
    {
        backend.Routes["POST /users/8/passkeys/begin"] = (HttpStatusCode.OK, """{"requestId":"r2","options":{}}""");
        var client = await Start();
        Assert.NotEqual(HttpStatusCode.OK, (await client.PostAsync("/account/passkeys/options", null)).StatusCode);

        var signin = await client.GetAsync("/signin");
        var request = new HttpRequestMessage(HttpMethod.Post, "/account/passkeys/options");
        request.Headers.Add("Cookie", signin.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Cookies=")).Split(';')[0]);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["POST /users/8/passkeys/begin"], backend.Requests);
    }

    [Fact]
    public async Task SavedPasskeyPassesThroughAnEmptyNoContent()
    {
        backend.Routes["POST /users/8/passkeys/complete"] = (HttpStatusCode.NoContent, "");
        var client = await Start();

        var signin = await client.GetAsync("/signin");
        var request = new HttpRequestMessage(HttpMethod.Post, "/account/passkeys") { Content = JsonContent.Create(new { requestId = "r2", name = "phone", response = new { } }) };
        request.Headers.Add("Cookie", signin.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Cookies=")).Split(';')[0]);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
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
