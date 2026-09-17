using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackEnd.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AuthEndpointTests : IAsyncDisposable
{
    // sqlite in memory instead of mysql so the tests don't need docker
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private WebApplication? app;

    private async Task<HttpClient> Start()
    {
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        app = builder.Build();
        app.MapAuthEndpoints();
        using (var scope = app.Services.CreateScope())
        {
            // the mysql enum column type isn't valid in sqlite
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var script = db.Database.GenerateCreateScript().Replace("enum('admin','sponsor','driver')", "TEXT");
            await db.Database.ExecuteSqlRawAsync(script);
        }
        await app.StartAsync();
        return app.GetTestClient();
    }

    private AppDbContext Db() => app!.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    private static object Driver(string username = "bob", string email = "bob@example.com") => new
    {
        username,
        email,
        password = "hunter22",
        userType = "driver",
        phoneNumber = "864-555-0100",
        address = "1 Main St"
    };

    [Fact]
    public async Task RegisterStoresHashNotPassword()
    {
        var client = await Start();
        var response = await client.PostAsJsonAsync("/auth/register", Driver());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var user = await Db().Users.SingleAsync();
        Assert.Equal("bob", user.Username);
        Assert.NotNull(user.Password);
        Assert.NotEqual("hunter22", user.Password);
        Assert.DoesNotContain("hunter22", user.Password);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("hunter22", body);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateUsernameIsRejected()
    {
        var client = await Start();
        await client.PostAsJsonAsync("/auth/register", Driver());
        var response = await client.PostAsJsonAsync("/auth/register", Driver(email: "other@example.com"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await Db().Users.CountAsync());
    }

    [Fact]
    public async Task DuplicateEmailIsRejected()
    {
        var client = await Start();
        await client.PostAsJsonAsync("/auth/register", Driver());
        var response = await client.PostAsJsonAsync("/auth/register", Driver(username: "bob2"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SponsorRegistrationCreatesSponsorRow()
    {
        var client = await Start();
        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            username = "acme",
            email = "acme@example.com",
            password = "hunter22",
            userType = "sponsor",
            companyName = "Acme Freight"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var sponsor = await Db().Sponsors.Include(s => s.User).SingleAsync();
        Assert.Equal("Acme Freight", sponsor.CompanyName);
        Assert.Equal("acme", sponsor.User.Username);
        Assert.Equal("sponsor", sponsor.User.UserType);

        using var details = JsonDocument.Parse(await client.GetStringAsync($"/users/{sponsor.UserId}"));
        Assert.Equal("Acme Freight", details.RootElement.GetProperty("companyName").GetString());
    }

    [Fact]
    public async Task SponsorWithoutCompanyIsRejected()
    {
        var client = await Start();
        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            username = "acme",
            email = "acme@example.com",
            password = "hunter22",
            userType = "sponsor"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminCannotBeCreatedThroughRegister()
    {
        var client = await Start();
        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            username = "root",
            email = "root@example.com",
            password = "hunter22",
            userType = "admin"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CorrectPasswordLogsInAndIsRecorded()
    {
        var client = await Start();
        await client.PostAsJsonAsync("/auth/register", Driver());
        var response = await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = "hunter22", ipAddress = "10.0.0.5" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var attempt = await Db().LoginAttempts.SingleAsync();
        Assert.True(attempt.Succeeded);
        Assert.Equal("bob", attempt.Username);
        Assert.NotNull(attempt.UserId);
        Assert.Equal("10.0.0.5", attempt.IpAddress);
    }

    [Fact]
    public async Task WrongPasswordIsRejectedAndRecorded()
    {
        var client = await Start();
        await client.PostAsJsonAsync("/auth/register", Driver());
        var response = await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var attempt = await Db().LoginAttempts.SingleAsync();
        Assert.False(attempt.Succeeded);
        Assert.NotNull(attempt.UserId);
    }

    [Fact]
    public async Task UnknownUserIsRecordedWithoutUserId()
    {
        var client = await Start();
        var response = await client.PostAsJsonAsync("/auth/login", new { username = "nobody", password = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var attempt = await Db().LoginAttempts.SingleAsync();
        Assert.False(attempt.Succeeded);
        Assert.Equal("nobody", attempt.Username);
        Assert.Null(attempt.UserId);
    }

    [Fact]
    public async Task FailedOnlyFilterOnlyReturnsFailures()
    {
        var client = await Start();
        await client.PostAsJsonAsync("/auth/register", Driver());
        await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = "hunter22" });
        await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = "nope" });
        await client.PostAsJsonAsync("/auth/login", new { username = "eve", password = "nope" });

        var failed = await client.GetFromJsonAsync<List<LoginAttempt>>("/admin/login-attempts?failedOnly=true");
        var all = await client.GetFromJsonAsync<List<LoginAttempt>>("/admin/login-attempts?failedOnly=false");
        Assert.Equal(2, failed!.Count);
        Assert.All(failed, a => Assert.False(a.Succeeded));
        Assert.Equal(3, all!.Count);
    }

    [Fact]
    public async Task ExternalLoginCreatesDriverOnceAndReusesIt()
    {
        var client = await Start();
        var first = await client.PostAsJsonAsync("/auth/external", new { provider = "Google", email = "sue@gmail.com", name = "Sue" });
        var second = await client.PostAsJsonAsync("/auth/external", new { provider = "Microsoft", email = "sue@gmail.com", name = "Sue" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var user = await Db().Users.SingleAsync();
        Assert.Equal("sue", user.Username);
        Assert.Equal("driver", user.UserType);
        Assert.Null(user.Password);
        Assert.Equal(2, await Db().LoginAttempts.CountAsync(a => a.UserId == user.Id && a.Succeeded));
    }

    [Fact]
    public async Task ExternalLoginPicksFreeUsername()
    {
        var client = await Start();
        await client.PostAsJsonAsync("/auth/register", Driver(username: "sue", email: "sue@example.com"));
        await client.PostAsJsonAsync("/auth/external", new { provider = "Google", email = "sue@gmail.com" });
        Assert.Contains(await Db().Users.Select(u => u.Username).ToListAsync(), n => n == "sue2");
    }

    [Fact]
    public async Task SsoOnlyAccountCannotLogInWithPassword()
    {
        var client = await Start();
        await client.PostAsJsonAsync("/auth/external", new { provider = "Google", email = "sue@gmail.com" });
        var response = await client.PostAsJsonAsync("/auth/login", new { username = "sue", password = "" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NewDriverStartsWithZeroPoints()
    {
        var client = await Start();
        await client.PostAsJsonAsync("/auth/register", Driver());
        var user = await Db().Users.SingleAsync();

        var details = await client.GetFromJsonAsync<UserDetails>($"/users/{user.Id}");
        Assert.NotNull(details);
        Assert.Equal(0, details.Points);
        Assert.Equal("driver", details.UserType);
        Assert.Null(details.CompanyName);
    }

    [Fact]
    public async Task UserDetailsIncludesStoredPoints()
    {
        var client = await Start();
        await client.PostAsJsonAsync("/auth/register", Driver());
        using (var db = Db())
        {
            var user = await db.Users.SingleAsync();
            user.Points = 1250;
            await db.SaveChangesAsync();
        }
        var id = (await Db().Users.SingleAsync()).Id;

        var details = await client.GetFromJsonAsync<UserDetails>($"/users/{id}");
        Assert.Equal(1250, details!.Points);
    }

    [Fact]
    public async Task UnknownUserDetailsIs404()
    {
        var client = await Start();
        var response = await client.GetAsync("/users/999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LoginActivityForFirstLoginHasNoPreviousLogin()
    {
        var client = await Start();
        await client.PostAsJsonAsync("/auth/register", Driver());
        await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = "hunter22", ipAddress = "10.0.0.5" });
        var id = (await Db().Users.SingleAsync()).Id;

        var activity = await client.GetFromJsonAsync<LoginActivity>($"/users/{id}/logins");
        Assert.NotNull(activity);
        Assert.NotNull(activity.LastLoginAt);
        Assert.Equal("10.0.0.5", activity.LastLoginIp);
        Assert.Null(activity.PreviousLoginAt);
        Assert.Equal(0, activity.FailedSincePreviousLogin);
        Assert.Single(activity.Recent);
    }

    [Fact]
    public async Task LoginActivityCountsFailuresSincePreviousLogin()
    {
        var client = await Start();
        await client.PostAsJsonAsync("/auth/register", Driver());
        var id = (await Db().Users.SingleAsync()).Id;

        // an old failure, a login, then two failures and today's login. only the two count.
        using (var db = Db())
        {
            var t = DateTime.UtcNow.AddDays(-3);
            db.LoginAttempts.AddRange(
                new LoginAttempt { Username = "bob", UserId = id, Succeeded = false, AttemptedAt = t },
                new LoginAttempt { Username = "bob", UserId = id, Succeeded = true, IpAddress = "1.1.1.1", AttemptedAt = t.AddHours(1) },
                new LoginAttempt { Username = "bob", UserId = id, Succeeded = false, AttemptedAt = t.AddHours(2) },
                new LoginAttempt { Username = "bob", UserId = id, Succeeded = false, AttemptedAt = t.AddHours(3) });
            await db.SaveChangesAsync();
        }
        await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = "hunter22", ipAddress = "10.0.0.5" });

        var activity = await client.GetFromJsonAsync<LoginActivity>($"/users/{id}/logins");
        Assert.Equal("10.0.0.5", activity!.LastLoginIp);
        Assert.NotNull(activity.PreviousLoginAt);
        Assert.Equal(2, activity.FailedSincePreviousLogin);
        Assert.Equal(5, activity.Recent.Count);
        Assert.True(activity.Recent[0].AttemptedAt >= activity.Recent[1].AttemptedAt, "newest first");
    }

    [Fact]
    public async Task LoginActivityOnlyShowsThatUsersAttempts()
    {
        var client = await Start();
        await client.PostAsJsonAsync("/auth/register", Driver());
        await client.PostAsJsonAsync("/auth/register", Driver(username: "amy", email: "amy@example.com"));
        await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = "hunter22" });
        await client.PostAsJsonAsync("/auth/login", new { username = "amy", password = "wrong" });
        await client.PostAsJsonAsync("/auth/login", new { username = "amy", password = "hunter22" });
        var bob = await Db().Users.SingleAsync(u => u.Username == "bob");

        var activity = await client.GetFromJsonAsync<LoginActivity>($"/users/{bob.Id}/logins");
        Assert.Single(activity!.Recent);
        Assert.Equal(0, activity.FailedSincePreviousLogin);
    }

    [Fact]
    public async Task LoginActivityForUnknownUserIs404()
    {
        var client = await Start();
        var response = await client.GetAsync("/users/999/logins");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
        await connection.DisposeAsync();
    }
}
