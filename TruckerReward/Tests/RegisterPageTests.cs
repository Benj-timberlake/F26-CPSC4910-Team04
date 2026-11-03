using System.Net;
using Bunit;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class RegisterPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    [Fact]
    public void DriversPickTheirSponsor()
    {
        Services.AddSingleton(new FrontEnd.CdlStorage(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
        Bunit.TestDoubles.FakeAuthorizationExtensions.AddTestAuthorization(this);
        backend.Routes["GET /companies"] = (HttpStatusCode.OK, """[{"id":2,"name":"Acme","description":null}]""");
        this.UseBackend(backend);

        var page = RenderComponent<Register>();

        page.WaitForAssertion(() => Assert.True(page.Find("select[name=companyId]").HasAttribute("required")));
        Assert.Contains("Acme", page.Find("select[name=companyId]").TextContent);
        Assert.Equal("multipart/form-data", page.Find("form").GetAttribute("enctype"));
        Assert.Equal("file", page.Find("input[name=cdlLicense]").GetAttribute("type"));
        page.Find("#userType").Change("sponsor");
        Assert.Empty(page.FindAll("select[name=companyId]"));
        Assert.Empty(page.FindAll("input[name=cdlLicense]"));
    }
}
