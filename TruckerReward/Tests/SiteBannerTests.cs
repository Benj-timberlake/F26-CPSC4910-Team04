using System.Net;
using System.Security.Claims;
using Bunit;
using FrontEnd.Pages;
using FrontEnd.Shared;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class SiteBannerTests : TestContext
{
    private readonly JsonBackend backend = new();

    private const string Warning = """{"message":"Down at 9pm","level":"warning","createdAt":"2026-11-02T09:00:00Z"}""";

    private void Setup(string role = "driver", string id = "7")
    {
        backend.Routes[$"GET /users/{id}/notifications/unread"] = (HttpStatusCode.OK, """{"count":0}""");
        this.SignInAs("bob", role, id);
        this.UseBackend(backend);
    }

    [Fact]
    public void BarShowsTheActiveBanner()
    {
        Setup();
        backend.Routes["GET /site-banner"] = (HttpStatusCode.OK, Warning);

        var bar = RenderComponent<AccountBar>();

        var banner = bar.Find(".site-banner");
        Assert.Equal("Down at 9pm", banner.TextContent);
        Assert.Contains("warning", banner.ClassList);
    }

    [Fact]
    public void NothingShowsWithoutABannerOrWhenTheBackendIsDown()
    {
        Setup();
        backend.Routes["GET /site-banner"] = (HttpStatusCode.NoContent, "");
        Assert.Empty(RenderComponent<AccountBar>().FindAll(".site-banner"));

        backend.Routes["GET /site-banner"] = (HttpStatusCode.InternalServerError, "");
        Assert.Empty(RenderComponent<AccountBar>().FindAll(".site-banner"));
    }

    [Fact]
    public void BannerIsCachedBetweenPageLoads()
    {
        Services.AddMemoryCache();
        Setup();
        backend.Routes["GET /site-banner"] = (HttpStatusCode.OK, Warning);

        RenderComponent<AccountBar>();
        RenderComponent<AccountBar>();

        Assert.Single(backend.Requests, r => r == "GET /site-banner");
    }

    [Fact]
    public void AdminPageSetsTheBannerAndDropsTheCache()
    {
        Services.AddMemoryCache();
        Setup("admin", "3");
        backend.Routes["GET /site-banner"] = (HttpStatusCode.NoContent, "");
        backend.Routes["PUT /admin/site-banner"] = (HttpStatusCode.NoContent, "");
        Services.GetRequiredService<IMemoryCache>().Set(SiteBanner.CacheKey, null as object);
        var page = RenderComponent<AdminBanner>();

        page.Find("#banner-message").Change("Back at noon");
        page.Find("#banner-level").Change("warning");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("The banner is showing.", page.Find(".tr-alert-success").TextContent));
        Assert.Equal("""{"adminId":3,"message":"Back at noon","level":"warning"}""", backend.Bodies["PUT /admin/site-banner"]);
        Assert.False(Services.GetRequiredService<IMemoryCache>().TryGetValue(SiteBanner.CacheKey, out _));
    }

    [Fact]
    public void AdminPageClearsTheBanner()
    {
        Setup("admin", "3");
        backend.Routes["GET /site-banner"] = (HttpStatusCode.OK, Warning);
        backend.Routes["POST /admin/site-banner/clear"] = (HttpStatusCode.NoContent, "");
        var page = RenderComponent<AdminBanner>();
        page.WaitForAssertion(() => Assert.Contains("Down at 9pm", page.Markup));

        page.Find("button.clear").Click();

        page.WaitForAssertion(() => Assert.Equal("The banner was cleared.", page.Find(".tr-alert-success").TextContent));
        Assert.Equal("""{"adminId":3}""", backend.Bodies["POST /admin/site-banner/clear"]);
    }

    [Fact]
    public void AdminsGetALinkToThePage()
    {
        Setup("admin", "3");
        backend.Routes["GET /site-banner"] = (HttpStatusCode.NoContent, "");

        Assert.Single(RenderComponent<AccountBar>().FindAll("a[href='admin/banner']"));
    }
}
