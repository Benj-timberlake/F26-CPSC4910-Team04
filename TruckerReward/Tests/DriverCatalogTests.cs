using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages.Products;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Security.Claims;
using Xunit;

namespace TruckerReward.Tests;

public sealed class DriverCatalogTests : TestContext
{
    [Fact]
    public void DriverLoadsSavedItemsAndSearchesOnlyThoseItems()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("driver");
        auth.SetRoles("driver");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        var handler = new CatalogHandler();
        Services.AddHttpClient("Backend", client => client.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var page = RenderComponent<DriverProducts>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".product-card")));
        Assert.Equal("GPS", page.Find(".product-card h2").TextContent);
        page.Find(".search-bar > input").Change("boots");
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".product-card")));
        Assert.DoesNotContain(handler.Paths, path => path.StartsWith("/api/products?"));
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Paths.Add(path);
            var body = path == "/users/7/catalog" ? """["v1|123|0"]"""
                : """{"itemId":"v1|123|0","title":"GPS","price":{"value":"10","currency":"USD"}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
