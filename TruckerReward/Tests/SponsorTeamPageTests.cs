using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class SponsorTeamPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private IRenderedComponent<SponsorTeam> Render()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("sue");
        auth.SetRoles("sponsor");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "5"));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        return RenderComponent<SponsorTeam>();
    }

    [Fact]
    public void InvitingPostsAndReloadsTheTeam()
    {
        backend.Routes["GET /users/5/sponsor-team"] = (HttpStatusCode.OK, """[{"id":5,"username":"sue","firstName":"Sue","lastName":"Moss","email":"sue@example.com","status":"active"}]""");
        backend.Routes["POST /users/5/sponsor-team"] = (HttpStatusCode.Created, "{}");
        var page = Render();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".team-list li")));

        backend.Routes["GET /users/5/sponsor-team"] = (HttpStatusCode.OK, """[{"id":5,"username":"sue","firstName":"Sue","lastName":"Moss","email":"sue@example.com","status":"active"},{"id":9,"username":"kim","firstName":"Kim","lastName":"Lee","email":"kim@example.com","status":"active"}]""");
        page.Find("#invite-username").Change("kim");
        page.Find("#invite-first").Change("Kim");
        page.Find("#invite-last").Change("Lee");
        page.Find("#invite-email").Change("kim@example.com");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("kim was added and emailed a link to set a password.", page.Find(".tr-alert-success").TextContent));
        Assert.Equal("""{"username":"kim","firstName":"Kim","lastName":"Lee","email":"kim@example.com"}""", backend.Bodies["POST /users/5/sponsor-team"]);
        Assert.Equal(2, page.FindAll(".team-list li").Count);
    }

    [Fact]
    public void RefusalIsShown()
    {
        backend.Routes["GET /users/5/sponsor-team"] = (HttpStatusCode.OK, "[]");
        backend.Routes["POST /users/5/sponsor-team"] = (HttpStatusCode.Conflict, """{"message":"That username is already taken."}""");
        var page = Render();
        page.WaitForAssertion(() => page.Find("form"));

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("That username is already taken.", page.Find(".tr-alert-error").TextContent));
    }

    [Fact]
    public void SponsorWithoutACompanyIsTold()
    {
        backend.Routes["GET /users/5/sponsor-team"] = (HttpStatusCode.Forbidden, "");
        var page = Render();
        page.WaitForAssertion(() => Assert.Contains("You aren't with a company yet", page.Markup));
        Assert.Empty(page.FindAll("form"));
    }
}
