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
        backend.Routes.TryAdd("GET /companies", (HttpStatusCode.OK, """[{"id":2,"name":"Acme","description":null}]"""));
        backend.Routes.TryAdd("GET /admin/accounts/counts", (HttpStatusCode.OK, """{"activeDrivers":12,"activeSponsors":3,"activeAdmins":1,"inactive":2}"""));
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
        backend.Routes["GET /admin/accounts?search=bob%20b&type=driver&companyId=2&status=inactive&sort=created"] = (HttpStatusCode.OK, "[]");
        var page = Render();
        page.WaitForAssertion(() => page.Find("tbody tr"));

        page.Find("input[type=search]").Change(" bob b ");
        page.Find("select[aria-label='Account type']").Change("driver");
        page.Find("select[aria-label=Sponsor]").Change("2");
        page.Find("select[aria-label=Status]").Change("inactive");
        page.Find("select[aria-label=Sort]").Change("created");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("No accounts match.", page.Markup));
    }

    [Fact]
    public void ShowsActiveCounts()
    {
        backend.Routes["GET /admin/accounts"] = (HttpStatusCode.OK, Accounts);
        var page = Render();

        page.WaitForAssertion(() => Assert.Equal("12", page.Find(".counts .drivers").TextContent));
        Assert.Equal("3", page.Find(".counts .sponsors").TextContent);
        Assert.Contains("Acme", page.Find("select[aria-label=Sponsor]").TextContent);
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

    [Fact]
    public void EditingLoadsTheAccountAndSaves()
    {
        backend.Routes["GET /admin/accounts"] = (HttpStatusCode.OK, Accounts);
        backend.Routes["GET /users/8"] = (HttpStatusCode.OK, """{"id":8,"userType":"driver","username":"bob","firstName":"Bob","lastName":"Baker","email":"bob@example.com","phoneNumber":"555","address":"","companyName":null,"companyId":null,"points":0,"pendingCompanyName":null}""");
        backend.Routes["PUT /admin/accounts/8"] = (HttpStatusCode.NoContent, "");
        var page = Render();
        page.WaitForAssertion(() => page.Find("button.edit"));

        page.Find("button.edit").Click();
        page.WaitForAssertion(() => Assert.Equal("555", page.Find("input[aria-label='Phone number']").GetAttribute("value")));
        page.Find("input[aria-label='First name']").Change("Robert");
        page.Find("select[aria-label='Account sponsor']").Change("2");
        page.Find("form.edit-form").Submit();

        page.WaitForAssertion(() => Assert.Equal("bob was updated.", page.Find(".tr-alert-success").TextContent));
        Assert.Equal("""{"adminId":4,"firstName":"Robert","lastName":"Baker","email":"bob@example.com","phoneNumber":"555","address":"","companyId":2}""", backend.Bodies["PUT /admin/accounts/8"]);
        Assert.Empty(page.FindAll("form.edit-form"));
    }

    [Fact]
    public void EditRefusalIsShown()
    {
        backend.Routes["GET /admin/accounts"] = (HttpStatusCode.OK, Accounts);
        backend.Routes["GET /users/8"] = (HttpStatusCode.OK, """{"id":8,"userType":"driver","username":"bob","firstName":"Bob","lastName":"Baker","email":"bob@example.com","phoneNumber":"","address":"","companyName":null,"companyId":null,"points":0,"pendingCompanyName":null}""");
        backend.Routes["PUT /admin/accounts/8"] = (HttpStatusCode.Conflict, """{"message":"That email address is already in use."}""");
        var page = Render();
        page.WaitForAssertion(() => page.Find("button.edit"));

        page.Find("button.edit").Click();
        page.WaitForAssertion(() => page.Find("form.edit-form"));
        page.Find("form.edit-form").Submit();

        page.WaitForAssertion(() => Assert.Equal("That email address is already in use.", page.Find(".tr-alert-error").TextContent));
        Assert.Single(page.FindAll("form.edit-form"));
    }

    [Fact]
    public void CreatingPostsTheNewAccount()
    {
        backend.Routes["GET /admin/accounts"] = (HttpStatusCode.OK, Accounts);
        backend.Routes["POST /admin/accounts"] = (HttpStatusCode.Created, "{}");
        var page = Render();
        page.WaitForAssertion(() => page.Find("tbody tr"));

        page.Find("button.new-account").Click();
        page.Find("input[aria-label='New username']").Change("kim");
        page.Find("input[aria-label='New first name']").Change("Kim");
        page.Find("input[aria-label='New last name']").Change("Lee");
        page.Find("input[aria-label='New email']").Change("kim@example.com");
        page.Find("select[aria-label='New account sponsor']").Change("2");
        page.Find("form.create-form").Submit();

        page.WaitForAssertion(() => Assert.Equal("kim was created and emailed a link to set a password.", page.Find(".tr-alert-success").TextContent));
        Assert.Equal("""{"adminId":4,"userType":"driver","username":"kim","firstName":"Kim","lastName":"Lee","email":"kim@example.com","companyId":2}""", backend.Bodies["POST /admin/accounts"]);
        Assert.Empty(page.FindAll("form.create-form"));
    }

    [Fact]
    public void AdminAccountsHaveNoSponsorPicker()
    {
        backend.Routes["GET /admin/accounts"] = (HttpStatusCode.OK, Accounts);
        var page = Render();
        page.WaitForAssertion(() => page.Find("tbody tr"));

        page.Find("button.new-account").Click();
        page.Find("select[aria-label='New account type']").Change("admin");

        Assert.Empty(page.FindAll("select[aria-label='New account sponsor']"));
    }

    [Fact]
    public void ViewAsPostsForActiveNonAdmins()
    {
        backend.Routes["GET /admin/accounts"] = (HttpStatusCode.OK, Accounts);
        var page = Render();

        page.WaitForAssertion(() => Assert.Single(page.FindAll("form.view-as")));
        Assert.Equal("account/view-as/8", page.Find("form.view-as").GetAttribute("action"));
    }

    [Fact]
    public void NoSponsorFilterGoesIntoTheQuery()
    {
        backend.Routes["GET /admin/accounts"] = (HttpStatusCode.OK, Accounts);
        backend.Routes["GET /admin/accounts?noSponsor=true"] = (HttpStatusCode.OK, "[]");
        var page = Render();
        page.WaitForAssertion(() => page.Find("tbody tr"));

        page.Find("select[aria-label=Sponsor]").Change("-1");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("No accounts match.", page.Markup));
    }
}
