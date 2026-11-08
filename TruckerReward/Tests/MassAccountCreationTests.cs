using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class MassAccountCreationTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;

    private async Task<(int AdminId, int SponsorId, int DriverId, int CompanyId)> Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var company = new Company { Name = "Sponsor Org" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var users = new[]
        {
            User("admin", "admin"),
            User("sponsor", "sponsor"),
            User("driver", "driver")
        };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        db.AccountsCompanies.Add(new AccountsCompany
        {
            AccountId = users[1].Id,
            CompanyId = company.Id,
            CurrentCompanyViewed = true
        });
        await db.SaveChangesAsync();
        return (users[0].Id, users[1].Id, users[2].Id, company.Id);
    }

    [Fact]
    public async Task AdminAndSponsorAwardsAreRecordedWithTheirAccountRows()
    {
        var ids = await Start();
        var response = await client.PostAsJsonAsync($"/users/{ids.AdminId}/mass-accounts", new
        {
            content = "O|Acme\nD|Acme|Ava|Driver|ava.driver@example.com|500|Safe driving\nS|Northwind|Sam|Sponsor|sam.sponsor@example.com|25|Sponsor balance"
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal("Added 2 accounts and 1 organization.", (await response.Content.ReadFromJsonAsync<ApiMessage>())!.Message);
        response = await client.PostAsJsonAsync($"/users/{ids.SponsorId}/mass-accounts", new
        {
            content = "D||Max|Driver|max.driver@example.com|100|Safe week"
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal("Added 1 account.", (await response.Content.ReadFromJsonAsync<ApiMessage>())!.Message);

        using var db = app.Db();
        var created = await db.Users.Where(user => user.Email.Contains("@example.com") && user.Id > ids.DriverId)
            .OrderBy(user => user.Email).ToListAsync();
        Assert.Equal(3, created.Count);
        Assert.Equal(("driver", "ava.driver", "Ava", "Driver", "", "", 500),
            (created[0].UserType, created[0].Username, created[0].FirstName, created[0].LastName, created[0].PhoneNumber, created[0].Address, created[0].Points));
        Assert.Equal(("driver", "max.driver", "Max", "Driver", "", "", 100),
            (created[1].UserType, created[1].Username, created[1].FirstName, created[1].LastName, created[1].PhoneNumber, created[1].Address, created[1].Points));
        Assert.Equal(("sponsor", "sam.sponsor", "Sam", "Sponsor", "", "", 25),
            (created[2].UserType, created[2].Username, created[2].FirstName, created[2].LastName, created[2].PhoneNumber, created[2].Address, created[2].Points));
        var memberships = await db.AccountsCompanies.Where(membership => created.Select(user => user.Id).Contains(membership.AccountId))
            .ToListAsync();
        var maxDriver = created.Single(user => user.Username == "max.driver");
        var avaDriver = created.Single(user => user.Username == "ava.driver");
        Assert.Equal(2, memberships.Count);
        Assert.Contains(memberships, membership => membership.AccountId == maxDriver.Id && membership.CompanyId == ids.CompanyId);
        var avaMembership = Assert.Single(memberships, membership => membership.AccountId == avaDriver.Id);
        var acmeId = await db.Companies.Where(company => company.Name == "Acme").Select(company => company.Id).SingleAsync();
        Assert.Equal(acmeId, avaMembership.CompanyId);
        Assert.All(memberships, membership => Assert.True(membership.CurrentCompanyViewed));
        var awardHistory = await db.PointsHistory.Where(history => created.Select(user => user.Id).Contains(history.UserId))
            .OrderBy(history => history.UserId).ToListAsync();
        Assert.Collection(awardHistory,
            history => Assert.Equal((avaDriver.Id, acmeId, 500), (history.UserId, history.CompanyId, history.PointsDelta)),
            history => Assert.Equal((maxDriver.Id, ids.CompanyId, 100), (history.UserId, history.CompanyId, history.PointsDelta)));
        var sponsorDashboardDrivers = await client.GetFromJsonAsync<List<PointsDriver>>(
            $"/users/{ids.SponsorId}/points/drivers");
        Assert.Equal(100, Assert.Single(sponsorDashboardDrivers!, driver => driver.Id == maxDriver.Id).Points);
        var reasons = await db.AuditHistories.Where(audit => awardHistory.Select(history => history.Id).Contains(audit.PointsHistoryId))
            .OrderBy(audit => audit.UserId).Select(audit => audit.Resoning).ToListAsync();
        Assert.Equal(["Reason: Safe driving.", "Reason: Safe week."], reasons);
        foreach (var user in created)
        {
            var email = Assert.Single(app.Email.Sent, message => message.To == user.Email);
            var password = email.Body.Split('\n').Single(line => line.StartsWith("Temporary password: ", StringComparison.Ordinal))["Temporary password: ".Length..];
            Assert.Null(PasswordPolicy.Check(password));
            Assert.True(Passwords.Verify(user, password));
            var storedNotices = await db.NotificationsHistories.Where(notice => notice.UserId == user.Id)
                .Select(notice => notice.Body).ToListAsync();
            Assert.DoesNotContain(password, string.Join('\n', storedNotices));
        }
    }

    [Fact]
    public async Task InvalidBatchDoesNotCreateAccountsOrSendEmails()
    {
        var ids = await Start();
        const string content = """
            D|Acme|Ava|Driver|ava.driver@example.com|500|Safe driving
            X|Northwind|Sam|Sponsor|sam.sponsor@example.com|0|New sponsor
            """;

        var response = await client.PostAsJsonAsync($"/users/{ids.AdminId}/mass-accounts", new { content });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = (await response.Content.ReadFromJsonAsync<ApiMessage>())!.Message;
        Assert.Contains("Line 2: Type must be D or S.", message);
        using var db = app.Db();
        Assert.Equal(3, await db.Users.CountAsync());
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task OrganizationRulesDependOnUploader()
    {
        var ids = await Start();
        var response = await client.PostAsJsonAsync($"/users/{ids.AdminId}/mass-accounts", new
        {
            content = "D||Ava|Driver|ava.driver@example.com||"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Organization name is required.", (await response.Content.ReadFromJsonAsync<ApiMessage>())!.Message);

        response = await client.PostAsJsonAsync($"/users/{ids.SponsorId}/mass-accounts", new
        {
            content = "D|Acme|Ava|Driver|ava.driver@example.com||"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Leave organization blank", (await response.Content.ReadFromJsonAsync<ApiMessage>())!.Message);

        using var db = app.Db();
        Assert.Equal(3, await db.Users.CountAsync());
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task PointsAreOptionalButRequireAReasonWhenProvided()
    {
        var ids = await Start();
        var response = await client.PostAsJsonAsync($"/users/{ids.AdminId}/mass-accounts", new
        {
            content = "D|Acme|Ava|Driver|ava.driver@example.com"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var db = app.Db();
        Assert.Equal(0, (await db.Users.SingleAsync(user => user.Username == "ava.driver")).Points);

        response = await client.PostAsJsonAsync($"/users/{ids.AdminId}/mass-accounts", new
        {
            content = "D|Acme|Max|Driver|max.driver@example.com|50|"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("A reason of 1 to 500 characters is required when points are provided.",
            (await response.Content.ReadFromJsonAsync<ApiMessage>())!.Message);
        Assert.Equal(4, await db.Users.CountAsync());
        Assert.Single(app.Email.Sent);
    }

    [Fact]
    public async Task AdminCanCreateOrganizationsAndSponsorCannot()
    {
        var ids = await Start();
        var response = await client.PostAsJsonAsync($"/users/{ids.AdminId}/mass-accounts", new
        {
            content = "O|New Organization"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Added 1 organization.", (await response.Content.ReadFromJsonAsync<ApiMessage>())!.Message);
        using var db = app.Db();
        Assert.True(await db.Companies.AnyAsync(company => company.Name == "New Organization"));

        response = await client.PostAsJsonAsync($"/users/{ids.SponsorId}/mass-accounts", new
        {
            content = "O|Another Organization"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Only administrators can create organizations.", (await response.Content.ReadFromJsonAsync<ApiMessage>())!.Message);
        Assert.False(await db.Companies.AnyAsync(company => company.Name == "Another Organization"));
    }

    [Fact]
    public async Task DuplicateOrganizationRowsRejectTheWholeBatch()
    {
        var ids = await Start();
        var response = await client.PostAsJsonAsync($"/users/{ids.AdminId}/mass-accounts", new
        {
            content = "O|New Organization\nO|new organization"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("This organization name appears more than once", (await response.Content.ReadFromJsonAsync<ApiMessage>())!.Message);
        using var db = app.Db();
        Assert.False(await db.Companies.AnyAsync(company => company.Name == "New Organization"));
    }

    [Fact]
    public async Task OnlyActiveAdminOrSponsorCanUseTheEndpoint()
    {
        var ids = await Start();
        var response = await client.PostAsJsonAsync($"/users/{ids.DriverId}/mass-accounts", new
        {
            content = "D|Acme|Ava|Driver|ava.driver@example.com|0|Reason"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var db = app.Db();
        var sponsor = await db.Users.SingleAsync(user => user.Id == ids.SponsorId);
        sponsor.Status = BackEnd.Models.User.Inactive;
        await db.SaveChangesAsync();

        response = await client.PostAsJsonAsync($"/users/{ids.SponsorId}/mass-accounts", new
        {
            content = "D|Acme|Ava|Driver|ava.driver@example.com|0|Reason"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(app.Email.Sent);
    }

    private static User User(string username, string type) => new()
    {
        UserType = type,
        Username = username,
        FirstName = username,
        LastName = "Test",
        Email = $"{username}@example.com",
        PhoneNumber = "",
        Address = ""
    };

    private sealed record ApiMessage(string? Message);

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
