using Scalar.AspNetCore;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// user secrets locally, an environment property on beanstalk, never appsettings
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not set, see README.md");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySQL(connectionString));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient();
builder.Services.AddSingleton<EbayClient>();
builder.AddEmail();

var app = builder.Build();

app.UseBackendApiKey();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/", () => Results.Ok(new { message = "TruckerReward API", health = "/health" }));
app.MapMarketEndpoints();
app.MapAuthEndpoints();
app.MapPasswordEndpoints();
app.MapUserEndpoints();
app.MapCartEndpoints();
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
