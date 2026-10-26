using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class SponsorTeamTests : IAsyncDisposable
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
        var users = new[] { User("sue", "sponsor", a.Id), User("sam", "sponsor", a.Id), User("zed", "sponsor", o.Id), User("lou", "sponsor", null), User("bob", "driver", a.Id) };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        foreach (var u in users)
            ids[u.Username] = u.Id;
        acme = a.Id;
    }

    private static User User(string name, string type, int? companyId) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x",
        AccountsCompanies = companyId is int company ? [new AccountsCompany { CompanyId = company }] : []
    };

    private Task<HttpResponseMessage> Invite(string actor, string username = "kim", string email = "kim@example.com") =>
        client.PostAsJsonAsync($"/users/{ids[actor]}/sponsor-team", new { username, firstName = "Kim", lastName = "Lee", email });

    [Fact]
    public async Task SponsorsSeeTheirOwnTeam()
    {
        await Start();

        var team = await client.GetFromJsonAsync<List<TeamMember>>($"/users/{ids["sue"]}/sponsor-team");

        Assert.Equal(["sam", "sue"], team!.Select(m => m.Username).Order());
    }

    [Fact]
    public async Task InvitedSponsorJoinsTheCompanyAndGetsALink()
    {
        await Start();

        Assert.Equal(HttpStatusCode.Created, (await Invite("sue")).StatusCode);

        using var db = app.Db();
        var kim = await db.Users.SingleAsync(u => u.Username == "kim");
        var kimCompany = await db.AccountsCompanies.Where(membership => membership.AccountId == kim.Id)
            .Select(membership => (int?)membership.CompanyId).FirstOrDefaultAsync();
        Assert.Equal(("sponsor", (int?)acme, (string?)null), (kim.UserType, kimCompany, kim.Password));
        var mail = Assert.Single(app.Email.Sent, m => m.To == "kim@example.com");
        Assert.Contains("sue Test at Acme made you a TruckerReward sponsor account", mail.Body);
        Assert.Contains("reset-password?token=", mail.Body);
    }

    [Theory]
    [InlineData("bob", "kim", "kim@example.com", 403)]
    [InlineData("lou", "kim", "kim@example.com", 403)]
    [InlineData("sue", "sam", "kim@example.com", 409)]
    [InlineData("sue", "kim", "nope", 400)]
    public async Task OtherInvitesAreRefused(string actor, string username, string email, int status)
    {
        await Start();

        Assert.Equal(status, (int)(await Invite(actor, username, email)).StatusCode);
        Assert.Empty(app.Email.Sent);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
