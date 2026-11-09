using System.Net;
using Bunit;
using FrontEnd.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class PdfReportPageTests : TestContext
{
    private readonly JsonBackend backend = new();

    [Fact]
    public void PointsPageBuildsThePdfFromItsHistory()
    {
        this.SignInAs("bob", "driver", "7");
        backend.Routes["GET /users/7/points"] = (HttpStatusCode.OK, """
            {"balance":260,"earnedThisMonth":300,"earned":800,"spent":500,"deducted":40,
             "history":[{"at":"2026-10-07T12:00:00","points":-40,"reason":"Late delivery","by":"sue"},
                        {"at":"2026-10-05T08:00:00","points":300,"reason":"Safe week","by":"sue"}]}
            """);
        this.UseBackend(backend);
        var module = JSInterop.SetupModule("./js/reports.js");
        var call = module.SetupVoid("pdf", _ => true);
        var page = RenderComponent<Points>();

        page.WaitForAssertion(() => page.Find("button.pdf"));
        page.Find("button.pdf").Click();

        page.WaitForAssertion(() => Assert.Single(module.Invocations));
        var arg = module.Invocations.Single().Arguments.Single()!;
        Assert.Equal("Points history", Prop<string>(arg, "title"));
        Assert.Equal("points-history.pdf", Prop<string>(arg, "filename"));
        Assert.Contains("bob", Prop<string>(arg, "subtitle"));
        Assert.Contains("UTC", Prop<string>(arg, "subtitle"));
        Assert.Equal(["When (UTC)", "Points", "Reason", "By"], Prop<string[]>(arg, "columns"));
        var rows = Prop<string[][]>(arg, "rows");
        Assert.Equal(2, rows.Length);
        Assert.Equal(["2026-10-07 12:00", "-40", "Late delivery", "sue"], rows[0]);
        call.SetVoidResult();
    }

    [Fact]
    public void AuditLogBuildsThePdfFromTheShownRows()
    {
        this.SignInAs("someone", "sponsor", "5");
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.OK, "[]");
        backend.Routes["GET /users/5/audit"] = (HttpStatusCode.OK, """
            [{"at":"2026-09-15T12:00:00","category":"points","userId":8,"username":"bob","detail":"+250 points: Safe week","by":"sue"},
             {"at":"2026-09-10T12:00:00","category":"logins","userId":8,"username":"bob","detail":"Signed in from 1.2.3.4","by":null}]
            """);
        backend.Routes["GET /users/5/audit?category=points"] = (HttpStatusCode.OK, """
            [{"at":"2026-09-15T12:00:00","category":"points","userId":8,"username":"bob","detail":"+250 points: Safe week","by":"sue"}]
            """);
        this.UseBackend(backend);
        var module = JSInterop.SetupModule("./js/reports.js");
        module.SetupVoid("pdf", _ => true).SetVoidResult();
        var page = RenderComponent<AuditLog>();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));

        page.Find("select[aria-label=Category]").Change("points");
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));
        page.Find("button.pdf").Click();

        page.WaitForAssertion(() => Assert.Single(module.Invocations));
        var arg = module.Invocations.Single().Arguments.Single()!;
        Assert.Equal("Audit log", Prop<string>(arg, "title"));
        Assert.Equal("audit-log.pdf", Prop<string>(arg, "filename"));
        Assert.Contains("category Points", Prop<string>(arg, "subtitle"));
        Assert.Equal(5, Prop<string[]>(arg, "columns").Length);
        Assert.Single(Prop<string[][]>(arg, "rows"));
    }

    [Fact]
    public void AuditLogHasNoPdfButtonWhenNothingIsShown()
    {
        this.SignInAs("someone", "sponsor", "5");
        backend.Routes["GET /users/5/points/drivers"] = (HttpStatusCode.OK, "[]");
        backend.Routes["GET /users/5/audit"] = (HttpStatusCode.OK, "[]");
        this.UseBackend(backend);
        var page = RenderComponent<AuditLog>();
        page.WaitForAssertion(() => Assert.Contains("Nothing in this range.", page.Markup));
        Assert.Empty(page.FindAll("button.pdf"));
    }

    private static T Prop<T>(object o, string name) => (T)o.GetType().GetProperty(name)!.GetValue(o)!;
}
