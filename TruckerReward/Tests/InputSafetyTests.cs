using System.Net;
using System.Net.Http.Json;
using System.Text;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class InputSafetyTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob, sue;

    private async Task Start()
    {
        client = await app.Start(jsonErrors: true);
        using var db = app.Db();
        var company = new Company { Name = "Acme" };
        db.Add(company);
        await db.SaveChangesAsync();
        var driver = new User { Username = "bob", FirstName = "Bob", LastName = "Baker", Email = "bob@example.com", UserType = "driver", PhoneNumber = "", Address = "", Password = "x", CompanyId = company.Id };
        var sponsor = new User { Username = "sue", FirstName = "Sue", LastName = "Moss", Email = "sue@example.com", UserType = "sponsor", PhoneNumber = "", Address = "", Password = "x", CompanyId = company.Id };
        db.Users.AddRange(driver, sponsor);
        await db.SaveChangesAsync();
        (bob, sue) = (driver.Id, sponsor.Id);
    }

    private static async Task<string?> Message(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Problem>())?.Message;

    [Theory]
    [InlineData("' OR '1'='1")]
    [InlineData("bob' --")]
    [InlineData("bob\"; DROP TABLE users; --")]
    public async Task InjectionInTheUsernameIsJustAWrongUsername(string username)
    {
        await Start();

        var response = await client.PostAsJsonAsync("/auth/login", new { username, password = "' OR '1'='1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, await app.Db().Users.CountAsync());
    }

    [Theory]
    [InlineData("%")]
    [InlineData("' OR 1=1 --")]
    [InlineData("_")]
    public async Task InjectionInSearchesMatchesNothing(string search)
    {
        await Start();
        var query = Uri.EscapeDataString(search);

        Assert.Empty((await client.GetFromJsonAsync<List<AdminAccount>>($"/admin/accounts?search={query}"))!);
        Assert.Empty((await client.GetFromJsonAsync<List<PointsDriver>>($"/users/{sue}/points/drivers?search={query}"))!);
    }

    [Fact]
    public async Task MalformedJsonGetsAMessage()
    {
        await Start();

        var response = await client.PostAsync("/auth/login", new StringContent("{\"username\": ", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(JsonErrors.Malformed, await Message(response));
    }

    [Fact]
    public async Task WrongFieldTypesGetAMessage()
    {
        await Start();

        var response = await client.PostAsync($"/users/{sue}/points/drivers/{bob}",
            new StringContent("""{"points":"lots","reason":"x"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(JsonErrors.Malformed, await Message(response));
        Assert.Empty(await app.Db().PointsHistory.ToListAsync());
    }

    [Fact]
    public async Task BadQueryValuesGetAMessage()
    {
        await Start();

        var response = await client.GetAsync($"/users/{sue}/audit?from=yesterday");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(JsonErrors.Malformed, await Message(response));
    }

    [Fact]
    public async Task ServerErrorsHideTheDetails()
    {
        await Start();
        await app.Db().Database.ExecuteSqlRawAsync("DROP TABLE audit_history;");

        var response = await client.GetAsync($"/users/{sue}/audit");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(JsonErrors.Failed, await Message(response));
        Assert.DoesNotContain("audit_history", body);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public async Task ExtremePointValuesAreRefused(int points)
    {
        await Start();
        var response = await client.PostAsJsonAsync($"/users/{sue}/points/drivers/{bob}", new { points, reason = "edge" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record Problem(string? Message);

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
