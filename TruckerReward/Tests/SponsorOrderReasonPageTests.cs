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
}
