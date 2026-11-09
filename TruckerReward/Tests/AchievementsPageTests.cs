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
        backend.Routes.TryAdd("GET /users/7/achievements/progress", (HttpStatusCode.OK, "[]"));
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

    [Fact]
    public void ShowsProgressForTheClosestGoals()
    {
        backend.Routes["GET /users/7/achievements"] = (HttpStatusCode.OK, "[]");
        backend.Routes["GET /users/7/achievements/progress"] = (HttpStatusCode.OK, """
            [{"id":2,"name":"1,000 points","description":"Earn 1,000 points in all.","company":null,"metric":"points_earned","current":900,"threshold":1000},
             {"id":6,"name":"Regular shopper","description":"Place 5 orders.","company":null,"metric":"orders_placed","current":4,"threshold":5},
             {"id":9,"name":"Acme 500","description":"Earn 500 Acme points.","company":"Acme","metric":"points_earned","current":100,"threshold":500},
             {"id":4,"name":"10,000 points","description":"Earn 10,000 points in all.","company":null,"metric":"points_earned","current":900,"threshold":10000}]
            """);
        var page = Render();

        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll("progress").Count));
        var bar = page.Find("progress");
        Assert.Equal("goal-2", bar.Id);
        Assert.Equal("900", bar.GetAttribute("value"));
        Assert.Equal("1000", bar.GetAttribute("max"));
        Assert.Equal("900", bar.GetAttribute("aria-valuenow"));
        Assert.Equal("1000", bar.GetAttribute("aria-valuemax"));
        Assert.Equal("1,000 points", page.Find("label[for=goal-2]").TextContent);
        Assert.Equal("900 of 1,000 points earned", page.Find("#" + bar.GetAttribute("aria-describedby")).TextContent);
        var goals = page.FindAll(".goal").ToList();
        Assert.Contains("1 more order to go.", goals[1].TextContent);
        Assert.Contains("Set by Acme.", goals[2].TextContent);

        page.Find("button.toggle").Click();

        Assert.Equal(4, page.FindAll("progress").Count);
        Assert.Equal("true", page.Find("button.toggle").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void SaysSoWhenEveryBadgeIsEarned()
    {
        backend.Routes["GET /users/7/achievements"] = (HttpStatusCode.OK, "[]");
        var page = Render();

        page.WaitForAssertion(() => Assert.Contains("earned every badge", page.Find(".next").TextContent));
        Assert.Empty(page.FindAll("button.toggle"));
    }
}
