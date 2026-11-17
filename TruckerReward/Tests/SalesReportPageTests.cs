using System.Net;
using Bunit;
using FrontEnd.Pages;
using FrontEnd.Shared;
using Xunit;

namespace TruckerReward.Tests;

public sealed class SalesReportPageTests : TestContext
{
    private readonly JsonBackend backend = new();
    private const string Companies = """[{"id":1,"name":"Acme"},{"id":2,"name":"Beta"},{"id":3,"name":"Zed"}]""";
    private const string Summary = """[{"companyId":1,"name":"Acme","orders":2,"items":3,"points":3500,"dollars":35,"sales":[]},{"companyId":2,"name":"Beta","orders":1,"items":1,"points":700,"dollars":7,"sales":[]}]""";
    private const string Detailed = """[{"companyId":2,"name":"Beta","orders":1,"items":1,"points":700,"dollars":7,"sales":[{"orderId":4,"at":"2026-09-15T12:00:00","driverName":"Bob User","username":"bob","products":"Cap","items":1,"points":700,"dollars":7,"status":"Ordered"}]}]""";

    private IRenderedComponent<SalesReport> Render()
    {
        this.SignInAs("root", "admin", "9");
        this.UseBackend(backend);
        backend.Routes["GET /companies"] = (HttpStatusCode.OK, Companies);
        backend.Routes["GET /users/9/reports/sales?detailed=false"] = (HttpStatusCode.OK, Summary);
        return RenderComponent<SalesReport>();
    }

    [Fact]
    public void ShowsAllSponsorSummaryWithTotals()
    {
        var page = Render();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));
        Assert.Contains("3,500", page.Find("tbody").TextContent);
        var total = page.Find("tfoot").TextContent;
        Assert.Contains("4,200", total);
        Assert.Contains("$42.00", total);
        Assert.Contains("All sponsors, all time, summary.", page.Markup);
        Assert.Empty(page.FindAll(".sales-detail"));
    }

    [Fact]
    public void SendsPickedSponsorsDatesAndDetail()
    {
        backend.Routes["GET /users/9/reports/sales?detailed=true&companyIds=2&from=2026-09-01&to=2026-09-30"] = (HttpStatusCode.OK, Detailed);
        var page = Render();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));
        page.Find("#sales-all").Change(false);
        page.Find("#sales-company-2").Change(true);
        page.Find("#sales-from").Change("2026-09-01");
        page.Find("#sales-to").Change("2026-09-30");
        page.Find("#sales-view").Change("detailed");
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".sales-detail")));
        var detail = page.Find(".sales-detail").TextContent;
        Assert.Contains("Bob User (bob)", detail);
        Assert.Contains("Cap", detail);
        Assert.Contains("Ordered", detail);
        Assert.Contains("2026-09-15 12:00", detail);
        Assert.Empty(page.FindAll("tfoot"));
    }

    [Fact]
    public void NeedsASponsorAndAValidRange()
    {
        var page = Render();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));
        var requests = backend.Requests.Count;
        page.Find("#sales-all").Change(false);
        page.Find("form").Submit();
        Assert.Contains("Pick at least one sponsor", page.Markup);
        page.Find("#sales-all").Change(true);
        page.Find("#sales-from").Change("2026-09-30");
        page.Find("#sales-to").Change("2026-09-01");
        page.Find("form").Submit();
        Assert.Contains("start date must be on or before", page.Markup);
        Assert.Equal(requests, backend.Requests.Count);
    }

    [Fact]
    public void BuildsThePdfFromTheReport()
    {
        var module = JSInterop.SetupModule("./js/reports.js");
        module.SetupVoid("pdf", _ => true).SetVoidResult();
        var page = Render();
        page.WaitForAssertion(() => page.Find("button.pdf"));
        page.Find("button.pdf").Click();
        page.WaitForAssertion(() => Assert.Single(module.Invocations));
        var arg = module.Invocations.Single().Arguments.Single()!;
        Assert.Equal("sales-report.pdf", Prop<string>(arg, "filename"));
        var section = Assert.Single(Prop<PdfSection[]>(arg, "sections"));
        Assert.Equal(["Total", "3", "4", "4,200", "$42.00"], section.Rows[^1]);
    }

    private static T Prop<T>(object o, string name) => (T)o.GetType().GetProperty(name)!.GetValue(o)!;
}
