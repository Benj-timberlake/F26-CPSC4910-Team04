using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class LeaderboardHiddenPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private static string Board(bool hidden)
    {
        var h = hidden ? "true" : "false";
        return """{"period":"all","yourRank":2,"youHidden":HID,"rows":[{"rank":1,"id":0,"username":"Anonymous driver","firstName":"","lastName":"","points":300,"hidden":true},{"rank":2,"id":7,"username":"bob","firstName":"Bob","lastName":"Baker","points":100,"hidden":HID}]}""".Replace("HID", h);
    }

    private IRenderedComponent<FrontEnd.Pages.Leaderboard> Render(string role, bool hidden)
    {
        this.SignInAs("bob", role, "7");
        backend.Routes["GET /users/7/leaderboard?period=all"] = (HttpStatusCode.OK, Board(hidden));
        this.UseBackend(backend);
        return RenderComponent<FrontEnd.Pages.Leaderboard>();
    }

    [Fact]
    public void HiddenRowsShowAsAnonymousWithoutBlankNames()
    {
        var page = Render("driver", false);

        page.WaitForAssertion(() => page.Find("tbody tr"));
        var first = page.FindAll("tbody tr").First().TextContent;
        Assert.Contains("Anonymous driver", first);
        Assert.DoesNotContain("()", first);
        Assert.Contains("Bob Baker (bob)", page.Find("tr.me").TextContent);
    }

    [Fact]
    public void ToggleSendsTheChoice()
    {
        backend.Routes["PUT /users/7/leaderboard/hidden"] = (HttpStatusCode.OK, "{}");
        var page = Render("driver", false);
        page.WaitForAssertion(() => page.Find("input.hide-me"));
        Assert.Null(page.Find("input.hide-me").GetAttribute("checked"));

        page.Find("input.hide-me").Change(true);

        page.WaitForAssertion(() => Assert.Contains("Your name is hidden on the leaderboard.", page.Markup));
        Assert.Contains("PUT /users/7/leaderboard/hidden", backend.Requests);
    }

    [Fact]
    public void CheckboxIsCheckedWhenHidden()
    {
        var page = Render("driver", true);
        page.WaitForAssertion(() => Assert.NotNull(page.Find("input.hide-me").GetAttribute("checked")));
    }

    [Fact]
    public void SponsorsDontGetTheToggle()
    {
        var page = Render("sponsor", false);
        page.WaitForAssertion(() => page.Find("tbody tr"));
        Assert.Empty(page.FindAll("input.hide-me"));
    }
}
