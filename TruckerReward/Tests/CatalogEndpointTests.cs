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
        db.Users.AddRange(driver, sponsor, other);
        await db.SaveChangesAsync();
        db.AccountsCompanies.AddRange(
            new AccountsCompany { AccountId = driver.Id, CompanyId = company.Id },
            new AccountsCompany { AccountId = sponsor.Id, CompanyId = company.Id },
            new AccountsCompany { AccountId = other.Id, CompanyId = otherCompany.Id });
        await db.SaveChangesAsync();
        db.CatalogItems.AddRange(
            new CatalogItem { SponsorId = sponsor.Id, ItemId = "v1|123|0" },
            new CatalogItem { SponsorId = other.Id, ItemId = "v1|456|0" });
        await db.SaveChangesAsync();
        Assert.Equal(new[] { "v1|123|0" }, await client.GetFromJsonAsync<string[]>($"/users/{driver.Id}/catalog"));
        Assert.Equal(new[] { "v1|123|0" }, await client.GetFromJsonAsync<string[]>($"/users/{sponsor.Id}/catalog"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/users/{sponsor.Id}/catalog?itemId=v1%7C456%7C0")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/users/{driver.Id}/catalog?itemId=v1%7C123%7C0")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/users/{sponsor.Id}/catalog?itemId=v1%7C123%7C0")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<string[]>($"/users/{driver.Id}/catalog"))!);
        Assert.Equal("v1|456|0", Assert.Single(await db.CatalogItems.AsNoTracking().ToListAsync()).ItemId);
        await db.AccountsCompanies.Where(membership => membership.AccountId == driver.Id).ExecuteDeleteAsync();
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

    [Fact]
    public async Task WishlistsArePersonalAndLimitedToTheDriversCatalog()
    {
        await using var app = new TestApp();
        using var client = await app.Start();
        using var db = app.Db();
        var company = new Company { Name = "Acme" };
        db.Add(company);
        await db.SaveChangesAsync();
        var driver = NewUser("driver");
        var sponsor = NewUser("sponsor");
        var other = NewUser("driver");
        other.Username = "other";
        other.Email = "other@example.com";
        driver.AccountsCompanies.Add(new AccountsCompany { CompanyId = company.Id });
        sponsor.AccountsCompanies.Add(new AccountsCompany { CompanyId = company.Id });
        other.AccountsCompanies.Add(new AccountsCompany { CompanyId = company.Id });
        db.Users.AddRange(driver, sponsor, other);
        await db.SaveChangesAsync();
        db.CatalogItems.Add(new CatalogItem { SponsorId = sponsor.Id, ItemId = "gps" });
        await db.SaveChangesAsync();
        var path = $"/users/{driver.Id}/wishlist";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(path, new UpdateWishlist("gps", true))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(path, new UpdateWishlist("gps", true))).StatusCode);
        Assert.Single(await db.WishlistItems.ToListAsync());
        Assert.Equal(new[] { "gps" }, await client.GetFromJsonAsync<string[]>(path));
        Assert.Empty((await client.GetFromJsonAsync<string[]>($"/users/{other.Id}/wishlist"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(path, new UpdateWishlist("outside-catalog", true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/users/{sponsor.Id}/wishlist", new UpdateWishlist("gps", true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/users/{sponsor.Id}/wishlist")).StatusCode);
        db.AccountsCompanies.RemoveRange(driver.AccountsCompanies);
        await db.SaveChangesAsync();
        Assert.Empty((await client.GetFromJsonAsync<string[]>(path))!);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(path, new UpdateWishlist("gps", false))).StatusCode);
        Assert.Empty(await db.WishlistItems.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-long")]
    public async Task InvalidWishlistProductIdsDoNotCreateFavorites(string itemId)
    {
        await using var app = new TestApp();
        using var client = await app.Start();
        using var db = app.Db();
        var driver = NewUser("driver");
        db.Users.Add(driver);
        await db.SaveChangesAsync();
        if (itemId == "too-long") itemId = new string('x', 256);
        var response = await client.PutAsJsonAsync($"/users/{driver.Id}/wishlist", new UpdateWishlist(itemId, true));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await db.WishlistItems.ToListAsync());
    }

    [Fact]
    public async Task MissingUsersCannotReadOrUpdateAWishlist()
    {
        await using var app = new TestApp();
        using var client = await app.Start();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/users/999/wishlist")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/users/999/wishlist", new UpdateWishlist("gps", true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/users/999/wishlist", new UpdateWishlist("gps", false))).StatusCode);
    }

    private static User NewUser(string role) => new()
    {
        UserType = role, Username = role, Email = role + "@example.com", Password = "hash",
        PhoneNumber = "123", FirstName = "Test", LastName = "User", Address = "Road"
    };
}
