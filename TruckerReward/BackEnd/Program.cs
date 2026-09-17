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

// login, register and the failed login log, see src/endpoints/auth.cs
app.MapAuthEndpoints();

// change, forgot and reset password, see src/endpoints/password.cs
app.MapPasswordEndpoints();

app.Run();
