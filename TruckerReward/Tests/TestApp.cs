using BackEnd.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TruckerReward.Tests;

// the backend on in-memory sqlite, no mysql needed
public sealed class TestApp : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private WebApplication? app;

    public FakeEmail Email { get; } = new();
    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));

    public async Task<HttpClient> Start(Action<WebApplicationBuilder>? configure = null)
    {
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["FRONTEND_URL"] = "http://frontend";
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        builder.Services.AddSingleton<IEmailSender>(Email);
        builder.Services.AddSingleton<TimeProvider>(Clock);
        configure?.Invoke(builder);
        app = builder.Build();
        app.UseBackendApiKey();
        app.MapGet("/", () => Results.Ok());
        app.MapGet("/health", () => Results.Ok());
        app.MapAuthEndpoints();
        app.MapPasswordEndpoints();
        app.MapUserEndpoints();
        app.MapCartEndpoints();
        app.MapAdminEndpoints();
        using (var scope = app.Services.CreateScope())
        {
            // the mysql enum column types aren't valid in sqlite
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var script = System.Text.RegularExpressions.Regex.Replace(db.Database.GenerateCreateScript(), @"enum\([^)]*\)", "TEXT");
            await db.Database.ExecuteSqlRawAsync(script);
        }
        await app.StartAsync();
        return app.GetTestClient();
    }

    public AppDbContext Db() => app!.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    public async ValueTask DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
        await connection.DisposeAsync();
    }

    public sealed record Sent(string To, string Subject, string Body);

    public sealed class FakeEmail : IEmailSender
    {
        public List<Sent> Sent { get; } = new();
        public Task SendAsync(string to, string subject, string body)
        {
            Sent.Add(new Sent(to, subject, body));
            return Task.CompletedTask;
        }
    }

    public sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset now = now;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }
}
