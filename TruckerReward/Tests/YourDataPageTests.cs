using System.Net;
using System.Security.Claims;
using Bunit;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class YourDataPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private const string Export = """
        {"exportedAt":"2026-11-09T12:00:00Z",
         "profile":{"id":8,"userType":"driver","username":"bob","firstName":"Bob","lastName":"Driver","email":"bob@example.com","phoneNumber":"","address":"",
                    "status":"active","points":200,"emailConfirmedAt":null,"emailCodes":false,"leaderboardHidden":false,"hasPassword":true,"createdAt":"2026-09-01T08:00:00"},
         "companies":[{"companyId":1,"name":"Acme","currentCompanyViewed":true}],
         "points":[{"id":3,"timestamp":"2026-10-07T12:00:00","companyId":1,"company":"Acme","points":300,"reason":"Safe week","rejectionReason":null},
                   {"id":4,"timestamp":"2026-10-08T12:00:00","companyId":1,"company":"Acme","points":-100,"reason":null,"rejectionReason":null}],
         "orders":[],"wishlist":["v1|123"],"notifications":[],"notificationPreferences":[],"sessions":[],"loginAttempts":[],
         "passwordChanges":[],"passkeys":[],"badges":[{"name":"First points","description":"x","earnedAt":"2026-10-07T12:00:00"}],
         "applications":[],"announcements":[]}
        """;

    private IRenderedComponent<YourData> Render(string account, string role = "driver", params Claim[] extra)
    {
        this.SignInAs("bob", role, "8", extra);
        return Show(account);
    }

    private IRenderedComponent<YourData> Show(string account, string? url = null)
    {
        backend.Routes["GET /users/8"] = (HttpStatusCode.OK, account);
        this.UseBackend(backend);
        if (url is not null)
            Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo(url);
        return RenderComponent<YourData>();
    }

    [Fact]
    public void OffersBothDownloadsAndAPasswordConfirmedDelete()
    {
        var page = Render("""{"username":"bob","hasPassword":true}""");
        page.WaitForAssertion(() => page.Find("form[action='account/delete']"));

        Assert.Equal("account/data/export", page.Find("a.json").GetAttribute("href"));
        Assert.Equal("password", page.Find("#delete-confirmation").GetAttribute("type"));
        Assert.Equal("confirmation", page.Find("#delete-confirmation").GetAttribute("name"));
        Assert.Contains("Current password", page.Find("label[for=delete-confirmation]").TextContent);
    }

    [Fact]
    public void AccountsWithoutAPasswordTypeTheirUsername()
    {
        var page = Render("""{"username":"bob","hasPassword":false}""");
        page.WaitForAssertion(() => page.Find("form[action='account/delete']"));

        Assert.Equal("text", page.Find("#delete-confirmation").GetAttribute("type"));
        Assert.Contains("Type your username (bob)", page.Find("label[for=delete-confirmation]").TextContent);
    }

    [Fact]
    public void AdminsAreToldToAskAnotherAdmin()
    {
        var page = Render("""{"username":"bob","hasPassword":true}""", "admin");
        page.WaitForAssertion(() => page.Find("a.json"));

        Assert.Empty(page.FindAll("form[action='account/delete']"));
        Assert.Contains("Ask another admin", page.Markup);
    }

    [Fact]
    public void ViewersSeeNeitherDownloadsNorDelete()
    {
        var page = Render("""{"username":"bob","hasPassword":true}""", "driver", new Claim(FrontEnd.Auth.ViewAs.ViewerIdClaim, "4"));

        Assert.Contains("aren't available while viewing", page.Markup);
        Assert.Empty(page.FindAll("a.json"));
        Assert.Empty(page.FindAll("form"));
        Assert.DoesNotContain(backend.Requests, r => r.StartsWith("GET /users/8"));
    }

    [Fact]
    public void PdfHasASectionPerKindOfData()
    {
        backend.Routes["GET /users/8/export"] = (HttpStatusCode.OK, Export);
        var module = JSInterop.SetupModule("./js/reports.js");
        module.SetupVoid("pdf", _ => true).SetVoidResult();
        var page = Render("""{"username":"bob","hasPassword":true}""");
        page.WaitForAssertion(() => page.Find("button.pdf"));

        page.Find("button.pdf").Click();

        page.WaitForAssertion(() => Assert.Single(module.Invocations));
        var arg = module.Invocations.Single().Arguments.Single()!;
        Assert.Equal("truckerreward-data.pdf", Prop<string>(arg, "filename"));
        Assert.Contains("bob", Prop<string>(arg, "subtitle"));
        var sections = Prop<FrontEnd.Shared.PdfSection[]>(arg, "sections");
        var byHeading = sections.ToDictionary(s => s.Heading);
        Assert.Contains(["Email", "bob@example.com"], byHeading["Profile"].Rows);
        Assert.Equal(["2026-10-07 12:00", "Acme", "+300", "Safe week"], byHeading["Points history"].Rows[0]);
        Assert.Equal(2, byHeading["Points history"].Rows.Length);
        Assert.Equal("v1|123", byHeading["Wishlist"].Rows.Single()[0]);
        Assert.Equal("First points", byHeading["Badges"].Rows.Single()[0]);
        Assert.Equal("None", byHeading["Orders and cart"].Rows.Single()[0]);
        Assert.All(sections, s => Assert.All(s.Rows, r => Assert.Equal(s.Columns.Length, r.Length)));
    }

    [Fact]
    public void ShowsTheRefusalFromADeleteAttempt()
    {
        this.SignInAs("bob", "driver", "8");
        var page = Show("""{"username":"bob","hasPassword":true}""", "account/data?error=Current%20password%20is%20wrong.");

        page.WaitForAssertion(() => Assert.Equal("Current password is wrong.", page.Find(".tr-alert-error").TextContent));
    }

    [Fact]
    public void LoginSaysTheAccountWasDeleted()
    {
        Services.AddSingleton<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>(
            new Microsoft.AspNetCore.Authentication.AuthenticationSchemeProvider(Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Authentication.AuthenticationOptions())));
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("login?deleted=1");

        var login = RenderComponent<Login>();

        Assert.Equal("Your account has been deleted.", login.Find(".tr-alert-success").TextContent);
    }

    private static T Prop<T>(object o, string name) => (T)o.GetType().GetProperty(name)!.GetValue(o)!;
}
