using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Security.Claims;
using Xunit;

namespace TruckerReward.Tests;

public sealed class PastOrdersTests : TestContext
{
    [Fact]
    public void RefundRequiresConfirmationAndRemovesOrderAfterSuccess()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("buyer");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        var handler = new Handler();
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var page = RenderComponent<PastOrders>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));
        Assert.Contains("2026-09-26 12:00:00", page.Markup);
        Assert.Contains("300 points", page.Markup);
        page.Find("tbody button").Click();
        Assert.Contains("This action is permanent", page.Find("[role=dialog]").TextContent);
        Assert.Equal(0, handler.Refunds);
        page.Find("[role=dialog] button.tr-btn-outline").Click();
        Assert.Empty(page.FindAll("[role=dialog]"));
        Assert.Equal(0, handler.Refunds);
        page.Find("tbody button").Click();
        page.Find("[role=dialog] button.tr-btn").Click();
        page.WaitForAssertion(() => Assert.Contains("300 points returned", page.Markup));
        Assert.Equal(1, handler.Refunds);
        Assert.Empty(page.FindAll("tbody tr"));
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Refunds;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("/users/7/orders/42/refund", request.RequestUri!.AbsolutePath);
                Refunds++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"pointsHistoryId":42,"timestamp":"2026-09-26T12:00:00","totalPoints":300,"canRefund":true,"items":[{"id":1,"name":"Mug","price":1.5,"quantity":2}]}]""", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
