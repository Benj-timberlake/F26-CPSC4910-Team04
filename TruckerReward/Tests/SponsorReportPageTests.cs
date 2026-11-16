using System.Net;
using Bunit;
using FrontEnd.Pages;
using Xunit;

namespace TruckerReward.Tests;

public sealed class SponsorReportPageTests : TestContext
{
    private readonly JsonBackend backend = new();
    private const string Report = """[{"driverId":8,"driverName":"Bob Baker","username":"bob","totalPoints":130,"changes":[{"at":"2026-09-15T23:59:59","points":-20,"sponsorName":"Sue Smith","reason":"Safety review"}]},{"driverId":9,"driverName":"Amy Zane","username":"amy","totalPoints":0,"changes":[]}]""";

    private IRenderedComponent<SponsorReport> Render()
    {
        this.SignInAs("sue", "sponsor", "5");
        this.UseBackend(backend);
        backend.Routes["GET /users/5/reports/driver-points"] = (HttpStatusCode.OK, Report);
        return RenderComponent<SponsorReport>();
    }

    [Fact]
    public void ShowsReportAndSendsDriverAndDateFilters()
    {
        backend.Routes["GET /users/5/reports/driver-points?driverId=8&from=2026-09-01&to=2026-09-30"] = (HttpStatusCode.OK, "[]");
        var page = Render();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));
        var table = page.Find("table").TextContent;
        Assert.Contains("Bob Baker", table);
        Assert.Contains("130", table);
        Assert.Contains("-20", table);
        Assert.Contains("2026-09-15 23:59", table);
        Assert.Contains("Sue Smith", table);
        Assert.Contains("Safety review", table);
        Assert.Contains("No point changes in this date range.", table);
        page.Find("#report-driver").Change("8");
        page.Find("#report-from").Change("2026-09-01");
        page.Find("#report-to").Change("2026-09-30");
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Contains("No drivers found", page.Markup));
        Assert.Contains(backend.Requests, r => r.Contains("driverId=8&from=2026-09-01&to=2026-09-30"));
    }

    [Fact]
    public void RejectsReversedDateRange()
    {
        var page = Render();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));
        var requests = backend.Requests.Count;
        page.Find("#report-from").Change("2026-09-30");
        page.Find("#report-to").Change("2026-09-01");
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Contains("start date must be on or before", page.Markup));
        Assert.Equal(requests, backend.Requests.Count);
    }
}
