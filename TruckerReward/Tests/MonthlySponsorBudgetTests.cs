using BackEnd.Models;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TruckerReward.Tests;

public sealed class MonthlySponsorBudgetTests
{
    [Fact]
    public async Task MonthlyDriverAwardsAreCompanyScopedAdditiveAndOnlyOncePerMonth()
    {
        await using var app = new TestApp();
        using var client = await app.Start(builder => builder.Logging.ClearProviders());
        using var db = app.Db();
        var company = new Company { Name = "Monthly awards" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var sponsor = MakeUser("monthly-sponsor", "sponsor", company.Id);
        var driver = MakeUser("monthly-driver", "driver", company.Id);
        var outsider = MakeUser("other-driver", "driver", null);
        db.Users.AddRange(sponsor, driver, outsider);
        await db.SaveChangesAsync();
        var response = await client.PostAsJsonAsync($"/users/{sponsor.Id}/points/monthly", new { Points = 100 });
        response.EnsureSuccessStatusCode();
        await MonthlySponsorBudgetService.AwardDriverPoints(db, app.Clock.GetUtcNow());
        Assert.Empty(await db.PointsHistory.ToListAsync());
        var boundary = new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero);
        await MonthlySponsorBudgetService.AwardDriverPoints(db, boundary.AddMinutes(-1));
        Assert.Empty(await db.PointsHistory.ToListAsync());
        await MonthlySponsorBudgetService.AwardDriverPoints(db, boundary);
        await MonthlySponsorBudgetService.AwardDriverPoints(db, boundary.AddDays(1));
        db.ChangeTracker.Clear();
        Assert.Equal(125, (await db.Users.FindAsync(driver.Id))!.Points);
        Assert.Equal(25, (await db.Users.FindAsync(outsider.Id))!.Points);
        Assert.Equal(25, (await db.Users.FindAsync(sponsor.Id))!.Points);
        var history = Assert.Single(await db.PointsHistory.ToListAsync());
        Assert.Equal(company.Id, history.CompanyId);
        Assert.Equal(100, history.PointsDelta);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/users/{driver.Id}/points/monthly", new { Points = 200 })).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/users/{sponsor.Id}/points/monthly", new { Points = -1 })).StatusCode);
    }

    [Fact]
    public async Task ResetsAtEasternMonthBoundaryOnlyOnceAndCatchesUpAfterDowntime()
    {
        await using var app = new TestApp();
        using var client = await app.Start(builder => builder.Logging.ClearProviders());
        using var db = app.Db();
        var company = new Company { Name = "Budget company", MonthlyBudget = 1000 };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var sponsor = MakeUser("sponsor", "sponsor", company.Id);
        var driver = MakeUser("driver", "driver", company.Id);
        var unattached = MakeUser("unattached", "sponsor", null);
        db.Users.AddRange(sponsor, driver, unattached);
        await db.SaveChangesAsync();

        // November starts at 04:00 UTC in Eastern time in 2026.
        await MonthlySponsorBudgetService.ResetDueBalances(db,
            new DateTimeOffset(2026, 11, 1, 3, 59, 0, TimeSpan.Zero));
        db.ChangeTracker.Clear();
        Assert.Equal(25, (await db.Users.FindAsync(sponsor.Id))!.Points);
        await MonthlySponsorBudgetService.ResetDueBalances(db,
            new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero));
        db.ChangeTracker.Clear();
        Assert.Equal(1000, (await db.Users.FindAsync(sponsor.Id))!.Points);
        Assert.Equal(25, (await db.Users.FindAsync(driver.Id))!.Points);
        Assert.Equal(25, (await db.Users.FindAsync(unattached.Id))!.Points);

        await db.Users.Where(u => u.Id == sponsor.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Points, 10));
        await MonthlySponsorBudgetService.ResetDueBalances(db,
            new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        db.ChangeTracker.Clear();
        Assert.Equal(10, (await db.Users.FindAsync(sponsor.Id))!.Points);

        await MonthlySponsorBudgetService.ResetDueBalances(db,
            new DateTimeOffset(2027, 1, 3, 12, 0, 0, TimeSpan.Zero));
        db.ChangeTracker.Clear();
        var updated = (await db.Users.FindAsync(sponsor.Id))!;
        Assert.Equal(1000, updated.Points);
        Assert.Equal(new DateTime(2027, 1, 1), updated.BudgetResetMonth);
    }

    private static User MakeUser(string name, string type, int? companyId) => new()
    {
        Username = name, FirstName = name, LastName = "Test",
        Email = name + "@example.com", PhoneNumber = "", UserType = type,
        AccountsCompanies = companyId is int company ? [new AccountsCompany { CompanyId = company }] : [],
        Points = 25, BudgetResetMonth = new DateTime(2026, 10, 1)
    };
}
