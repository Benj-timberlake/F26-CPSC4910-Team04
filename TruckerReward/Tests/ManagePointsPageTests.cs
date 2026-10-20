using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ManagePointsPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private const string Drivers = """
        [{"id":8,"username":"bob","firstName":"Bob","lastName":"Baker","email":"bob@example.com","points":100},
         {"id":9,"username":"amy","firstName":"Amy","lastName":"Zane","email":"amy@example.com","points":0}]
        """;

    private IRenderedComponent<ManagePoints> Render(params Claim[] extra) => Render("sponsor", extra);

    private IRenderedComponent<ManagePoints> Render(string role, params Claim[] extra)
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("sue");
        auth.SetRoles(role);
        auth.SetClaims([new Claim(ClaimTypes.NameIdentifier, "5"), .. extra]);
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        return RenderComponent<ManagePoints>();
    }

    [Fact]
    public void ListsAndSearchesDrivers()
    {
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.OK, Drivers);
        backend.Routes["GET /users/5/points/drivers?search=amy%20z"] = (HttpStatusCode.OK, """[{"id":9,"username":"amy","firstName":"Amy","lastName":"Zane","email":"amy@example.com","points":0}]""");
        var page = Render();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));
        page.Find("#search").Change(" amy z ");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));
        Assert.Contains("Amy Zane (amy)", page.Find("tbody").TextContent);
    }

    [Fact]
    public void SortsBySpending()
    {
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.OK, Drivers);
        backend.Routes["GET /users/5/points/drivers?sort=spent"] = (HttpStatusCode.OK, """[{"id":9,"username":"amy","firstName":"Amy","lastName":"Zane","email":"amy@example.com","points":0,"spent":900}]""");
        var page = Render();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));

        page.Find("select[aria-label=Sort]").Change("spent");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("900", page.Find("td.spent").TextContent));
    }

    [Fact]
    public void ChangingPointsSendsTheAmountAndReason()
    {
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.OK, Drivers);
        backend.Routes["POST /users/5/points/drivers/8"] = (HttpStatusCode.OK, """{"id":8,"username":"bob","firstName":"Bob","lastName":"Baker","email":"bob@example.com","points":350}""");
        var page = Render();
        page.WaitForAssertion(() => page.Find("tbody tr"));

        page.FindAll("tbody button").First().Click();
        page.Find("input[type=number]").Change("250");
        page.Find("input[placeholder=Reason]").Change("Safe week");
        page.FindAll("tbody button").Single(b => b.TextContent == "Save").Click();

        page.WaitForAssertion(() => Assert.Equal("bob now has 350 points.", page.Find(".tr-alert-success").TextContent));
        Assert.Equal("""{"points":250,"reason":"Safe week"}""", backend.Bodies["POST /users/5/points/drivers/8"]);
        Assert.Equal("350", page.FindAll("td.balance").First().TextContent);
        Assert.Empty(page.FindAll("input[type=number]"));
    }

    [Fact]
    public void ServerMessageIsShown()
    {
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.OK, Drivers);
        backend.Routes["POST /users/5/points/drivers/8"] = (HttpStatusCode.BadRequest, """{"message":"Give a reason of up to 500 characters."}""");
        var page = Render();
        page.WaitForAssertion(() => page.Find("tbody tr"));

        page.FindAll("tbody button").First().Click();
        page.Find("input[type=number]").Change("10");
        page.FindAll("tbody button").Single(b => b.TextContent == "Save").Click();

        page.WaitForAssertion(() => Assert.Equal("Give a reason of up to 500 characters.", page.Find(".tr-alert-error").TextContent));
    }

    [Fact]
    public void SponsorWithoutACompanyIsTold()
    {
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.Forbidden, "");
        var page = Render();
        page.WaitForAssertion(() => Assert.Contains("You aren't with a company yet", page.Markup));
    }

    // sponsors get the driver view on the dashboard instead
    [Theory]
    [InlineData("admin", false, 2)]
    [InlineData("admin", true, 0)]
    [InlineData("sponsor", false, 0)]
    public void ViewAsIsOfferedToAdminsUnlessAlreadyViewing(string role, bool viewing, int forms)
    {
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.OK, Drivers);
        var page = Render(role, viewing ? [new Claim(FrontEnd.Auth.ViewAs.ViewerIdClaim, "4")] : []);

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));
        Assert.Equal(forms, page.FindAll("form.view-as").Count);
    }

    [Fact]
    public void RemovingADriverPostsTheReasonAndDropsTheRow()
    {
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.OK, Drivers);
        backend.Routes["POST /users/5/points/drivers/8/remove"] = (HttpStatusCode.NoContent, "");
        var page = Render();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));

        page.FindAll("button.remove").First().Click();
        page.Find("input[aria-label='Why remove bob']").Change("Left the company");
        page.FindAll("button").First(b => b.TextContent == "Remove from sponsor").Click();

        page.WaitForAssertion(() => Assert.Equal("bob was removed and told why.", page.Find(".tr-alert-success").TextContent));
        Assert.Equal("""{"reason":"Left the company"}""", backend.Bodies["POST /users/5/points/drivers/8/remove"]);
        Assert.Single(page.FindAll("tbody tr"));
    }
}
