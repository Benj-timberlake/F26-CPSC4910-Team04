using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ViewAsTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private readonly Dictionary<string, int> ids = [];

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
            User("bob", "driver", a.Id), User("tom", "driver", o.Id), User("sue", "sponsor", a.Id), User("zed", "sponsor", null),
            User("ann", "admin", null), User("abe", "admin", null), User("old", "driver", a.Id, BackEnd.Models.User.Inactive)
        };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        foreach (var u in users)
            ids[u.Username] = u.Id;
    }

    private static User User(string name, string type, int? companyId, string status = "active") => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x", CompanyId = companyId, Status = status
    };

    private Task<HttpResponseMessage> View(string viewer, string user) =>
        client.PostAsJsonAsync($"/users/{ids[viewer]}/view-as/{ids[user]}", new { ipAddress = "1.2.3.4" });

    [Theory]
    [InlineData("ann", "bob")]
    [InlineData("ann", "sue")]
    [InlineData("sue", "bob")]
    public async Task AllowedViewsAreRecordedAndTheUserIsTold(string viewer, string user)
    {
        await Start();

        var response = await View(viewer, user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(user, (await response.Content.ReadFromJsonAsync<UserProfile>())!.Username);
        using var db = app.Db();
        var row = await db.Impersonations.SingleAsync();
        Assert.Equal((ids[viewer], ids[user], "1.2.3.4"), (row.ViewerId, row.UserId, row.IpAddress));
        var notice = await db.NotificationsHistories.SingleAsync(n => n.UserId == ids[user]);
        Assert.Contains($"({viewer})", notice.Body);
        Assert.Empty(app.Email.Sent);
    }

    [Theory]
    [InlineData("ann", "abe", 403)]
    [InlineData("ann", "ann", 403)]
    [InlineData("sue", "tom", 403)]
    [InlineData("sue", "ann", 403)]
    [InlineData("zed", "bob", 403)]
    [InlineData("bob", "sue", 403)]
    [InlineData("ann", "old", 404)]
    public async Task OtherViewsAreRefused(string viewer, string user, int status)
    {
        await Start();

        Assert.Equal(status, (int)(await View(viewer, user)).StatusCode);
        Assert.Empty(await app.Db().Impersonations.ToListAsync());
    }

    [Fact]
    public async Task ViewsShowInTheAuditLog()
    {
        await Start();
        await View("sue", "bob");

        var rows = await client.GetFromJsonAsync<List<AuditEntry>>($"/users/{ids["ann"]}/audit?category=views");

        var row = Assert.Single(rows!);
        Assert.Equal(("bob", "sue", "Site viewed as this account from 1.2.3.4"), (row.Username, row.By, row.Detail));
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
