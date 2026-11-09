using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AchievementTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob, tom, sue, zed, ann, acme, other;

    private async Task Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var a = new Company { Name = "Acme" };
        var o = new Company { Name = "Other" };
        db.AddRange(a, o);
        await db.SaveChangesAsync();
        var users = new[]
        {
            User("bob", "driver", a.Id),
            User("tom", "driver", o.Id),
            User("sue", "sponsor", a.Id),
            User("zed", "sponsor", o.Id),
            User("ann", "admin", null)
        };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        (bob, tom, sue, zed, ann, acme, other) = (users[0].Id, users[1].Id, users[2].Id, users[3].Id, users[4].Id, a.Id, o.Id);
    }

    private static User User(string name, string type, int? companyId) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x",
        AccountsCompanies = companyId is int company ? [new AccountsCompany { CompanyId = company }] : []
    };

    private async Task Points(int driver, int company, int delta, bool order = false)
    {
        using var db = app.Db();
        var history = new PointsHistory { UserId = driver, CompanyId = company, PointsDelta = delta, Timestamp = app.Clock.GetUtcNow().UtcDateTime };
        db.PointsHistory.Add(history);
        if (order)
            db.CartItems.Add(new CartItem { UserId = driver, Name = "Mug", Price = -delta / 100m, InCart = false, PointsHistory = history });
        await db.SaveChangesAsync();
    }

    private async Task<int> Goal(int company, string metric, int threshold, string name = "Acme regular", bool active = true)
    {
        using var db = app.Db();
        var goal = new Achievement { SponsorCompanyId = company, Name = name, Description = "Sponsor goal.", Metric = metric, Threshold = threshold, Active = active };
        db.Achievements.Add(goal);
        await db.SaveChangesAsync();
        return goal.Id;
    }

    private async Task<List<Badge>> Badges(int user) =>
        (await client.GetFromJsonAsync<List<Badge>>($"/users/{user}/achievements"))!;

    private async Task<List<GoalProgress>> Progress(int user) =>
        (await client.GetFromJsonAsync<List<GoalProgress>>($"/users/{user}/achievements/progress"))!;

    [Fact]
    public async Task ProgressListsUnearnedGoalsClosestFirst()
    {
        await Start();
        await Goal(acme, Achievement.OrdersPlaced, 2, "Acme orders");
        await Points(bob, acme, 950);
        await Points(bob, acme, -100, order: true);

        var progress = await Progress(bob);

        Assert.DoesNotContain(progress, p => p.Name is "First points" or "First order");
        Assert.Equal(["1,000 points", "Acme orders", "Regular shopper"], progress.Take(3).Select(p => p.Name));
        Assert.Equal((950L, 1_000), (progress[0].Current, progress[0].Threshold));
        Assert.Null(progress[0].Company);
        Assert.Equal("Acme", progress[1].Company);
        Assert.Equal(Achievement.BuiltIn.Length + 1 - 2, progress.Count);
        Assert.Equal(2, await app.Db().EarnedBadges.CountAsync(b => b.UserId == bob));
    }

    [Fact]
    public async Task ProgressLeavesOutOtherSponsorsAndSwitchedOffGoals()
    {
        await Start();
        await Goal(other, Achievement.PointsEarned, 10, "Other goal");
        await Goal(acme, Achievement.PointsEarned, 10, "Off", active: false);

        Assert.DoesNotContain(await Progress(bob), p => p.Name is "Other goal" or "Off");
        Assert.Contains(await Progress(tom), p => p.Name == "Other goal" && p.Current == 0 && p.Threshold == 10);
    }

    [Fact]
    public async Task OnlyDriversHaveProgress()
    {
        await Start();

        Assert.Empty(await Progress(sue));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/users/9999/achievements/progress")).StatusCode);
    }

    [Fact]
    public async Task BuiltInGoalsAreSeeded()
    {
        await Start();

        var goals = await app.Db().Achievements.ToListAsync();

        Assert.Equal(Achievement.BuiltIn.Length, goals.Count);
        Assert.All(goals, g => Assert.Null(g.SponsorCompanyId));
        Assert.Contains(goals, g => g.Name == "First points" && g.Metric == Achievement.PointsEarned && g.Threshold == 1);
    }

    [Fact]
    public async Task FirstPointsEarnABadgeAndANotification()
    {
        await Start();
        Assert.Empty(await Badges(bob));

        var response = await client.PostAsJsonAsync($"/users/{sue}/points/drivers/{bob}", new { points = 250, reason = "Safe week" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var badges = await Badges(bob);

        var badge = Assert.Single(badges);
        Assert.Equal("First points", badge.Name);
        Assert.Null(badge.Company);
        Assert.Equal(app.Clock.GetUtcNow().UtcDateTime, badge.EarnedAt);
        var note = Assert.Single(await app.Db().NotificationsHistories.Where(n => n.UserId == bob && n.Subject.StartsWith("Badge earned")).ToListAsync());
        Assert.Equal("Badge earned: First points", note.Subject);
    }

    [Fact]
    public async Task ReadingAgainDoesNotAwardTwice()
    {
        await Start();
        await Points(bob, acme, 1_200);

        var first = await Badges(bob);
        app.Clock.Advance(TimeSpan.FromHours(1));
        var second = await Badges(bob);

        Assert.Equal(["1,000 points", "First points"], first.Select(b => b.Name).Order());
        Assert.Equal(first.Select(b => (b.Id, b.EarnedAt)).Order(), second.Select(b => (b.Id, b.EarnedAt)).Order());
        Assert.Equal(2, await app.Db().EarnedBadges.CountAsync(b => b.UserId == bob));
        Assert.Equal(2, await app.Db().NotificationsHistories.CountAsync(n => n.UserId == bob));
    }

    [Fact]
    public async Task OrdersAndSpendingCount()
    {
        await Start();
        await Points(bob, acme, 6_000);
        for (var i = 0; i < 5; i++)
            await Points(bob, acme, -1_000, order: true);

        var names = (await Badges(bob)).Select(b => b.Name).ToList();

        Assert.Contains("First order", names);
        Assert.Contains("Regular shopper", names);
        Assert.Contains("Big spender", names);
        Assert.Contains("5,000 points", names);
        Assert.DoesNotContain("10,000 points", names);
    }

    [Fact]
    public async Task DeductionsDoNotCountAsOrders()
    {
        await Start();
        await Points(bob, acme, 100);
        await Points(bob, acme, -50);

        Assert.DoesNotContain(await Badges(bob), b => b.Name == "First order");
    }

    [Fact]
    public async Task SponsorGoalsOnlyCountThatSponsorsPointsAndDrivers()
    {
        await Start();
        await Goal(acme, Achievement.PointsEarned, 500, "Acme 500");
        await Points(bob, other, 1_000);
        await Points(tom, other, 1_000);

        Assert.DoesNotContain(await Badges(bob), b => b.Name == "Acme 500");
        Assert.DoesNotContain(await Badges(tom), b => b.Name == "Acme 500");

        await Points(bob, acme, 500);
        var badge = Assert.Single(await Badges(bob), b => b.Name == "Acme 500");
        Assert.Equal("Acme", badge.Company);
    }

    [Fact]
    public async Task InactiveGoalsAreNotAwarded()
    {
        await Start();
        await Goal(acme, Achievement.PointsEarned, 1, "Switched off", active: false);
        await Points(bob, acme, 10);

        Assert.DoesNotContain(await Badges(bob), b => b.Name == "Switched off");
    }

    [Fact]
    public async Task OnlyDriversEarnBadges()
    {
        await Start();
        await Points(sue, acme, 10);

        Assert.Empty(await Badges(sue));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/users/9999/achievements")).StatusCode);
    }

    [Fact]
    public async Task DeletingADriverDeletesTheirBadges()
    {
        await Start();
        await Points(bob, acme, 10);
        await Badges(bob);
        using var db = app.Db();
        await db.PointsHistory.Where(h => h.UserId == bob).ExecuteDeleteAsync();
        await db.NotificationsHistories.Where(n => n.UserId == bob).ExecuteDeleteAsync();
        await db.AccountsCompanies.Where(m => m.AccountId == bob).ExecuteDeleteAsync();

        await db.Users.Where(u => u.Id == bob).ExecuteDeleteAsync();

        Assert.Equal(0, await db.EarnedBadges.CountAsync());
    }

    [Fact]
    public void TablesMatchTheMigration()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL("Server=localhost;Database=model_validation;User Id=unused;Password=unused")
            .Options);
        var sql = File.ReadAllText(Path.Combine(Migration004Tests.RepoRoot(), "db", "migrations", "018_achievements.sql"));
        var schema = File.ReadAllText(Path.Combine(Migration004Tests.RepoRoot(), "db", "schema.sql"));

        foreach (var table in new[] { db.Model.FindEntityType(typeof(Achievement))!, db.Model.FindEntityType(typeof(EarnedBadge))! })
        {
            Assert.Contains($"CREATE TABLE IF NOT EXISTS {table.GetTableName()} (", sql);
            Assert.Contains($"CREATE TABLE IF NOT EXISTS {table.GetTableName()} (", schema);
            foreach (var column in table.GetProperties().Select(p => p.GetColumnName()))
                Assert.Contains($"    {column} ", sql);
            foreach (var fk in table.GetForeignKeys())
                Assert.Contains($"CONSTRAINT {fk.GetConstraintName()} ", sql);
        }
        Assert.Contains("REFERENCES users (id) ON DELETE SET NULL", sql);
        Assert.Contains("fk_earned_badges_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE", sql);
        foreach (var goal in Achievement.BuiltIn)
            Assert.Contains($"({goal.Id}, NULL, '{goal.Name}', '{goal.Description}', '{goal.Metric}', {goal.Threshold}, NULL, 1)", sql);
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}
