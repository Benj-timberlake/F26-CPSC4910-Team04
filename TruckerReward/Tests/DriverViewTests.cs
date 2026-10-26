using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TruckerReward.Tests;

public sealed class DriverViewTests
{
    [Fact]
    public async Task SponsorCanPreviewOnlyDriversInTheirCompany()
    {
        await using var app = new TestApp();
        using var client = await app.Start(builder => builder.Logging.ClearProviders());
        using var db = app.Db();
        var company = new Company { Name = "Sponsor company" };
        var other = new Company { Name = "Other company" };
        db.Companies.AddRange(company, other);
        await db.SaveChangesAsync();
        var sponsor = MakeUser("sponsor", "sponsor", company.Id);
        var driver = MakeUser("driver", "driver", company.Id);
        var outside = MakeUser("outside", "driver", other.Id);
        var unattached = MakeUser("unattached", "sponsor", null);
        db.Users.AddRange(sponsor, driver, outside, unattached);
        await db.SaveChangesAsync();

        var preview = await client.GetFromJsonAsync<Preview>($"/users/{sponsor.Id}/drivers/{driver.Id}/view");
        Assert.Equal(driver.Username, preview!.Username);
        Assert.Equal(driver.Points, preview.Points);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/users/{sponsor.Id}/drivers/{outside.Id}/view")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/users/{sponsor.Id}/drivers/{sponsor.Id}/view")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/users/{driver.Id}/drivers/{driver.Id}/view")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/users/{unattached.Id}/drivers/{driver.Id}/view")).StatusCode);
    }

    private static User MakeUser(string name, string type, int? companyId) => new()
    {
        Username = name, FirstName = name, LastName = "Test",
        Email = name + "@example.com", PhoneNumber = "", UserType = type,
        AccountsCompanies = companyId is int company ? [new AccountsCompany { CompanyId = company }] : [], Points = 200
    };

    private sealed record Preview(int Id, string Username, int Points);
}
