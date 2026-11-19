using BackEnd.Models;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace TruckerReward.Tests;

public sealed class SalesReportTests
{
    private sealed record Seeded(int Admin, int Sponsor, int Acme, int Beta, int Empty, int Amy, int Bob, int Cal);

    private static async Task<Seeded> Seed(TestApp app)
    {
        using var db = app.Db();
        Company acme = new() { Name = "Acme" }, beta = new() { Name = "Beta" }, empty = new() { Name = "Zed" };
        db.Companies.AddRange(acme, beta, empty);
        await db.SaveChangesAsync();
        var admin = NewUser("root", "admin", null);
        var sponsor = NewUser("sue", "sponsor", acme.Id);
        var amy = NewUser("amy", "driver", acme.Id);
        var bob = NewUser("bob", "driver", beta.Id);
        var cal = NewUser("cal", "driver", acme.Id);
        db.Users.AddRange(admin, sponsor, amy, bob, cal);
        await db.SaveChangesAsync();

        PointsHistory Order(User u, Company c, int points, int day, string? rejected = null) =>
            new() { UserId = u.Id, CompanyId = c.Id, PointsDelta = -points, Timestamp = new DateTime(2026, 9, day, 12, 0, 0, DateTimeKind.Utc), RejectionReason = rejected };
        var placed = Order(amy, acme, 2500, 10);
        var pending = Order(amy, acme, 1000, 20);
        var rejected = Order(amy, acme, 500, 21, "Out of stock");
        var betaOrder = Order(bob, beta, 700, 15);
        var award = new PointsHistory { UserId = amy.Id, CompanyId = acme.Id, PointsDelta = 50, Timestamp = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc) };
        db.PointsHistory.AddRange(placed, pending, rejected, betaOrder, award);
        await db.SaveChangesAsync();
        db.CartItems.AddRange(
            new CartItem { UserId = amy.Id, Name = "Mug", Price = 12.50m, Quantity = 2, InCart = false, WasOrdered = true, PointsHistoryId = placed.Id },
            new CartItem { UserId = amy.Id, Name = "Hat", Price = 10m, InCart = false, PointsHistoryId = pending.Id },
            new CartItem { UserId = amy.Id, Name = "Gone", Price = 5m, InCart = false, PointsHistoryId = rejected.Id },
            new CartItem { UserId = bob.Id, Name = "Cap", Price = 7m, InCart = false, WasOrdered = true, PointsHistoryId = betaOrder.Id });
        await db.SaveChangesAsync();
        return new(admin.Id, sponsor.Id, acme.Id, beta.Id, empty.Id, amy.Id, bob.Id, cal.Id);
    }

    [Fact]
    public async Task AllSponsorsTotalsSalesAndSkipsRejectedOrders()
    {
        await using var app = new TestApp();
        using var client = await app.Start(b => b.Logging.ClearProviders());
        var s = await Seed(app);

        var report = (await client.GetFromJsonAsync<List<SponsorSales>>($"/users/{s.Admin}/reports/sales"))!;
        Assert.Equal(["Acme", "Beta", "Zed"], report.Select(r => r.Name));
        var acme = report[0];
        Assert.Equal(2, acme.Orders);
        Assert.Equal(3, acme.Items);
        Assert.Equal(3500, acme.Points);
        Assert.Equal(35m, acme.Dollars);
        Assert.Equal(["Pending", "Ordered"], acme.Sales.Select(x => x.Status));
        Assert.Equal("Mug x2", acme.Sales[1].Products);
        Assert.All(acme.Sales, x => Assert.Equal("Acme", x.Sponsor));
        Assert.Equal(0, report[2].Orders);
        Assert.Empty(report[2].Sales);
    }

    [Fact]
    public async Task FiltersBySponsorsDatesAndSummary()
    {
        await using var app = new TestApp();
        using var client = await app.Start(b => b.Logging.ClearProviders());
        var s = await Seed(app);

        var two = (await client.GetFromJsonAsync<List<SponsorSales>>(
            $"/users/{s.Admin}/reports/sales?companyIds={s.Beta}&companyIds={s.Acme}&from=2026-09-01&to=2026-09-15&detailed=false"))!;
        Assert.Equal(["Acme", "Beta"], two.Select(r => r.Name));
        Assert.Equal(1, two[0].Orders);
        Assert.Equal(2500, two[0].Points);
        Assert.Equal(1, two[1].Orders);
        Assert.All(two, r => Assert.Empty(r.Sales));

        var one = (await client.GetFromJsonAsync<List<SponsorSales>>($"/users/{s.Admin}/reports/sales?companyIds={s.Beta}"))!;
        Assert.Equal("Cap", Assert.Single(Assert.Single(one).Sales).Products);
    }

    [Fact]
    public async Task OnlyAdminsCanViewIt()
    {
        await using var app = new TestApp();
        using var client = await app.Start(b => b.Logging.ClearProviders());
        var s = await Seed(app);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/users/{s.Sponsor}/reports/sales")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/users/99999/reports/sales")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/users/{s.Admin}/reports/sales?companyIds=99999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/users/{s.Admin}/reports/sales?from=2026-09-30&to=2026-09-01")).StatusCode);
    }

    [Fact]
    public async Task DriverReportCoversOneSponsorsDrivers()
    {
        await using var app = new TestApp();
        using var client = await app.Start(b => b.Logging.ClearProviders());
        var s = await Seed(app);

        var all = (await client.GetFromJsonAsync<List<DriverSales>>($"/users/{s.Admin}/reports/sales/drivers?companyId={s.Acme}"))!;
        Assert.Equal(["amy", "cal"], all.Select(d => d.Username));
        Assert.Equal(2, all[0].Orders);
        Assert.Equal(3500, all[0].Points);
        Assert.Equal(35m, all[0].Dollars);
        Assert.All(all, d => Assert.Equal("Acme", d.Sponsor));
        Assert.All(all[0].Sales, x => Assert.Equal("amy User", x.DriverName));
        Assert.Equal(0, all[1].Orders);

        var one = (await client.GetFromJsonAsync<List<DriverSales>>(
            $"/users/{s.Admin}/reports/sales/drivers?companyId={s.Acme}&driverIds={s.Amy}&to=2026-09-15&detailed=false"))!;
        var amy = Assert.Single(one);
        Assert.Equal(1, amy.Orders);
        Assert.Equal(2500, amy.Points);
        Assert.Empty(amy.Sales);

        var two = (await client.GetFromJsonAsync<List<DriverSales>>(
            $"/users/{s.Admin}/reports/sales/drivers?companyId={s.Acme}&driverIds={s.Cal}&driverIds={s.Amy}"))!;
        Assert.Equal(2, two.Count);
    }

    [Fact]
    public async Task DriverReportRefusesOtherSponsorsDrivers()
    {
        await using var app = new TestApp();
        using var client = await app.Start(b => b.Logging.ClearProviders());
        var s = await Seed(app);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/users/{s.Admin}/reports/sales/drivers?companyId={s.Acme}&driverIds={s.Bob}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/users/{s.Admin}/reports/sales/drivers?companyId=99999")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/users/{s.Sponsor}/reports/sales/drivers?companyId={s.Acme}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/users/{s.Admin}/reports/sales/drivers?companyId={s.Acme}&from=2026-09-30&to=2026-09-01")).StatusCode);
    }

    private static User NewUser(string name, string role, int? companyId) => new()
    {
        Username = name, Email = name + "@example.com", UserType = role,
        AccountsCompanies = companyId is int company ? [new AccountsCompany { CompanyId = company }] : [],
        Password = "hash", PhoneNumber = "", FirstName = name, LastName = "User", Address = ""
    };
}
