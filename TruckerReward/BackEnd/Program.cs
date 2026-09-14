using Scalar.AspNetCore;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySQL(connectionString));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/", () => Results.Ok(new
{
    message = "TruckerReward API is running.",
    dashboard = "/dashboard"
}));

app.MapGet("/dashboard", async (AppDbContext db) =>
{
    var driver = await db.Users
        .AsNoTracking()
        .Where(user => user.UserType == "driver")
        .OrderBy(user => user.Id)
        .Select(user => new UserProfile(
            user.Id,
            user.UserType,
            user.Username,
            user.Email,
            user.PhoneNumber,
            user.Address))
        .FirstOrDefaultAsync();

    return driver is null
        ? Results.NotFound(new { message = "No driver user was found." })
        : Results.Ok(driver);
})
.WithName("GetDriverDashboard");

app.Run();

record UserProfile(int Id, string UserType, string Username, string Email, string PhoneNumber, string Address);
