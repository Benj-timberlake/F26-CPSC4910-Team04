using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TruckerReward.Tests;

public sealed class CatalogEndpointTests
{
    [Fact]
    public async Task DriverCatalogOnlyIncludesSponsorsInTheirCompany()
    {
        await using var app = new TestApp();
        using var client = await app.Start(builder => builder.Logging.ClearProviders());
        using var db = app.Db();
        var company = new Company { Name = "Driver company" };
        var otherCompany = new Company { Name = "Other company" };
        db.Companies.AddRange(company, otherCompany);
        await db.SaveChangesAsync();
        var driver = NewUser("driver");
        var sponsor = NewUser("sponsor");
        var other = NewUser("sponsor");
        other.Username = "other";
        other.Email = "other@example.com";
        driver.CompanyId = sponsor.CompanyId = company.Id;
        other.CompanyId = otherCompany.Id;
        db.Users.AddRange(driver, sponsor, other);
        await db.SaveChangesAsync();
        db.CatalogItems.AddRange(
            new CatalogItem { SponsorId = sponsor.Id, ItemId = "v1|123|0" },
            new CatalogItem { SponsorId = other.Id, ItemId = "v1|456|0" });
        await db.SaveChangesAsync();
        Assert.Equal(new[] { "v1|123|0" }, await client.GetFromJsonAsync<string[]>($"/users/{driver.Id}/catalog"));
        driver.CompanyId = null;
        await db.SaveChangesAsync();
        Assert.Empty((await client.GetFromJsonAsync<string[]>($"/users/{driver.Id}/catalog"))!);
    }

    [Fact]
    public async Task AddStoresItemIdForSponsorAndAvoidsDuplicates()
    {
        await using var app = new TestApp();
        using var client = await app.Start(builder => builder.Logging.ClearProviders());
        using var db = app.Db();
        var sponsor = NewUser("sponsor");
        var driver = NewUser("driver");
        db.Users.AddRange(sponsor, driver);
        await db.SaveChangesAsync();
        var path = $"/users/{sponsor.Id}/catalog";
        var item = new AddCatalogItem("v1|123456789012|0");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(path, item)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(path, item)).StatusCode);
        var saved = Assert.Single(await db.CatalogItems.AsNoTracking().ToListAsync());
        Assert.Equal(sponsor.Id, saved.SponsorId);
        Assert.Equal(item.ItemId, saved.ItemId);
        Assert.True(saved.Id > 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new AddCatalogItem(new string('1', 256)))).StatusCode);
        Assert.Empty(await db.CartItems.ToListAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/users/{driver.Id}/catalog", item)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new AddCatalogItem(" "))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/users/99999/catalog", item)).StatusCode);
    }

    private static User NewUser(string role) => new()
    {
        UserType = role, Username = role, Email = role + "@example.com", Password = "hash",
        PhoneNumber = "123", FirstName = "Test", LastName = "User", Address = "Road"
    };
}
