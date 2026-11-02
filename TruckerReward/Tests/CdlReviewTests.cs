using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd;
using FrontEnd.Auth;
using FrontEnd.Pages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class CdlReviewTests : TestContext
{
    [Fact]
    public void SponsorCanOpenAndCloseCdlModal()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("sponsor");
        auth.SetRoles("sponsor");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "4"));
        var backend = new JsonBackend();
        backend.Routes["GET /users/4/applications/driver"] = (HttpStatusCode.OK,
            """[{"id":12,"applicantId":8,"companyId":2,"applicantName":"Bob Driver","applicantEmail":"bob@example.com","companyName":"Acme","status":"active","applicationTimestamp":"2026-11-01T12:00:00Z"}]""");
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        JSInterop.Mode = JSRuntimeMode.Loose;
        var page = RenderComponent<Applications>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".cdl-button")));
        page.Find(".cdl-button").Click();
        Assert.Equal("applications/12/cdl", page.Find("iframe").GetAttribute("src"));
        Assert.Contains("Bob Driver", page.Find("#cdl-review-title").TextContent);
        page.Find("button[aria-label='Close CDL review']").Click();
        Assert.Empty(page.FindAll("iframe"));
    }

    [Theory]
    [InlineData("sponsor", 12, false, HttpStatusCode.OK, 1)]
    [InlineData("sponsor", 99, false, HttpStatusCode.NotFound, 0)]
    [InlineData("driver", 12, false, HttpStatusCode.Redirect, 0)]
    [InlineData("sponsor", 12, true, HttpStatusCode.Redirect, 0)]
    [InlineData("admin", 12, false, HttpStatusCode.OK, 1)]
    public async Task DocumentRequiresAuthorizedApplication(string role, int applicationId, bool impersonating, HttpStatusCode expected, int reads)
    {
        var backend = new JsonBackend();
        backend.Routes["GET /users/4/applications/driver"] = (HttpStatusCode.OK, """[{"id":12,"applicantId":8}]""");
        var reader = new Reader();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddTruckerAuthentication();
        builder.Services.AddSingleton<ICdlReader>(reader);
        builder.Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.Use(async (http, next) =>
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "4"), new(ClaimTypes.Role, role) };
            if (impersonating) claims.Add(new Claim(ViewAs.ViewerIdClaim, "1"));
            http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
            await next();
        });
        app.UseAuthorization();
        app.MapCdlReview();
        await app.StartAsync();
        using var response = await app.GetTestClient().GetAsync($"/applications/{applicationId}/cdl");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(reads, reader.Reads);
        if (reads > 0)
        {
            Assert.Equal(8, reader.DriverId);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Equal("%PDF-test", await response.Content.ReadAsStringAsync());
        }
    }

    private sealed class Reader : ICdlReader
    {
        public int Reads;
        public int DriverId;
        public Task<CdlDocument> DownloadAsync(int driverId, CancellationToken cancellationToken = default)
        {
            Reads++;
            DriverId = driverId;
            return Task.FromResult(new CdlDocument("%PDF-test"u8.ToArray(), "application/pdf"));
        }
    }
}
