using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages.Products;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class WishlistPageTests : TestContext
{
    [Theory]
    [InlineData(false, HttpStatusCode.NoContent)]
    [InlineData(true, HttpStatusCode.NoContent)]
    [InlineData(false, HttpStatusCode.InternalServerError)]
    [InlineData(true, HttpStatusCode.InternalServerError)]
    public void HeartsPersistChangesAndKeepStateOnFailure(bool wishlistOnly, HttpStatusCode result)
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("driver");
        auth.SetRoles("driver");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        var backend = new JsonBackend();
        backend.Routes["GET /users/7/wishlist"] = (HttpStatusCode.OK, wishlistOnly ? "[\"gps\"]" : "[]");
        backend.Routes["GET /users/7/catalog"] = (HttpStatusCode.OK, "[\"gps\"]");
        backend.Routes["GET /api/products/detail?itemId=gps"] = (HttpStatusCode.OK,
            """{"itemId":"gps","title":"GPS","price":{"value":"10","currency":"USD"}}""");
        backend.Routes["PUT /users/7/wishlist"] = (result, "");
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        var page = RenderComponent<Products>(p => p.Add(c => c.DriverCatalog, true).Add(c => c.WishlistOnly, wishlistOnly));
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".wishlist-button")));
        Assert.Equal(wishlistOnly ? "true" : "false", page.Find(".wishlist-button").GetAttribute("aria-pressed"));
        page.Find(".wishlist-button").Click();
        page.WaitForAssertion(() => Assert.Contains("PUT /users/7/wishlist", backend.Requests));
        using var body = System.Text.Json.JsonDocument.Parse(backend.Bodies["PUT /users/7/wishlist"]);
        Assert.Equal("gps", body.RootElement.GetProperty("itemId").GetString());
        Assert.Equal(!wishlistOnly, body.RootElement.GetProperty("saved").GetBoolean());
        if (result != HttpStatusCode.NoContent)
        {
            page.WaitForAssertion(() => Assert.Contains("Could not update", page.Find("[role=alert]").TextContent));
            Assert.Equal(wishlistOnly ? "true" : "false", page.Find(".wishlist-button").GetAttribute("aria-pressed"));
            Assert.Single(page.FindAll(".product-card"));
        }
        else if (wishlistOnly)
            page.WaitForAssertion(() => Assert.Empty(page.FindAll(".product-card")));
        else
            page.WaitForAssertion(() => Assert.Equal("true", page.Find(".wishlist-button").GetAttribute("aria-pressed")));
    }

    [Fact]
    public void CatalogLoadsSavedHeartsAndCanRemoveThemWithoutRemovingTheProduct()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("driver");
        auth.SetRoles("driver");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        var backend = new JsonBackend();
        backend.Routes["GET /users/7/wishlist"] = (HttpStatusCode.OK, """["gps"]""");
        backend.Routes["GET /users/7/catalog"] = (HttpStatusCode.OK, """["gps"]""");
        backend.Routes["GET /api/products/detail?itemId=gps"] = (HttpStatusCode.OK,
            """{"itemId":"gps","title":"GPS","price":{"value":"10","currency":"USD"}}""");
        backend.Routes["PUT /users/7/wishlist"] = (HttpStatusCode.NoContent, "");
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        var page = RenderComponent<Products>(p => p.Add(c => c.DriverCatalog, true));
        page.WaitForAssertion(() => Assert.Equal("true", page.Find(".wishlist-button").GetAttribute("aria-pressed")));
        Assert.Equal("Remove from wishlist: GPS", page.Find(".wishlist-button").GetAttribute("aria-label"));
        Assert.NotNull(page.Find(".product-price-row .product-price"));
        page.Find(".wishlist-button").Click();
        page.WaitForAssertion(() => Assert.Equal("false", page.Find(".wishlist-button").GetAttribute("aria-pressed")));
        Assert.Single(page.FindAll(".product-card"));
        using var body = System.Text.Json.JsonDocument.Parse(backend.Bodies["PUT /users/7/wishlist"]);
        Assert.False(body.RootElement.GetProperty("saved").GetBoolean());
    }

    [Fact]
    public void EmptyWishlistShowsGuidanceAndDoesNotLoadTheWholeCatalog()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("driver");
        auth.SetRoles("driver");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        var backend = new JsonBackend();
        backend.Routes["GET /users/7/wishlist"] = (HttpStatusCode.OK, "[]");
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        var page = RenderComponent<Wishlist>();
        page.WaitForAssertion(() => Assert.Contains("Your wishlist is empty", page.Markup));
        Assert.Equal("Wishlist", page.Find("h1").TextContent);
        Assert.Empty(page.FindAll(".product-card"));
        Assert.DoesNotContain("GET /users/7/catalog", backend.Requests);
        Assert.DoesNotContain(backend.Requests, r => r.Contains("api/products"));
    }
}
