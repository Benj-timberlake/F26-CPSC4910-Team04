using System.Net;
using Bunit;
using FrontEnd.Pages;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AchievementGoalsPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private const string Goals = """
        [{"id":9,"name":"Acme 500","description":"Earn 500 Acme points.","metric":"points_earned","threshold":500,"active":true,"earned":2},
         {"id":10,"name":"Old goal","description":"","metric":"orders_placed","threshold":3,"active":false,"earned":1}]
        """;

    private IRenderedComponent<AchievementGoals> Render()
    {
        this.SignInAs("sue", "sponsor", "4");
        this.UseBackend(backend);
        return RenderComponent<AchievementGoals>();
    }

    [Fact]
    public void ListsTheCompanysGoals()
    {
        backend.Routes["GET /users/4/achievements/goals"] = (HttpStatusCode.OK, Goals);
        var page = Render();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".goals li").Count));
        var rows = page.FindAll(".goals li").ToList();
        Assert.Contains("500 points earned. Earned by 2 drivers.", rows[0].TextContent);
        Assert.Contains("Off", rows[1].QuerySelector(".state")!.TextContent);
        Assert.Contains("3 orders placed. Earned by 1 driver.", rows[1].TextContent);
        Assert.Equal("Turn on Old goal", rows[1].QuerySelector("button.toggle")!.GetAttribute("aria-label"));
        foreach (var field in new[] { "goal-name", "goal-description", "goal-metric", "goal-threshold" })
            Assert.Single(page.FindAll($"label[for={field}]"));
    }

    [Fact]
    public void AddsAGoal()
    {
        backend.Routes["GET /users/4/achievements/goals"] = (HttpStatusCode.OK, "[]");
        backend.Routes["POST /users/4/achievements/goals"] = (HttpStatusCode.OK,
            """{"id":11,"name":"Safe month","description":"","metric":"orders_placed","threshold":4,"active":true,"earned":0}""");
        var page = Render();
        page.WaitForAssertion(() => Assert.Contains("No goals yet", page.Markup));

        page.Find("#goal-name").Change("Safe month");
        page.Find("#goal-metric").Change("orders_placed");
        page.Find("#goal-threshold").Change("4");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Added Safe month.", page.Find(".tr-alert-success").TextContent));
        var body = backend.Bodies["POST /users/4/achievements/goals"];
        Assert.Contains("\"name\":\"Safe month\"", body);
        Assert.Contains("\"metric\":\"orders_placed\"", body);
        Assert.Contains("\"threshold\":4", body);
        Assert.Contains("\"active\":true", body);
    }

    [Fact]
    public void EditsAndTurnsOffAGoal()
    {
        backend.Routes["GET /users/4/achievements/goals"] = (HttpStatusCode.OK, Goals);
        backend.Routes["PUT /users/4/achievements/goals/9"] = (HttpStatusCode.OK,
            """{"id":9,"name":"Acme 600","description":"Earn 500 Acme points.","metric":"points_earned","threshold":600,"active":true,"earned":2}""");
        var page = Render();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".goals li").Count));

        page.Find("button.edit").Click();
        Assert.Equal("Acme 500", page.Find("#goal-name").GetAttribute("value"));
        page.Find("#goal-name").Change("Acme 600");
        page.Find("#goal-threshold").Change("600");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Saved Acme 600.", page.Find(".tr-alert-success").TextContent));
        Assert.Contains("\"threshold\":600", backend.Bodies["PUT /users/4/achievements/goals/9"]);

        page.Find("button.toggle").Click();

        page.WaitForAssertion(() => Assert.Contains("Turned off Acme 500", page.Find(".tr-alert-success").TextContent));
        Assert.Contains("\"active\":false", backend.Bodies["PUT /users/4/achievements/goals/9"]);
    }

    [Fact]
    public void ShowsTheBackendsMessageWhenSavingFails()
    {
        backend.Routes["GET /users/4/achievements/goals"] = (HttpStatusCode.OK, "[]");
        backend.Routes["POST /users/4/achievements/goals"] = (HttpStatusCode.BadRequest, """{"message":"The target has to be 1 to 10,000,000."}""");
        var page = Render();
        page.WaitForAssertion(() => Assert.Contains("No goals yet", page.Markup));

        page.Find("#goal-name").Change("Too big");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("The target has to be 1 to 10,000,000.", page.Find(".tr-alert-error").TextContent));
    }

    [Fact]
    public void SponsorsWithoutACompanyAreToldWhy()
    {
        backend.Routes["GET /users/4/achievements/goals"] = (HttpStatusCode.Forbidden, "");
        var page = Render();

        page.WaitForAssertion(() => Assert.Equal("Join a sponsor company to set goals.", page.Find(".tr-alert-error").TextContent));
    }
}
