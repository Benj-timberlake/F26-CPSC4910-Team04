using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AnnouncementEndpointTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private readonly Dictionary<string, int> ids = [];

    private async Task Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var acme = new Company { Name = "Acme" };
        var other = new Company { Name = "Other" };
        db.AddRange(acme, other);
        await db.SaveChangesAsync();
        var users = new[]
        {
            User("sue", "sponsor", acme.Id), User("zed", "sponsor", other.Id), User("lou", "sponsor"),
            User("bob", "driver", acme.Id), User("dan", "driver", other.Id), User("both", "driver", acme.Id, other.Id),
            User("gone", "driver", acme.Id), User("root", "admin")
        };
        users[6].Status = BackEnd.Models.User.Inactive;
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        foreach (var u in users)
            ids[u.Username] = u.Id;
    }

    private static User User(string name, string type, params int[] companyIds) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x",
        AccountsCompanies = companyIds.Select(c => new AccountsCompany { CompanyId = c }).ToList()
    };

    private Task<HttpResponseMessage> Post(string who, string? title = "Holiday hours", string? body = "Closed Thursday.") =>
        client.PostAsJsonAsync($"/users/{ids[who]}/announcements", new { title, body });

    private Task<List<AnnouncementEntry>?> List(string who) =>
        client.GetFromJsonAsync<List<AnnouncementEntry>>($"/users/{ids[who]}/announcements");

    [Fact]
    public async Task SponsorPostsAndDriversOfThatCompanyReadIt()
    {
        await Start();

        var response = await Post("sue");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var seen = Assert.Single((await List("bob"))!);
        Assert.Equal(("Holiday hours", "Closed Thursday.", "Acme", "sue Test"), (seen.Title, seen.Body, seen.Company, seen.Author));
        Assert.Equal(app.Clock.GetUtcNow().UtcDateTime, seen.CreatedAt);
        Assert.Single((await List("sue"))!);
        Assert.Single((await List("both"))!);
        Assert.Empty((await List("dan"))!);
        Assert.Empty((await List("zed"))!);
    }

    [Fact]
    public async Task ListsNewestFirst()
    {
        await Start();
        await Post("sue", "first");
        app.Clock.Advance(TimeSpan.FromHours(1));
        await Post("sue", "second");
        await Post("zed", "elsewhere");

        Assert.Equal(["second", "first"], (await List("bob"))!.Select(a => a.Title));
        Assert.Equal(["elsewhere", "second", "first"], (await List("both"))!.Select(a => a.Title));
    }

    [Fact]
    public async Task ActiveDriversOfTheCompanyAreNotified()
    {
        await Start();

        await Post("sue");

        using var db = app.Db();
        var sent = await db.NotificationsHistories.ToListAsync();
        Assert.Equal(["bob", "both"], sent.Select(n => ids.First(p => p.Value == n.UserId).Key).Order());
        Assert.All(sent, n => Assert.Equal("Acme: Holiday hours", n.Subject));
        Assert.Contains("Closed Thursday.", sent[0].Body);
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task DriversCanSwitchTheNotificationOff()
    {
        await Start();
        await client.PutAsJsonAsync($"/users/{ids["bob"]}/notification-settings", new[] { new { category = "announcements", enabled = false, emailed = false } });

        await Post("sue");

        using var db = app.Db();
        Assert.Equal([ids["both"]], await db.NotificationsHistories.Select(n => n.UserId).ToListAsync());
        Assert.Single((await List("bob"))!);
    }

    [Theory]
    [InlineData("bob")]
    [InlineData("root")]
    [InlineData("lou")]
    public async Task OnlySponsorsWithACompanyCanPost(string who)
    {
        await Start();

        Assert.Equal(HttpStatusCode.Forbidden, (await Post(who)).StatusCode);
        Assert.Empty(await app.Db().Announcements.ToListAsync());
    }

    [Theory]
    [InlineData(null, "body")]
    [InlineData("  ", "body")]
    [InlineData("title", "")]
    [InlineData("title", null)]
    public async Task EmptyFieldsAreRefused(string? title, string? body)
    {
        await Start();

        Assert.Equal(HttpStatusCode.BadRequest, (await Post("sue", title, body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("sue", new string('t', 101))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("sue", "title", new string('b', 2001))).StatusCode);
        Assert.Empty(await app.Db().Announcements.ToListAsync());
    }

    [Fact]
    public async Task AdminsAndUnknownUsersCantList()
    {
        await Start();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/users/{ids["root"]}/announcements")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/users/9999/announcements")).StatusCode);
    }

    [Fact]
    public async Task DeletingTheAuthorKeepsTheAnnouncement()
    {
        await Start();
        await Post("sue");
        using var db = app.Db();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
        await db.AccountsCompanies.Where(m => m.AccountId == ids["sue"]).ExecuteDeleteAsync();
        await db.Users.Where(u => u.Id == ids["sue"]).ExecuteDeleteAsync();

        var seen = Assert.Single((await List("bob"))!);
        Assert.Null(seen.Author);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
