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
        backend.Routes["GET /companies"] = (HttpStatusCode.OK, """[{"id":2,"name":"Acme","description":null}]""");
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);

        var page = RenderComponent<Register>();

        page.WaitForAssertion(() => Assert.True(page.Find("select[name=companyId]").HasAttribute("required")));
        Assert.Contains("Acme", page.Find("select[name=companyId]").TextContent);
        page.Find("#userType").Change("sponsor");
        Assert.Empty(page.FindAll("select[name=companyId]"));
    }
}
