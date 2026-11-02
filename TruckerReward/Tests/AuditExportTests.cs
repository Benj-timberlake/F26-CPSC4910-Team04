using System.Net;
using FrontEnd.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AuditExportTests : IAsyncDisposable
{
    private readonly JsonBackend backend = new();
    private WebApplication? app;

    private async Task<HttpClient> Start()
    {
        var builder = FrontendHost.Builder(backend);
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
        return await client.SendWithCookie(HttpMethod.Get, url, signin.AuthCookie());
    }

    [Fact]
    public async Task ForwardsTheFiltersForTheSignedInUser()
    {
        backend.Routes["GET /users/5/audit.csv?category=points&from=2026-09-01&by=sue"] = (HttpStatusCode.OK, "When (UTC)\r\n");
        var client = await Start();

        var response = await Get(client, "sponsor", "/audit/export?category=points&from=2026-09-01&by=sue&id=1&other=x");

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
        Assert.DoesNotContain(backend.Requests, r => r.Contains("audit"));
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
    }
}
