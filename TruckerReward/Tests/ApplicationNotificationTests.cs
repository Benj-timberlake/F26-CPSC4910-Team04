using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ApplicationNotificationTests : IAsyncDisposable
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
        var driver = User("bob", "driver", null);
        var sponsor = User("sue", "sponsor", company.Id);
        db.Users.AddRange(driver, sponsor);
        await db.SaveChangesAsync();
        (bob, sue, acme) = (driver.Id, sponsor.Id, company.Id);
    }

    private static User User(string name, string type, int? companyId) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x", CompanyId = companyId
    };

    private async Task<int> Apply()
    {
        var response = await client.PostAsJsonAsync($"/users/{bob}/applications", new { companyId = acme, applicantExtraInfo = "nights" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await app.Db().Applications.SingleAsync()).Id;
    }

    private Task<HttpResponseMessage> Review(int id, string decision, string reasoning) =>
        client.PostAsJsonAsync($"/users/{sue}/applications/{id}/review", new { decision, reasoning });

    [Fact]
    public async Task ApplyingConfirmsItWasReceived()
    {
        await Start();
        await Apply();

        var mail = Assert.Single(app.Email.Sent);
        Assert.Equal("bob@example.com", mail.To);
        Assert.Equal("Your application to Acme was received", mail.Subject);
        Assert.True(await app.Db().NotificationsHistories.AnyAsync(n => n.UserId == bob && n.Subject == mail.Subject));
    }

    [Fact]
    public async Task ApprovalTellsTheDriver()
    {
        await Start();
        var id = await Apply();
        app.Email.Sent.Clear();

        Assert.Equal(HttpStatusCode.OK, (await Review(id, "approved", "Welcome aboard.")).StatusCode);

        var mail = Assert.Single(app.Email.Sent);
        Assert.Equal("You've been accepted by Acme", mail.Subject);
        Assert.Contains("Reason: Welcome aboard.", mail.Body);
    }

    [Fact]
    public async Task RejectionTellsTheDriverWhy()
    {
        await Start();
        var id = await Apply();
        app.Email.Sent.Clear();

        await Review(id, "rejected", "No openings.");

        var mail = Assert.Single(app.Email.Sent);
        Assert.Equal("Your application to Acme was rejected", mail.Subject);
        Assert.Contains("Reason: No openings.", mail.Body);
    }

    [Fact]
    public async Task UndoingAnApprovalTellsTheDriverTheyWereDropped()
    {
        await Start();
        var id = await Apply();
        await Review(id, "approved", "Welcome aboard.");
        app.Email.Sent.Clear();

        Assert.Equal(HttpStatusCode.OK, (await Review(id, "active", "Left the route.")).StatusCode);

        var mail = Assert.Single(app.Email.Sent);
        Assert.Equal("You've been removed from Acme", mail.Subject);
        Assert.Contains("Reason: Left the route.", mail.Body);
    }

    [Fact]
    public async Task RefusedReviewsSendNothing()
    {
        await Start();
        var id = await Apply();
        app.Email.Sent.Clear();

        Assert.Equal(HttpStatusCode.BadRequest, (await Review(id, "approved", "")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Review(id, "active", "again")).StatusCode);
        Assert.Empty(app.Email.Sent);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
