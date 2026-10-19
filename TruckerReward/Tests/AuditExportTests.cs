using System.Net;
using System.Security.Claims;
using FrontEnd.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AuditExportTests : IAsyncDisposable
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
        app.MapGet("/signin/{type}", async (string type, HttpContext http) =>
        {
            await TruckerSignIn.SignInAsync(http, new FrontEnd.Auth.UserProfile(5, type, "sue", "sue@example.com", "", ""));
            return Results.Ok();
        });
        app.MapAuditExport();
        await app.StartAsync();
        return app.GetTestClient();
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string type, string url)
    {
        var signin = await client.GetAsync($"/signin/{type}");
        var cookie = signin.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Cookies=")).Split(';')[0];
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task ForwardsTheFiltersForTheSignedInUser()
    {
        backend.Routes["GET /users/5/audit.csv?category=points&from=2026-09-01"] = (HttpStatusCode.OK, "When (UTC)\r\n");
        var client = await Start();

        var response = await Get(client, "sponsor", "/audit/export?category=points&from=2026-09-01&id=1&other=x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("audit-log.csv", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
        Assert.Equal("When (UTC)\r\n", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DriversCantExport()
    {
        var client = await Start();
        var response = await Get(client, "driver", "/audit/export");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
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
