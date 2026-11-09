using System.Net;
using BackEnd.Models;
using Xunit;

namespace TruckerReward.Tests;

public sealed class PointsCsvTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob, amy, sue;

    private async Task Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var acme = new Company { Name = "Acme, Inc" };
        db.Add(acme);
        await db.SaveChangesAsync();
        User[] users =
        [
            User("bob", "driver", acme.Id), User("amy", "driver", acme.Id), User("sue", "sponsor", acme.Id)
        ];
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        (bob, amy, sue) = (users[0].Id, users[1].Id, users[2].Id);

        var award = Entry(bob, acme.Id, 250, 15);
        var spend = Entry(bob, acme.Id, -100, 16);
        var evil = Entry(bob, acme.Id, 5, 17);
        db.PointsHistory.AddRange(award, spend, evil, Entry(amy, acme.Id, 999, 15));
        await db.SaveChangesAsync();
        db.AuditHistories.Add(new AuditHistory { UserId = sue, PointsHistoryId = award.Id, Resoning = "Safe week" });
        db.AuditHistories.Add(new AuditHistory { UserId = sue, PointsHistoryId = evil.Id, Resoning = "=HYPERLINK(\"x\")" });
        await db.SaveChangesAsync();
    }

    private static PointsHistory Entry(int user, int company, int delta, int day) => new()
    {
        UserId = user, CompanyId = company, PointsDelta = delta, Timestamp = new DateTime(2026, 9, day, 12, 0, 0, DateTimeKind.Utc)
    };

    private static User User(string name, string type, int companyId) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x",
        AccountsCompanies = [new AccountsCompany { CompanyId = companyId }]
    };

    [Fact]
    public async Task CsvHasOnlyTheDriversOwnRowsWithABalance()
    {
        await Start();

        var response = await client.GetAsync($"/users/{bob}/points.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        var lines = (await response.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("When (UTC),Points,Reason,Sponsor,Balance", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.Equal("\"2026-09-16 12:00:00\",-100,\"\",\"Acme, Inc\",150", lines[2]);
        Assert.Equal("\"2026-09-15 12:00:00\",250,\"Safe week\",\"Acme, Inc\",250", lines[3]);
        Assert.DoesNotContain("999", string.Join("", lines));
    }

    [Fact]
    public async Task CsvDefusesFormulasInReasons()
    {
        await Start();

        var csv = await client.GetStringAsync($"/users/{bob}/points.csv");

        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv);
    }

    [Fact]
    public async Task UnknownUserIsNotFound()
    {
        await Start();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/users/9999/points.csv")).StatusCode);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
