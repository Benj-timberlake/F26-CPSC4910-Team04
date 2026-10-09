using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AuditLogPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private const string Drivers = """[{"id":8,"username":"bob","firstName":"Bob","lastName":"Baker","email":"bob@example.com","points":100}]""";
    private const string Rows = """
        [{"at":"2026-09-15T12:00:00","category":"points","userId":8,"username":"bob","detail":"+250 points: Safe week","by":"sue"},
         {"at":"2026-09-10T12:00:00","category":"logins","userId":8,"username":"bob","detail":"Signed in from 1.2.3.4","by":null}]
        """;

    private IRenderedComponent<AuditLog> Render(string role, int id)
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("someone");
        auth.SetRoles(role);
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, id.ToString()));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        return RenderComponent<AuditLog>();
    }

    [Fact]
    public void SponsorSeesTheirDriversLog()
    {
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.OK, Drivers);
        backend.Routes["GET /users/5/audit"] = (HttpStatusCode.OK, Rows);
        var page = Render("sponsor", 5);

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));
        Assert.Contains("+250 points: Safe week", page.Find("tbody").TextContent);
        Assert.Empty(page.FindAll("select[aria-label=Sponsor]"));
        Assert.Contains("Bob Baker (bob)", page.Find("select[aria-label=Driver]").TextContent);
    }

    [Fact]
    public void AdminFiltersGoIntoTheQuery()
    {
        backend.Routes["GET /users/4/points/drivers"] = (HttpStatusCode.OK, Drivers);
        backend.Routes["GET /companies"] = (HttpStatusCode.OK, """[{"id":2,"name":"Acme","description":null}]""");
        backend.Routes["GET /users/4/audit"] = (HttpStatusCode.OK, Rows);
        backend.Routes["GET /users/4/audit?category=points&companyId=2&driverId=8&from=2026-09-01&to=2026-09-30"] = (HttpStatusCode.OK, "[]");
        var page = Render("admin", 4);
        page.WaitForAssertion(() => page.Find("tbody tr"));

        page.Find("select[aria-label=Category]").Change("points");
        page.Find("select[aria-label=Sponsor]").Change("2");
        page.Find("select[aria-label=Driver]").Change("8");
        page.FindAll("input[type=date]").First().Change("2026-09-01");
        page.FindAll("input[type=date]").Last().Change("2026-09-30");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Nothing in this range.", page.Markup));
    }

    [Fact]
    public void SponsorWithoutACompanyIsTold()
    {
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.Forbidden, "");
        var page = Render("sponsor", 5);
        page.WaitForAssertion(() => Assert.Contains("You aren't with a company yet", page.Markup));
        Assert.DoesNotContain(backend.Requests, r => r.Contains("/audit"));
    }
}
