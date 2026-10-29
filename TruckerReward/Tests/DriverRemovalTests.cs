using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class DriverRemovalTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private readonly Dictionary<string, int> ids = [];
    private int acme;

    private async Task Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var a = new Company { Name = "Acme" };
        var o = new Company { Name = "Other" };
        db.AddRange(a, o);
        await db.SaveChangesAsync();
        var users = new[] { User("bob", "driver", a.Id), User("sue", "sponsor", a.Id), User("zed", "sponsor", o.Id), User("ann", "admin", null) };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        foreach (var u in users)
            ids[u.Username] = u.Id;
        acme = a.Id;
        db.Applications.Add(new Application { ApplicantId = ids["bob"], CompanyId = a.Id, Status = "approved", ApplicationTimestamp = new DateTime(2026, 9, 1) });
        await db.SaveChangesAsync();
    }

    private static User User(string name, string type, int? companyId) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x",
        AccountsCompanies = companyId is int company ? [new AccountsCompany { CompanyId = company }] : []
    };

    private Task<HttpResponseMessage> Remove(string actor, string reason = "Left the company") =>
        client.PostAsJsonAsync($"/users/{ids[actor]}/points/drivers/{ids["bob"]}/remove", new { reason });

    [Theory]
    [InlineData("sue")]
    [InlineData("ann")]
    public async Task RemovedDriverLosesTheSponsorAndIsTold(string actor)
    {
        await Start();

        Assert.Equal(HttpStatusCode.NoContent, (await Remove(actor)).StatusCode);

        using var db = app.Db();
        Assert.False(await db.AccountsCompanies.AnyAsync(membership => membership.AccountId == ids["bob"]));
        var application = await db.Applications.SingleAsync();
        Assert.Equal(("canceled", (int?)ids[actor], "Left the company"), (application.Status, application.ReviewerId, application.ReviewerReasoning));
        var notice = await db.NotificationsHistories.SingleAsync(n => n.UserId == ids["bob"]);
        Assert.Equal("You've been removed from Acme", notice.Subject);
        Assert.Contains("Reason: Left the company", notice.Body);
    }

    [Theory]
    [InlineData("zed", "Left", 403)]
    [InlineData("bob", "Left", 403)]
    [InlineData("sue", "", 400)]
    public async Task OtherRemovalsAreRefused(string actor, string reason, int status)
    {
        await Start();

        Assert.Equal(status, (int)(await Remove(actor, reason)).StatusCode);
        Assert.Equal(acme, await CompanyMembershipQueries.FirstCompanyId(app.Db(), ids["bob"]));
    }

    [Fact]
    public async Task DriverWithoutASponsorCantBeRemovedAgain()
    {
        await Start();
        await Remove("sue");

        Assert.Equal(HttpStatusCode.NotFound, (await Remove("sue")).StatusCode);
    }

    [Fact]
    public async Task RemovedDriverCanApplyAgain()
    {
        await Start();
        await Remove("sue");

        var response = await client.PostAsJsonAsync($"/users/{ids["bob"]}/applications", new { companyId = acme });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
