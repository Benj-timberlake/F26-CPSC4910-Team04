using Scalar.AspNetCore;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySQL(connectionString));

// Allow endpoints to make HTTP requests to eBay.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<EbayClient>();

var app = builder.Build();

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

app.Run();
