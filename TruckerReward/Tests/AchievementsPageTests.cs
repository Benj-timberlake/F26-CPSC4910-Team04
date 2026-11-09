using System.Net;
using Bunit;
using FrontEnd.Pages;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AchievementsPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private IRenderedComponent<Achievements> Render()
    {
        this.SignInAs("bob", "driver", "7");
        this.UseBackend(backend);
        return RenderComponent<Achievements>();
    }

    [Fact]
    public void ShowsEarnedBadges()
    {
        backend.Routes["GET /users/7/achievements"] = (HttpStatusCode.OK, """
            [{"id":9,"name":"Acme 500","description":"Earn 500 Acme points.","company":"Acme","earnedAt":"2026-11-02T10:00:00Z"},
             {"id":1,"name":"First points","description":"Earn your first points.","company":null,"earnedAt":"2026-11-01T08:30:00Z"}]
            """);
        var page = Render();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".badge-item").Count));
        var items = page.FindAll(".badge-item").ToList();
        Assert.Equal("Acme 500", items[0].QuerySelector(".badge-name")!.TextContent);
        Assert.Contains("2026-11-02 10:00 UTC from Acme", items[0].TextContent);
        Assert.Contains("2026-11-01 08:30 UTC", items[1].TextContent);
        Assert.DoesNotContain(" from ", items[1].QuerySelector(".badge-meta")!.TextContent);
    }

    [Fact]
    public void SaysSoWhenThereAreNone()
    {
        backend.Routes["GET /users/7/achievements"] = (HttpStatusCode.OK, "[]");
        var page = Render();

        page.WaitForAssertion(() => Assert.Contains("No badges yet", page.Find(".badges").TextContent));
    }

    [Fact]
    public void ShowsAnErrorWhenTheBackendFails()
    {
        backend.Routes["GET /users/7/achievements"] = (HttpStatusCode.InternalServerError, "");
        var page = Render();

        page.WaitForAssertion(() => Assert.Equal("Couldn't load your achievements right now. Try again in a moment.", page.Find(".tr-alert-error").TextContent));
    }
}
