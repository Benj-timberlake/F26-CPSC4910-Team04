using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class CartBalanceTests : TestContext
{
    private readonly JsonBackend backend = new();

    private IRenderedComponent<Cart> Render()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("bob");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        return RenderComponent<Cart>();
    }

    [Fact]
    public void CartShowsTheBalanceBeforeAndAfterTheOrder()
    {
        backend.Routes["GET /users/7/cart"] = (HttpStatusCode.OK, """[{"id":1,"name":"Mug","price":2.50,"quantity":2}]""");
        backend.Routes["GET /users/7"] = (HttpStatusCode.OK, """{"id":7,"points":1200}""");
        var page = Render();

        page.WaitForAssertion(() => Assert.Equal("Your balance: 1,200 points, 700 points after this order", page.Find(".cart-balance").TextContent));
    }

    [Fact]
    public void BalanceRefreshesAfterTheOrder()
    {
        backend.Routes["GET /users/7/cart"] = (HttpStatusCode.OK, """[{"id":1,"name":"Mug","price":2.50,"quantity":2}]""");
        backend.Routes["GET /users/7"] = (HttpStatusCode.OK, """{"id":7,"points":1200}""");
        backend.Routes["POST /users/7/cart/send-order"] = (HttpStatusCode.NoContent, "");
        var page = Render();
        page.WaitForAssertion(() => page.Find(".cart-balance"));

        backend.Routes["GET /users/7"] = (HttpStatusCode.OK, """{"id":7,"points":700}""");
        page.Find(".cart-total button").Click();

        page.WaitForAssertion(() => Assert.StartsWith("Your balance: 700 points", page.Find(".cart-balance").TextContent));
    }

    [Fact]
    public void CartStillLoadsWithoutTheBalance()
    {
        backend.Routes["GET /users/7/cart"] = (HttpStatusCode.OK, """[{"id":1,"name":"Mug","price":2.50,"quantity":2}]""");
        var page = Render();

        page.WaitForAssertion(() => Assert.Contains("Mug", page.Find("tbody").TextContent));
        Assert.Empty(page.FindAll(".cart-balance"));
    }
}
