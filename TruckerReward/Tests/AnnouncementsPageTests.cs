using System.Net;
using Bunit;
using FrontEnd.Pages;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AnnouncementsPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private const string Items = """
        [{"id":2,"title":"Holiday hours","body":"Closed Thursday.","createdAt":"2026-11-02T09:00:00Z","company":"Acme","author":"Sue Lee"},
         {"id":1,"title":"Welcome","body":"Hi all.","createdAt":"2026-11-01T09:00:00Z","company":"Acme","author":null}]
        """;

    private IRenderedComponent<Announcements> Render(string role)
    {
        backend.Routes["GET /users/8/announcements"] = (HttpStatusCode.OK, Items);
        this.SignInAs("pat", role, "8");
        this.UseBackend(backend);
        return RenderComponent<Announcements>();
    }

    [Fact]
    public void DriversSeeTheListWithoutAPostForm()
    {
        var page = Render("driver");

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("li.announcement").Count));
        Assert.Contains("Holiday hours", page.Find("li.announcement").TextContent);
        Assert.Contains("Acme, Sue Lee, 2026-11-02 09:00 UTC", page.Find("li.announcement").TextContent);
        Assert.Contains("a former sponsor", page.FindAll("li.announcement").Last().TextContent);
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public void SponsorsPostAndTheListReloads()
    {
        backend.Routes["POST /users/8/announcements"] = (HttpStatusCode.Created, "{}");
        var page = Render("sponsor");
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("li.announcement").Count));

        page.Find("#announce-title").Change("Raffle");
        page.Find("#announce-body").Change("Friday at noon.");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Your announcement was posted and your drivers were notified.", page.Find(".tr-alert-success").TextContent));
        Assert.Equal("""{"title":"Raffle","body":"Friday at noon."}""", backend.Bodies["POST /users/8/announcements"]);
        Assert.Equal(2, backend.Requests.Count(r => r == "GET /users/8/announcements"));
    }

    [Fact]
    public void RefusalsShowTheBackendMessage()
    {
        backend.Routes["POST /users/8/announcements"] = (HttpStatusCode.BadRequest, """{"message":"Give a title of 1 to 100 characters."}""");
        var page = Render("sponsor");
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("li.announcement").Count));

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Give a title of 1 to 100 characters.", page.Find(".tr-alert-error").TextContent));
    }

    [Fact]
    public void NavLinkShowsForDriversAndSponsorsOnly()
    {
        foreach (var (role, shown) in new[] { ("driver", 1), ("sponsor", 1), ("admin", 0) })
        {
            var ctx = new TestContext();
            var b = new JsonBackend();
            b.Routes["GET /users/8/notifications/unread"] = (HttpStatusCode.OK, """{"count":0}""");
            ctx.SignInAs("pat", role, "8");
            ctx.UseBackend(b);
            Assert.Equal(shown, ctx.RenderComponent<FrontEnd.Shared.AccountBar>().FindAll("a[href=announcements]").Count);
        }
    }
}
