using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class LeaderboardPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private static string Board(int? yourRank, int count) =>
        $$"""{"period":"all","yourRank":{{(yourRank?.ToString() ?? "null")}},"rows":[{{string.Join(",", Enumerable.Range(1, count).Select(i =>
            $$"""{"rank":{{i}},"id":{{i}},"username":"d{{i}}","firstName":"D","lastName":"{{i}}","points":{{1000 - i}}}"""))}}]}""";

    private IRenderedComponent<FrontEnd.Pages.Leaderboard> Render(string role, int id)
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("someone");
        auth.SetRoles(role);
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, id.ToString()));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        return RenderComponent<FrontEnd.Pages.Leaderboard>();
    }

    [Fact]
    public void DriverSeesTheirRankAndTheTopFive()
    {
        backend.Routes["GET /users/7/leaderboard?period=all"] = (HttpStatusCode.OK, Board(7, 8));
        var page = Render("driver", 7);

        page.WaitForAssertion(() => Assert.Equal("You're #7 of 8.", page.Find(".your-rank").TextContent));
        Assert.Equal(5, page.FindAll(".top5 li").Count);
        Assert.Equal(8, page.FindAll("tbody tr").Count);
        Assert.Contains("d7", page.Find("tr.me").TextContent);
        Assert.Empty(page.FindAll("button.award"));
    }

    [Fact]
    public void PeriodGoesIntoTheQuery()
    {
        backend.Routes["GET /users/7/leaderboard?period=all"] = (HttpStatusCode.OK, Board(1, 1));
        backend.Routes["GET /users/7/leaderboard?period=week"] = (HttpStatusCode.OK, """{"period":"week","yourRank":null,"rows":[]}""");
        var page = Render("driver", 7);
        page.WaitForAssertion(() => page.Find("tbody tr"));

        page.Find("select[aria-label=Period]").Change("week");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("No drivers on this board yet.", page.Markup));
    }

    [Fact]
    public void SponsorAwardsTheTopDriver()
    {
        backend.Routes["GET /users/5/leaderboard?period=all"] = (HttpStatusCode.OK, Board(null, 3));
        backend.Routes["GET /users/5/leaderboard?period=week"] = (HttpStatusCode.OK, Board(null, 3));
        backend.Routes["POST /users/5/points/drivers/1"] = (HttpStatusCode.OK, "{}");
        var page = Render("sponsor", 5);
        page.WaitForAssertion(() => page.Find("tbody tr"));
        page.Find("select[aria-label=Period]").Change("week");
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Equal(2, backend.Requests.Count(r => r.StartsWith("GET"))));

        page.Find("button.award").Click();
        Assert.Equal("Top driver this week", page.Find("input[aria-label='Award reason']").GetAttribute("value"));
        page.Find("input[aria-label='Award points']").Change("250");
        page.Find("form.award-form").Submit();

        page.WaitForAssertion(() => Assert.Equal("d1 got 250 points.", page.Find(".tr-alert-success").TextContent));
        Assert.Equal("""{"points":250,"reason":"Top driver this week"}""", backend.Bodies["POST /users/5/points/drivers/1"]);
    }

    [Fact]
    public void AwardFollowsTheCategory()
    {
        backend.Routes["GET /users/5/leaderboard?period=all"] = (HttpStatusCode.OK, Board(null, 3));
        backend.Routes["GET /users/5/leaderboard?period=month&category=orders"] = (HttpStatusCode.OK, Board(null, 3));
        var page = Render("sponsor", 5);
        page.WaitForAssertion(() => page.Find("tbody tr"));

        page.Find("select[aria-label=Period]").Change("month");
        page.Find("select[aria-label=Category]").Change("orders");
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Contains("Orders", page.Find("thead").TextContent));
        page.Find("button.award").Click();

        Assert.Equal("Most orders this month", page.Find("input[aria-label='Award reason']").GetAttribute("value"));
        Assert.EndsWith("999 orders", page.Find(".top5 li").TextContent);
    }

    [Fact]
    public void AdminPicksASponsorFirst()
    {
        backend.Routes["GET /companies"] = (HttpStatusCode.OK, """[{"id":2,"name":"Acme","description":null}]""");
        backend.Routes["GET /users/4/leaderboard?period=all&companyId=2"] = (HttpStatusCode.OK, Board(null, 2));
        var page = Render("admin", 4);
        page.WaitForAssertion(() => Assert.Contains("Acme", page.Find("select[aria-label=Sponsor]").TextContent));
        Assert.DoesNotContain(backend.Requests, r => r.Contains("leaderboard"));

        page.Find("select[aria-label=Sponsor]").Change("2");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));
    }

    [Fact]
    public void DriverWithoutASponsorIsTold()
    {
        backend.Routes["GET /users/7/leaderboard?period=all"] = (HttpStatusCode.BadRequest, """{"message":"x"}""");
        var page = Render("driver", 7);
        page.WaitForAssertion(() => Assert.Contains("You aren't with a sponsor yet", page.Markup));
    }
}
