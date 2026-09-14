using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

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

app.MapGet("/dashboard", () =>
{
    var dashboard = new TruckerDashboard(
        new Trucker(
            "Jordan Miles",
            "TR-10482",
            "jordan.miles@example.com",
            "+1 (555) 013-9082",
            "Charlotte, NC"),
        new Points(
            2_450,
            550,
            "Gold",
            "Reach 3,000 points to unlock Platinum status."),
        new Sponsor(
            "Roadway Fuel & Travel",
            "Fuel Rewards Partner",
            "Earn 5 points per gallon at participating locations.",
            "support@roadwayfuel.example"));

    return Results.Ok(dashboard);
})
.WithName("GetTruckerDashboard");

app.Run();

record TruckerDashboard(Trucker Trucker, Points Points, Sponsor Sponsor);

record Trucker(string Name, string DriverId, string Email, string Phone, string HomeTerminal);

record Points(int CurrentBalance, int PointsToNextTier, string Tier, string NextMilestone);

record Sponsor(string Name, string Program, string Benefit, string ContactEmail);
