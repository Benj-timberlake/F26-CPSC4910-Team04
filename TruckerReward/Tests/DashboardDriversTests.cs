using System.Net;
using Bunit;
using FrontEnd.Pages;
using Xunit;

namespace TruckerReward.Tests;

public sealed class DashboardDriversTests : TestContext
{
    private readonly JsonBackend backend = new();
    private const string Drivers = """[{"id":8,"username":"bob","firstName":"Bob","lastName":"Baker","email":"bob@example.com","points":100}]""";

    private IRenderedComponent<ManagePoints> RenderDrivers(Action? changed = null)
    {
        this.SignInAs("sue", "sponsor", "5");
        this.UseBackend(backend);
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.OK, Drivers);
        return RenderComponent<ManagePoints>(parameters => parameters
            .Add(p => p.Embedded, true)
            .Add(p => p.PointsChanged, changed ?? (() => { })));
    }

    [Fact]
    public async Task CompactTableKeepsNamePointsAndEyeAndSubmitsFilters()
    {
        var page = RenderDrivers();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));
        Assert.Equal("Bob Baker", page.Find("tbody td:nth-child(2)").TextContent.Trim());
        Assert.DoesNotContain("Email", page.Find("thead").TextContent);
        Assert.DoesNotContain("Spent", page.Find("thead").TextContent);
        Assert.Single(page.FindAll("thead input[type=checkbox]"));
        Assert.Equal("account/driver-view/8", page.Find("tbody td:last-child form").GetAttribute("action"));
        Assert.Single(page.FindAll(".driver-view-eye svg"));

        backend.Routes["GET /users/5/points/drivers?search=amy&sort=earned&from=2026-09-01&to=2026-09-15"] = (HttpStatusCode.OK, "[]");
        Assert.Empty(page.FindAll("form.search"));
        await page.InvokeAsync(page.Instance.OpenFilters);
        page.Find("#driver-filter-search").Change("amy");
        page.Find("#driver-filter-sort").Change("earned");
        page.Find("#driver-filter-from").Change("2026-09-01");
        page.Find("#driver-filter-to").Change("2026-09-15");
        page.Find(".driver-filter-modal form").Submit();
        page.WaitForAssertion(() => Assert.Contains("No drivers found.", page.Markup));
    }

    [Fact]
    public async Task FilterModalValidatesDatesAndCancelDiscardsEdits()
    {
        var page = RenderDrivers();
        await page.InvokeAsync(page.Instance.OpenFilters);
        page.Find("#driver-filter-search").Change("amy");
        page.Find("#driver-filter-from").Change("2026-11-05");
        page.Find("#driver-filter-to").Change("2026-11-01");
        page.Find(".driver-filter-modal form").Submit();
        Assert.Contains("The start date has to be on or before the end date.", page.Find(".driver-filter-modal").TextContent);
        page.FindAll(".driver-filter-modal button").Single(b => b.TextContent == "Cancel").Click();
        Assert.Empty(page.FindAll(".driver-filter-modal"));
        await page.InvokeAsync(page.Instance.OpenFilters);
        Assert.True(string.IsNullOrEmpty(page.Find("#driver-filter-search").GetAttribute("value")));
        Assert.True(string.IsNullOrEmpty(page.Find("#driver-filter-from").GetAttribute("value")));
    }

    [Fact]
    public void ChangingPointsRefreshesSortedDriversAndNotifiesDashboard()
    {
        var changes = 0;
        var page = RenderDrivers(() => changes++);
        backend.Routes["POST /users/5/points/drivers/8"] = (HttpStatusCode.OK, Drivers[1..^1]);
        page.WaitForAssertion(() => page.Find("tbody tr"));
        page.FindAll("tbody button").Single(b => b.TextContent == "Change points").Click();
        page.Find("#change-amount").Change("50");
        page.Find("#change-reason").Change("Safe driving");
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.OK, Drivers.Replace("100", "150"));
        page.FindAll("button").Single(b => b.TextContent == "Save change").Click();
        page.WaitForAssertion(() => Assert.Equal("150", page.Find("td.balance").TextContent));
        Assert.Equal(1, changes);
        Assert.Contains("\"points\":50", backend.Bodies["POST /users/5/points/drivers/8"]);
        Assert.Contains("Safe driving", backend.Bodies["POST /users/5/points/drivers/8"]);
    }

    [Fact]
    public async Task BulkAwardUpdatesSelectedDriversAndNotifiesDashboard()
    {
        var changes = 0;
        var page = RenderDrivers(() => changes++);
        var twoDrivers = Drivers[..^1] + "," + Drivers[1..].Replace("8", "9").Replace("Bob", "Amy").Replace("Baker", "Zane");
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.OK, twoDrivers);
        await page.InvokeAsync(page.Instance.OpenFilters);
        page.Find(".driver-filter-modal form").Submit();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));
        page.Find("thead input[type=checkbox]").Change(true);
        page.FindAll("button").Single(b => b.TextContent == "Award selected").Click();
        page.Find("#bulk-amount").Change("50");
        page.Find("#bulk-reason").Change("Safe week");
        backend.Routes["POST /users/5/points/bulk"] = (HttpStatusCode.OK, """{"message":"Awarded 50 points each."}""");
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.OK, twoDrivers.Replace("100", "150"));
        page.FindAll("button").Single(b => b.TextContent == "Award").Click();
        page.WaitForAssertion(() => Assert.All(page.FindAll("td.balance"), cell => Assert.Equal("150", cell.TextContent)));
        Assert.Equal("""{"driverIds":[8,9],"points":50,"reason":"Safe week"}""", backend.Bodies["POST /users/5/points/bulk"]);
        Assert.Equal(1, changes);
        Assert.Empty(page.FindAll(".bulk-bar"));
        Assert.Empty(page.FindAll(".change-modal"));
    }

    [Fact]
    public void RemovalSendsReasonAndUpdatesTable()
    {
        var page = RenderDrivers();
        backend.Routes["POST /users/5/points/drivers/8/remove"] = (HttpStatusCode.OK, "{}");
        page.WaitForAssertion(() => page.Find("tbody tr"));
        page.FindAll("tbody button").Single(b => b.TextContent == "Remove").Click();
        page.Find("input[placeholder=Reason]").Change("Left the company");
        page.FindAll("tbody button").Single(b => b.TextContent == "Remove from sponsor").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("tbody tr")));
        Assert.Equal("""{"reason":"Left the company"}""", backend.Bodies["POST /users/5/points/drivers/8/remove"]);
    }
}
