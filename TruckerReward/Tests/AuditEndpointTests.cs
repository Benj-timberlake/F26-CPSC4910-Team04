using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AuditEndpointTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob, tom, sue, zed, ann, acme;
    private readonly DateTime day = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

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
            User("bob", "driver", a.Id), User("tom", "driver", o.Id), User("sue", "sponsor", a.Id),
            User("zed", "sponsor", null), User("ann", "admin", null)
        };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        (bob, tom, sue, zed, ann, acme) = (users[0].Id, users[1].Id, users[2].Id, users[3].Id, users[4].Id, a.Id);

        db.LoginAttempts.AddRange(
            new LoginAttempt { Username = "bob", UserId = bob, Succeeded = true, IpAddress = "1.2.3.4", AttemptedAt = day },
            new LoginAttempt { Username = "tom", UserId = tom, Succeeded = false, IpAddress = "", AttemptedAt = day.AddDays(1) },
            new LoginAttempt { Username = "ghost", UserId = null, Succeeded = false, IpAddress = "", AttemptedAt = day.AddDays(2) },
            new LoginAttempt { Username = "sue", UserId = sue, Succeeded = true, IpAddress = "", AttemptedAt = day.AddDays(3) });
        db.PasswordChanges.Add(new PasswordChange { UserId = bob, ChangeType = PasswordChange.ResetRequested, IpAddress = "5.6.7.8", ChangedAt = day.AddDays(4) });
        var award = new PointsHistory { UserId = bob, PointsDelta = 250, Timestamp = day.AddDays(5) };
        db.PointsHistory.Add(award);
        db.AuditHistories.Add(new AuditHistory { UserId = sue, PointsHistory = award, Resoning = "Safe week" });
        db.PointsHistory.Add(new PointsHistory { UserId = tom, PointsDelta = -40, Timestamp = day.AddDays(6) });
        await db.SaveChangesAsync();
    }

    private static User User(string name, string type, int? companyId) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x", CompanyId = companyId
    };

    private async Task<List<AuditEntry>> Audit(int actor, string query = "") =>
        (await client.GetFromJsonAsync<List<AuditEntry>>($"/users/{actor}/audit{query}"))!;

    [Fact]
    public async Task AdminSeesEverythingNewestFirst()
    {
        await Start();

        var rows = await Audit(ann);

        Assert.Equal(7, rows.Count);
        Assert.Equal(rows.OrderByDescending(r => r.At).Select(r => r.At), rows.Select(r => r.At));
        Assert.Contains(rows, r => r.Username == "ghost" && r.Detail == "Failed sign-in");
        Assert.Contains(rows, r => r.Category == "passwords" && r.Detail == "Password reset requested from 5.6.7.8");
        Assert.Contains(rows, r => r.Category == "points" && r.Detail == "+250 points: Safe week" && r.By == "sue");
        Assert.Contains(rows, r => r.Detail == "-40 points: no reason given" && r.By == null);
    }

    [Fact]
    public async Task AdminCanNarrowToACompanyOrADriver()
    {
        await Start();

        var company = await Audit(ann, $"?companyId={acme}");
        var driver = await Audit(ann, $"?driverId={tom}");

        Assert.Equal(["bob", "sue"], company.Select(r => r.Username).Distinct().Order());
        Assert.All(driver, r => Assert.Equal("tom", r.Username));
        Assert.Equal(2, driver.Count);
    }

    [Fact]
    public async Task SponsorsOnlySeeTheirDrivers()
    {
        await Start();

        var rows = await Audit(sue);

        Assert.All(rows, r => Assert.Equal("bob", r.Username));
        Assert.Equal(3, rows.Count);
        Assert.Empty(await Audit(sue, $"?driverId={tom}"));
    }

    [Theory]
    [InlineData("?category=logins", 4)]
    [InlineData("?category=passwords", 1)]
    [InlineData("?category=points", 2)]
    [InlineData("?from=2026-09-12&to=2026-09-14", 3)]
    [InlineData("?from=2026-09-15", 2)]
    [InlineData("?to=2026-09-10", 1)]
    public async Task FiltersByCategoryAndDate(string query, int count)
    {
        await Start();
        Assert.Equal(count, (await Audit(ann, query)).Count);
    }

    [Fact]
    public async Task DriversAndCompanylessSponsorsAreRefused()
    {
        await Start();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/users/{bob}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/users/{zed}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/users/{ann}/audit?category=cats")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/users/999/audit")).StatusCode);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
