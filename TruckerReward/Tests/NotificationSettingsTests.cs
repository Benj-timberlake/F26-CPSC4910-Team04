using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class NotificationSettingsTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob, sue, acme;

    private async Task Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var company = new Company { Name = "Acme" };
        db.Add(company);
        await db.SaveChangesAsync();
        var driver = User("bob", "driver", company.Id, 100);
        var sponsor = User("sue", "sponsor", company.Id);
        db.Users.AddRange(driver, sponsor);
        await db.SaveChangesAsync();
        (bob, sue, acme) = (driver.Id, sponsor.Id, company.Id);
    }

    private static User User(string name, string type, int? companyId, int points = 0) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x", CompanyId = companyId, Points = points
    };

    private Task<HttpResponseMessage> Save(int id, params object[] updates) =>
        client.PutAsJsonAsync($"/users/{id}/notification-settings", updates);

    private Task<HttpResponseMessage> Award(int points) =>
        client.PostAsJsonAsync($"/users/{sue}/points/drivers/{bob}", new { points, reason = "test" });

    [Fact]
    public void EveryCategoryBelongsToARole()
    {
        Assert.All(NotificationCategory.All, c => Assert.NotEmpty(c.Roles));
        Assert.Equal(NotificationCategory.All.Count, NotificationCategory.All.Select(c => c.Key).Distinct().Count());
    }

    [Fact]
    public async Task DefaultsDependOnTheRole()
    {
        await Start();

        var driver = await client.GetFromJsonAsync<List<NotificationSetting>>($"/users/{bob}/notification-settings");
        var sponsor = await client.GetFromJsonAsync<List<NotificationSetting>>($"/users/{sue}/notification-settings");

        Assert.Equal(["points", "applications"], driver!.Select(s => s.Category));
        Assert.All(driver, s => Assert.True(s.Enabled));
        Assert.False(driver.Single(s => s.Category == "points").Emailed);
        Assert.Equal(["applications", "purchases", "negative-balances"], sponsor!.Select(s => s.Category));
        Assert.True(sponsor.Single(s => s.Category == "negative-balances").Emailed);
    }

    [Fact]
    public async Task SavedChoicesComeBack()
    {
        await Start();

        Assert.Equal(HttpStatusCode.NoContent, (await Save(bob, new { category = "points", enabled = true, emailed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Save(bob, new { category = "points", enabled = false, emailed = true })).StatusCode);

        var points = (await client.GetFromJsonAsync<List<NotificationSetting>>($"/users/{bob}/notification-settings"))!.Single(s => s.Category == "points");
        Assert.False(points.Enabled);
        Assert.True(points.Emailed);
        Assert.Single(await app.Db().NotificationPreferences.ToListAsync());
    }

    [Fact]
    public async Task CategoriesForOtherRolesAreRefused()
    {
        await Start();
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(bob, new { category = "purchases", enabled = false, emailed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(bob, new { category = "nope", enabled = false, emailed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/users/999/notification-settings")).StatusCode);
        Assert.Empty(await app.Db().NotificationPreferences.ToListAsync());
    }

    [Fact]
    public async Task TurnedOffCategoriesAreNotSent()
    {
        await Start();
        await Save(bob, new { category = "points", enabled = false, emailed = false });

        await Award(10);

        Assert.Empty(await app.Db().NotificationsHistories.ToListAsync());
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task ChoosingEmailAddsAnEmail()
    {
        await Start();
        await Save(bob, new { category = "points", enabled = true, emailed = true });

        await Award(10);

        var mail = Assert.Single(app.Email.Sent);
        Assert.Equal("bob@example.com", mail.To);
        Assert.Equal("10 points added", mail.Subject);
    }

    [Fact]
    public async Task SponsorsCanStopNegativeBalanceEmails()
    {
        await Start();
        await Save(sue, new { category = "negative-balances", enabled = true, emailed = false });

        await Award(-200);

        Assert.Empty(app.Email.Sent);
        Assert.True(await app.Db().NotificationsHistories.AnyAsync(n => n.UserId == sue && n.Subject == "bob is negative on points"));
    }

    [Fact]
    public async Task PasswordNoticesIgnoreSettings()
    {
        await Start();
        using (var db = app.Db())
        {
            db.NotificationPreferences.AddRange(
                new NotificationPreference { UserId = bob, Category = "points", Enabled = false, Emailed = false },
                new NotificationPreference { UserId = bob, Category = "applications", Enabled = false, Emailed = false });
            await db.SaveChangesAsync();
        }

        await client.PostAsJsonAsync("/auth/forgot", new { email = "bob@example.com" });

        Assert.Contains(app.Email.Sent, m => m.To == "bob@example.com" && m.Body.Contains("reset-password?token="));
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
