using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class PointsPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    private IRenderedComponent<Points> Render(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("bob");
        auth.SetRoles("driver");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "7"));
        backend.Routes["GET /users/7/points"] = (status, json);
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        return RenderComponent<Points>();
    }

    [Fact]
    public void ShowsTotalsAndHistory()
    {
        var page = Render("""
            {"balance":1260,"earnedThisMonth":300,"earned":1800,"spent":500,"deducted":40,
             "history":[{"at":"2026-10-07T12:00:00","points":-40,"reason":"Late delivery","by":"sue"},
                        {"at":"2026-10-06T09:30:00","points":-500,"reason":"Order","by":null},
                        {"at":"2026-10-05T08:00:00","points":300,"reason":"Safe week","by":"sue"}]}
            """);

        page.WaitForAssertion(() => Assert.Equal("1,260", page.Find(".balance").TextContent));
        Assert.Equal("300", page.Find(".month").TextContent);
        Assert.Equal("1,800", page.Find(".earned").TextContent);
        Assert.Equal("500", page.Find(".spent").TextContent);
        Assert.Equal("40", page.Find(".deducted").TextContent);
        var rows = page.FindAll("tbody tr").ToList();
        Assert.Equal(3, rows.Count);
        Assert.Contains("-40", rows[0].TextContent);
        Assert.Contains("Late delivery", rows[0].TextContent);
        Assert.Contains("+300", rows[2].TextContent);
        Assert.Contains("minus", rows[0].QuerySelector("td.minus")!.ClassName);
    }

    [Fact]
    public void EmptyHistorySaysSo()
    {
        var page = Render("""{"balance":0,"earnedThisMonth":0,"earned":0,"spent":0,"deducted":0,"history":[]}""");
        page.WaitForAssertion(() => Assert.Contains("No point changes yet.", page.Markup));
    }

    [Fact]
    public void BackendFailureShowsAnError()
    {
        var page = Render("", HttpStatusCode.InternalServerError);
        page.WaitForAssertion(() => Assert.Contains("Couldn't load your points", page.Find(".tr-alert-error").TextContent));
    }
}
