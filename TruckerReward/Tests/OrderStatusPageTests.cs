using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class OrderStatusPageTests : TestContext
{
    [Theory]
    [InlineData("pending", "Waiting for your sponsor")]
    [InlineData("ordered", "Ordered by your sponsor")]
    public void PastOrdersShowEachOrdersStatus(string status, string shown)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var backend = new JsonBackend();
        backend.Routes["GET /users/7/orders"] = (HttpStatusCode.OK,
            $$"""[{"pointsHistoryId":42,"timestamp":"2026-09-26T12:00:00","totalPoints":300,"canRefund":false,"status":"{{status}}","items":[{"id":1,"name":"Mug","price":1.5,"quantity":2}]}]""");
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("bob");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);

        var page = RenderComponent<PastOrders>();

        page.WaitForAssertion(() => Assert.Equal(shown, page.Find("td.status span").TextContent));
    }

    [Fact]
    public void ChangingAPendingOrderGoesToTheCart()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var backend = new JsonBackend();
        backend.Routes["GET /users/7/orders"] = (HttpStatusCode.OK,
            """[{"pointsHistoryId":42,"timestamp":"2026-09-26T12:00:00","totalPoints":300,"canRefund":false,"status":"pending","items":[]},{"pointsHistoryId":43,"timestamp":"2026-09-25T12:00:00","totalPoints":300,"canRefund":false,"status":"ordered","items":[]}]""");
        backend.Routes["POST /users/7/orders/42/edit"] = (HttpStatusCode.NoContent, "");
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("bob");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();

        var page = RenderComponent<PastOrders>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("button.change-order")));
        page.Find("button.change-order").Click();

        page.WaitForAssertion(() => Assert.EndsWith("/cart", nav.Uri));
        Assert.Contains("POST /users/7/orders/42/edit", backend.Requests);
    }
}
