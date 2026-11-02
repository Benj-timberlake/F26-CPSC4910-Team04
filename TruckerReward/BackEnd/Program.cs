using Scalar.AspNetCore;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not set, see README.md");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySQL(connectionString));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<MonthlySponsorBudgetService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<EbayClient>();
builder.AddEmail();
builder.Services.AddScoped<Notifications>();
builder.Services.AddPasskeys(builder.Configuration);
builder.AddJsonErrors();

var app = builder.Build();

app.UseJsonErrors();
app.UseBackendApiKey();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/", () => Results.Ok(new { message = "TruckerReward API", health = "/health" }));
app.MapMarketEndpoints();
app.MapApi();
app.MapCartEndpoints();
app.MapCatalogEndpoints();
app.MapAboutEndpoints();

// a real query, so this fails when the database is asleep or unreachable
app.MapGet("/health", async (AppDbContext db, ILogger<Program> log) =>
{
    try
    {
        return Results.Ok(new { status = "ok", users = await db.Users.CountAsync() });
    }
    catch (Exception ex)
    {
        // the message can name the database host, so it stays in the log
        log.LogError(ex, "health check failed");
        return Results.Problem(detail: "The database is unreachable.", statusCode: 503);
    }
});

app.Run();
