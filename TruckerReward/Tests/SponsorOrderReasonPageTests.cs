using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class SponsorOrderReasonPageTests : TestContext
{
    [Theory]
    [InlineData("\"Mine tore\"", "Reason: Mine tore")]
    [InlineData("null", "No reason given.")]
    public void SponsorOrdersShowTheReason(string reason, string shown)
    {
        var backend = new JsonBackend();
        backend.Routes["GET /users/5/driver-orders"] = (HttpStatusCode.OK,
            $$"""[{"orderId":3,"driverId":8,"driverName":"Bob Baker","timestamp":"2026-10-01T12:00:00","totalPrice":20,"items":[{"id":1,"name":"Gloves","price":20,"quantity":1}],"reason":{{reason}}}]""");
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("sue");
        auth.SetRoles("sponsor");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "5"));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);

        var page = RenderComponent<SponsorOrders>();

        page.WaitForAssertion(() => Assert.Equal(shown, page.Find(".order-reason").TextContent));
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.Conflict)]
    public void RejectRequiresAReasonAndOnlyRemovesOrderAfterSuccess(HttpStatusCode result)
    {
        var backend = new JsonBackend();
        backend.Routes["GET /users/5/driver-orders"] = (HttpStatusCode.OK,
            """[{"orderId":3,"driverId":8,"driverName":"Bob Baker","totalPrice":20,"items":[]}]""");
        const string reject = "POST /users/5/driver-orders/3/reject";
        backend.Routes[reject] = (result, result == HttpStatusCode.NoContent ? "" : """{"message":"Only pending orders can be rejected."}""");
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("sue");
        auth.SetRoles("sponsor");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "5"));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        var page = RenderComponent<SponsorOrders>();
        page.WaitForAssertion(() => Assert.True(page.Find("button.reject-order").HasAttribute("disabled")));
        page.Find("textarea").Change("   ");
        Assert.True(page.Find("button.reject-order").HasAttribute("disabled"));
        Assert.DoesNotContain(reject, backend.Requests);
        page.Find("textarea").Change("Out of stock");
        Assert.False(page.Find("button.reject-order").HasAttribute("disabled"));
        page.Find("button.reject-order").Click();
        page.WaitForAssertion(() => Assert.Contains(reject, backend.Requests));
        using var body = System.Text.Json.JsonDocument.Parse(backend.Bodies[reject]);
        Assert.Equal("Out of stock", body.RootElement.GetProperty("reason").GetString());
        if (result == HttpStatusCode.NoContent)
        {
            page.WaitForAssertion(() => Assert.Empty(page.FindAll("section.tr-card")));
            Assert.Contains("Points returned to the driver", page.Find("[role=status]").TextContent);
        }
        else
        {
            page.WaitForAssertion(() => Assert.Contains("Only pending orders", page.Find("[role=alert]").TextContent));
            Assert.Single(page.FindAll("section.tr-card"));
            Assert.Equal("Out of stock", page.Find("textarea").GetAttribute("value"));
        }
    }
}
