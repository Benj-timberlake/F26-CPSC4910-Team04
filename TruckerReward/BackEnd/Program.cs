using Scalar.AspNetCore;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// the database is RDS in every environment. locally the string lives in user secrets, on
// elastic beanstalk in the ConnectionStrings__DefaultConnection env var, never in appsettings
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not set. See db/README.md for the user-secrets command.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySQL(connectionString));

// timestamps on login attempts, resets and lockouts; tests swap in a fake
builder.Services.AddSingleton(TimeProvider.System);

// Allow endpoints to make HTTP requests to eBay.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<EbayClient>();

// reset links and security notices, see src/Email.cs
builder.AddEmail();

var app = builder.Build();

// everything after this needs X-Api-Key when BACKEND_API_KEY is set, see src/ApiKeyMiddleware.cs
app.UseBackendApiKey();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/", () => Results.Ok(new
{
    message = "TruckerReward API is running.",
    health = "/health"
}));

// Register the endpoint defined in src/endpoints/market.cs.
app.MapMarketEndpoints();

// see src/endpoints
app.MapAuthEndpoints();
app.MapPasswordEndpoints();
app.MapUserEndpoints();
app.MapAdminEndpoints();

// a real query, so this fails when the database is asleep or unreachable
app.MapGet("/health", async (AppDbContext db) =>
{
    try
    {
        return Results.Ok(new { status = "ok", users = await db.Users.CountAsync() });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 503);
    }
});

app.Run();
