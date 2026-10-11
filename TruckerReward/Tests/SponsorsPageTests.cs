using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class SponsorsPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private IRenderedComponent<Sponsors> Render()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("sue");
        auth.SetRoles("sponsor");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "5"));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        return RenderComponent<Sponsors>();
    }

    [Fact]
    public void ListsEveryCompanyButTheSponsorsOwn()
    {
        backend.Routes["GET /users/5"] = (HttpStatusCode.OK, """{"companyId":2}""");
        backend.Routes["GET /companies"] = (HttpStatusCode.OK, """[{"id":1,"name":"Beta Freight","description":"Hauls things"},{"id":2,"name":"Acme","description":null},{"id":3,"name":"Cargo Co","description":null}]""");
        var page = Render();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".sponsor-list li").Count));
        Assert.DoesNotContain("Acme", page.Markup);
        Assert.Contains("Hauls things", page.Markup);
        Assert.Contains("No description.", page.Markup);
    }

    [Fact]
    public void FailureIsShown()
    {
        backend.Routes["GET /users/5"] = (HttpStatusCode.InternalServerError, "");
        var page = Render();
        page.WaitForAssertion(() => Assert.Contains("Couldn't load sponsors", page.Find(".tr-alert-error").TextContent));
    }
}
