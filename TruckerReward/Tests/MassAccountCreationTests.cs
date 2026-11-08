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

    private async Task<(int AdminId, int SponsorId, int DriverId)> Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var users = new[]
        {
            User("admin", "admin"),
            User("sponsor", "sponsor"),
            User("driver", "driver")
        };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        return (users[0].Id, users[1].Id, users[2].Id);
    }

    [Fact]
    public async Task AdminAndSponsorCreateAccountsFromRowsWithoutApplyingOrganizationOrPoints()
    {
        var ids = await Start();
        var response = await client.PostAsJsonAsync($"/users/{ids.AdminId}/mass-accounts", new
        {
            content = "D|Acme|Ava|Driver|ava.driver@example.com|500|Safe driving\nS|Northwind|Sam|Sponsor|sam.sponsor@example.com|0|New sponsor"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Added 2 accounts.", (await response.Content.ReadFromJsonAsync<ApiMessage>())!.Message);
        response = await client.PostAsJsonAsync($"/users/{ids.SponsorId}/mass-accounts", new
        {
            content = "D|Acme|Max|Driver|max.driver@example.com|100|Safe week"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Added 1 account.", (await response.Content.ReadFromJsonAsync<ApiMessage>())!.Message);

        using var db = app.Db();
        var created = await db.Users.Where(user => user.Email.Contains("@example.com") && user.Id > ids.DriverId)
            .OrderBy(user => user.Email).ToListAsync();
        Assert.Equal(3, created.Count);
        Assert.Equal(("driver", "ava.driver", "Ava", "Driver", "", "", 0),
            (created[0].UserType, created[0].Username, created[0].FirstName, created[0].LastName, created[0].PhoneNumber, created[0].Address, created[0].Points));
        Assert.Equal(("driver", "max.driver", "Max", "Driver", "", "", 0),
            (created[1].UserType, created[1].Username, created[1].FirstName, created[1].LastName, created[1].PhoneNumber, created[1].Address, created[1].Points));
        Assert.Equal(("sponsor", "sam.sponsor", "Sam", "Sponsor", "", "", 0),
            (created[2].UserType, created[2].Username, created[2].FirstName, created[2].LastName, created[2].PhoneNumber, created[2].Address, created[2].Points));
        Assert.Empty(await db.AccountsCompanies.Where(membership => created.Select(user => user.Id).Contains(membership.AccountId)).ToListAsync());
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
