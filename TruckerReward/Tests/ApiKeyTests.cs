using System.Net;
using BackEnd.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

// the frontend is the only thing meant to call the backend, so when BACKEND_API_KEY is set
// every request except /health has to carry it
public sealed class ApiKeyTests : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private WebApplication? app;

    private async Task<HttpClient> Start(string? apiKey)
    {
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["BACKEND_API_KEY"] = apiKey;
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        app = builder.Build();
        app.UseBackendApiKey();
        app.MapGet("/", () => Results.Ok());
        app.MapAuthEndpoints();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var script = db.Database.GenerateCreateScript().Replace("enum('admin','sponsor','driver')", "TEXT");
            await db.Database.ExecuteSqlRawAsync(script);
        }
        await app.StartAsync();
        return app.GetTestClient();
    }

    [Fact]
    public async Task NoKeyConfiguredMeansEverythingIsOpen()
    {
        var client = await Start(apiKey: null);
        var response = await client.GetAsync("/admin/login-attempts?failedOnly=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MissingKeyIsRejected()
    {
        var client = await Start("s3cret");
        var response = await client.GetAsync("/admin/login-attempts?failedOnly=true");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongKeyIsRejected()
    {
        var client = await Start("s3cret");
        client.DefaultRequestHeaders.Add("X-Api-Key", "guess");
        var response = await client.GetAsync("/admin/login-attempts?failedOnly=true");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RightKeyIsAccepted()
    {
        var client = await Start("s3cret");
        client.DefaultRequestHeaders.Add("X-Api-Key", "s3cret");
        var response = await client.GetAsync("/admin/login-attempts?failedOnly=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthAndRootStayOpenForBeanstalkChecks()
    {
        var client = await Start("s3cret");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
        await connection.DisposeAsync();
    }
}
