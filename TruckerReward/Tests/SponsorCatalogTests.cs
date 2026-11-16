using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages.Products;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Security.Claims;
using Xunit;

namespace TruckerReward.Tests;

public sealed class SponsorCatalogTests : TestContext
{
    [Fact]
    public void OpeningCatalogLoadsSavedProductsWithoutInteraction()
    {
        this.SignInAs("sponsor", "sponsor", "7");
        var backend = new JsonBackend();
        backend.Routes["GET /users/7/catalog"] = (HttpStatusCode.OK, """["gps"]""");
        backend.Routes["GET /api/products/detail?itemId=gps"] = (HttpStatusCode.OK,
            """{"itemId":"gps","title":"Truck GPS","price":{"value":"10","currency":"USD"}}""");
        this.UseBackend(backend);
        var page = RenderComponent<SponsorCatalog>();
        page.WaitForAssertion(() => Assert.Equal("Truck GPS", page.Find(".catalog-grid h2").TextContent));
        Assert.Contains("1,000 points", page.Find(".catalog-grid").TextContent);
        Assert.Empty(page.FindAll("[role=status]"));
        Assert.DoesNotContain(backend.Requests, request => request.StartsWith("POST"));
    }

    [Fact]
    public void UnavailableItemCanStillBeRemovedFromCatalog()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("sponsor");
        auth.SetRoles("sponsor");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        var handler = new Handler();
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var page = RenderComponent<SponsorCatalog>();
        page.WaitForAssertion(() => Assert.Contains("Unavailable product", page.Markup));
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Contains("Your catalog is empty", page.Markup));
        Assert.Equal("/users/7/catalog?itemId=v1%7C123%7C0", handler.DeletedPath);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string? DeletedPath { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Delete)
            {
                DeletedPath = request.RequestUri!.PathAndQuery;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            return Task.FromResult(request.RequestUri!.AbsolutePath == "/users/7/catalog"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""["v1|123|0"]""") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
