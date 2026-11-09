using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using FrontEnd.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AccessibilityTests : TestContext
{
    private readonly JsonBackend backend = new();

    private static void AssertEveryInputIsLabelled(AngleSharp.Dom.IElement root)
    {
        foreach (var input in root.QuerySelectorAll("input, select, textarea"))
        {
            if (input.GetAttribute("type") == "hidden") continue;
            var id = input.GetAttribute("id");
            var labelled = input.HasAttribute("aria-label")
                || (id is not null && root.QuerySelector($"label[for='{id}']") is not null)
                || input.Closest("label") is not null;
            Assert.True(labelled, $"unlabelled control: {input.OuterHtml}");
        }
    }

    [Fact]
    public void RegisterFormLabelsEveryControl()
    {
        Services.AddSingleton(new FrontEnd.CdlStorage(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
        this.AddTestAuthorization();
        backend.Routes["GET /companies"] = (HttpStatusCode.OK, """[{"id":2,"name":"Acme","description":null}]""");
        this.UseBackend(backend);

        var page = RenderComponent<Register>();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("select[name=companyId]")));
        AssertEveryInputIsLabelled(page.Find("form"));
    }

    [Fact]
    public void ForgotPasswordFormLabelsEveryControl()
    {
        this.AddTestAuthorization();
        this.UseBackend(backend);
        var page = RenderComponent<ForgotPassword>();
        AssertEveryInputIsLabelled(page.Find("form"));
    }

    [Fact]
    public void AccountBarOffersSkipLinkAndNamesItsNav()
    {
        this.AddTestAuthorization();
        this.UseBackend(backend);
        var bar = RenderComponent<AccountBar>();
        var skip = bar.Find("a.skip-link");
        Assert.Equal("#main-content", skip.GetAttribute("href"));
        Assert.Equal("Main", bar.Find("nav.account-bar").GetAttribute("aria-label"));
        Assert.Equal("Skip to main content", bar.Find("a").TextContent);
    }

    [Fact]
    public void DevicesTableHasNameAndColumnHeaders()
    {
        backend.Routes["GET /users/8/sessions?current=k1"] = (HttpStatusCode.OK,
            """[{"id":1,"createdAt":"2026-11-02T08:00:00Z","lastSeenAt":"2026-11-02T09:30:00Z","ipAddress":"10.0.0.1","device":"Chrome on macOS","current":false}]""");
        this.SignInAs("bob", null, "8", new Claim(FrontEnd.Auth.TruckerSignIn.SessionKeyClaim, "k1"));
        this.UseBackend(backend);

        var page = RenderComponent<Devices>();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("table")));
        Assert.False(string.IsNullOrWhiteSpace(page.Find("table").GetAttribute("aria-label")));
        Assert.All(page.FindAll("thead th"), th => Assert.Equal("col", th.GetAttribute("scope")));
        Assert.Equal("Sign out Chrome on macOS", page.Find("button.end").GetAttribute("aria-label"));
    }
}
