using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AdminAccountsPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private const string Accounts = """
        [{"id":4,"username":"ann","firstName":"Ann","lastName":"Admin","email":"ann@example.com","userType":"admin","status":"active","createdAt":"2026-09-01T08:00:00","lastLoginAt":null},
         {"id":8,"username":"bob","firstName":"Bob","lastName":"Baker","email":"bob@example.com","userType":"driver","status":"active","createdAt":null,"lastLoginAt":"2026-09-06T08:00:00"}]
        """;

    private IRenderedComponent<AdminAccounts> Render()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ann");
        auth.SetRoles("admin");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "4"));
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        return RenderComponent<AdminAccounts>();
    }

    [Fact]
    public void BothAdminUrlsOpenThisPage()
    {
        var routes = typeof(AdminAccounts).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), false)
                .Cast<Microsoft.AspNetCore.Components.RouteAttribute>()
                .Select(r => (r.Template, t)))
            .Where(r => r.Template is "/admin/accounts" or "/admin/users")
            .ToList();

        Assert.Equal(2, routes.Count);
        Assert.All(routes, r => Assert.Equal(typeof(AdminAccounts), r.t));
    }

    [Fact]
    public void ListsAccountsWithoutASelfDeactivateButton()
    {
        backend.Routes["GET /admin/accounts"] = (HttpStatusCode.OK, Accounts);
        var page = Render();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));
        var rows = page.FindAll("tbody tr").ToList();
        Assert.Contains("2026-09-01 08:00", rows[0].TextContent);
        Assert.Contains("Never", rows[0].TextContent);
        Assert.Empty(rows[0].QuerySelectorAll("button"));
        Assert.Contains("Unknown", rows[1].TextContent);
        Assert.Equal("Deactivate", rows[1].QuerySelector("button")!.TextContent.Trim());
    }

    [Fact]
    public void FiltersGoIntoTheQuery()
    {
        backend.Routes["GET /admin/accounts"] = (HttpStatusCode.OK, Accounts);
        backend.Routes["GET /admin/accounts?search=bob%20b&type=driver&status=inactive&sort=created"] = (HttpStatusCode.OK, "[]");
        var page = Render();
        page.WaitForAssertion(() => page.Find("tbody tr"));

        page.Find("input[type=search]").Change(" bob b ");
        page.Find("select[aria-label='Account type']").Change("driver");
        page.Find("select[aria-label=Status]").Change("inactive");
        page.Find("select[aria-label=Sort]").Change("created");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("No accounts match.", page.Markup));
    }

    [Fact]
    public void DeactivatingPostsAndReloads()
    {
        backend.Routes["GET /admin/accounts"] = (HttpStatusCode.OK, Accounts);
        backend.Routes["POST /admin/accounts/8/status"] = (HttpStatusCode.NoContent, "");
        var page = Render();
        page.WaitForAssertion(() => page.Find("tbody button"));

        page.Find("tbody button").Click();

        page.WaitForAssertion(() => Assert.Equal("bob was deactivated.", page.Find(".tr-alert-success").TextContent));
        Assert.Equal("""{"adminId":4,"status":"inactive"}""", backend.Bodies["POST /admin/accounts/8/status"]);
        Assert.Equal(2, backend.Requests.Count(r => r == "GET /admin/accounts"));
    }

    [Fact]
    public void RefusalIsShown()
    {
        backend.Routes["GET /admin/accounts"] = (HttpStatusCode.OK, Accounts);
        backend.Routes["POST /admin/accounts/8/status"] = (HttpStatusCode.Conflict, """{"message":"That account is already inactive."}""");
        var page = Render();
        page.WaitForAssertion(() => page.Find("tbody button"));

        page.Find("tbody button").Click();

        page.WaitForAssertion(() => Assert.Equal("That account is already inactive.", page.Find(".tr-alert-error").TextContent));
    }
}
