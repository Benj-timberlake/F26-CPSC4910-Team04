using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Security.Claims;
using Xunit;

namespace TruckerReward.Tests;

public sealed class CartDisplayTests : TestContext
{
    [Fact]
    public void DollarPricesDisplayAsPointsAndTotalIncludesQuantities()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("buyer");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => new CartHandler());

        var component = RenderComponent<Cart>();
        component.WaitForAssertion(() => Assert.Equal("Cart total: 8,851 points", component.Find(".cart-total strong").TextContent));
        Assert.Contains("2,950 points", component.Find("tbody").TextContent);
        Assert.Contains("1 point", component.Find("tbody").TextContent);
    }

    private sealed class CartHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"id":1,"name":"GPS","price":29.50,"quantity":3},{"id":2,"name":"Small item","price":0.01,"quantity":1}]""",
                    System.Text.Encoding.UTF8, "application/json")
            });
    }

    [Fact]
    public void InsufficientPointsMessageKeepsCartAndAllowsRetry()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("buyer");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => new InsufficientPointsHandler());
        var component = RenderComponent<Cart>();
        component.WaitForAssertion(() => Assert.Contains("100 points", component.Find("tbody").TextContent));
        component.Find(".cart-total button").Click();
        component.WaitForAssertion(() => Assert.Contains("Not enough points", component.Find("[role=alert]").TextContent));
        Assert.Contains("Mug", component.Find("tbody").TextContent);
        Assert.False(component.Find(".cart-total button").HasAttribute("disabled"));
        Assert.DoesNotContain("Order sent successfully", component.Markup);
    }

    private sealed class InsufficientPointsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            {
                Content = new StringContent(request.Method == HttpMethod.Post
                    ? """{"message":"Not enough points. Your order costs 100 points, but you have 0 points."}"""
                    : """[{"id":1,"name":"Mug","price":1,"quantity":1}]""",
                    System.Text.Encoding.UTF8, "application/json")
            });
    }
}
