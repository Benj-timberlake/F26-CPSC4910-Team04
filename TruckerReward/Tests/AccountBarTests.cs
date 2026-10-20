using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AccountBarTests : TestContext
{
    [Fact]
    public void CartIconIsAnSvgThatFollowsTheBarColor()
    {
        var backend = new JsonBackend();
        backend.Routes["GET /users/7/notifications/unread"] = (HttpStatusCode.OK, """{"count":0}""");
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("bob");
        auth.SetRoles("driver");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);

        var bar = RenderComponent<AccountBar>();

        var cart = bar.Find("a[href=cart]");
        Assert.Equal("Cart", cart.GetAttribute("aria-label"));
        Assert.Equal("currentColor", cart.QuerySelector("svg")!.GetAttribute("stroke"));
        Assert.Empty(bar.FindAll("img[src='cart.jpg']"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BannerShowsOnlyWhileViewingAsSomeone(bool viewing)
    {
        var backend = new JsonBackend();
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("bob");
        auth.SetRoles("driver");
        auth.SetClaims([new Claim(ClaimTypes.NameIdentifier, "7"), .. viewing ? new[] { new Claim(FrontEnd.Auth.ViewAs.ViewerNameClaim, "ann") } : []]);
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);

        var bar = RenderComponent<AccountBar>();

        if (!viewing)
        {
            Assert.Empty(bar.FindAll(".view-as-banner"));
            return;
        }
        Assert.Contains("Viewing the site as bob", bar.Find(".view-as-banner").TextContent);
        Assert.Equal("account/view-as/stop", bar.Find(".view-as-banner form").GetAttribute("action"));
        Assert.Equal("Return to ann", bar.Find(".view-as-banner button").TextContent);
    }
}
