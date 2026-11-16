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
    [Theory]
    [InlineData("driver", true)]
    [InlineData("sponsor", false)]
    [InlineData("admin", false)]
    public void WishlistHeartAppearsBesideCartOnlyForDrivers(string role, bool shown)
    {
        var backend = new JsonBackend();
        backend.Routes["GET /users/7/notifications/unread"] = (HttpStatusCode.OK, """{"count":0}""");
        backend.Routes["GET /users/7"] = (HttpStatusCode.OK, """{"companyId":1}""");
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("user");
        auth.SetRoles(role);
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        var bar = RenderComponent<AccountBar>();
        Assert.Equal(shown ? 1 : 0, bar.FindAll("a[href=wishlist]").Count);
        if (shown)
        {
            var heart = bar.Find("a[href=wishlist]");
            Assert.Equal("Wishlist", heart.GetAttribute("aria-label"));
            Assert.Equal("currentColor", heart.QuerySelector("svg")!.GetAttribute("stroke"));
            Assert.Equal("cart", heart.NextElementSibling!.GetAttribute("href"));
        }
    }

    [Theory]
    [InlineData("driver")]
    [InlineData("sponsor")]
    [InlineData("admin")]
    public void ProfileMenuContainsThreeOptionsAndCloses(string role)
    {
        var backend = new JsonBackend();
        backend.Routes["GET /users/7/notifications/unread"] = (HttpStatusCode.OK, """{"count":0}""");
        this.SignInAs("bob", role, "7");
        this.UseBackend(backend);
        var bar = RenderComponent<AccountBar>();
        Assert.Empty(bar.FindAll("form[action='account/logout']"));
        var trigger = bar.Find("button.profile-link");
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        trigger.Click();
        Assert.Equal("true", bar.Find("button.profile-link").GetAttribute("aria-expanded"));
        var options = bar.Find("#profile-options");
        Assert.Equal(new[] { "Profile", "Settings", "Logout" }, options.QuerySelectorAll("a, button").Select(e => e.TextContent.Trim()));
        Assert.Equal("profile", options.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("settings", options.QuerySelectorAll("a")[1].GetAttribute("href"));
        Assert.Equal("post", options.QuerySelector("form")!.GetAttribute("method"));
        Assert.Equal("account/logout", options.QuerySelector("form")!.GetAttribute("action"));
        Assert.Single(bar.FindAll("form[action='account/logout']"));
        bar.Find(".profile-backdrop").Click();
        Assert.Empty(bar.FindAll("#profile-options"));
        bar.Find("button.profile-link").Click();
        bar.Find(".profile-menu").KeyDown("Escape");
        Assert.Empty(bar.FindAll("#profile-options"));
        bar.Find("button.profile-link").Click();
        bar.Find("#profile-options a").Click();
        Assert.Empty(bar.FindAll("#profile-options"));
    }

    [Fact]
    public void CartIconIsAnSvgThatFollowsTheBarColor()
    {
        var backend = new JsonBackend();
        backend.Routes["GET /users/7/notifications/unread"] = (HttpStatusCode.OK, """{"count":0}""");
        this.SignInAs("bob", "driver", "7");
        this.UseBackend(backend);

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
        this.SignInAs("bob", "driver", "7", viewing ? new[] { new Claim(FrontEnd.Auth.ViewAs.ViewerNameClaim, "ann") } : []);
        this.UseBackend(backend);

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
