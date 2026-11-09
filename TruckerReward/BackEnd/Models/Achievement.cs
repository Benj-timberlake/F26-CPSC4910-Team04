namespace BackEnd.Models;

// a goal drivers earn a badge for. no sponsor company means it's built in and applies to every driver
public class Achievement
{
    public const string PointsEarned = "points_earned";
    public const string PointsSpent = "points_spent";
    public const string OrdersPlaced = "orders_placed";

    public static readonly IReadOnlyList<string> Metrics = [PointsEarned, PointsSpent, OrdersPlaced];

    // ids must match db/migrations/018_achievements.sql
    public static readonly Achievement[] BuiltIn =
    [
        new() { Id = 1, Name = "First points", Description = "Earn your first points.", Metric = PointsEarned, Threshold = 1 },
        new() { Id = 2, Name = "1,000 points", Description = "Earn 1,000 points in all.", Metric = PointsEarned, Threshold = 1_000 },
        new() { Id = 3, Name = "5,000 points", Description = "Earn 5,000 points in all.", Metric = PointsEarned, Threshold = 5_000 },
        new() { Id = 4, Name = "10,000 points", Description = "Earn 10,000 points in all.", Metric = PointsEarned, Threshold = 10_000 },
        new() { Id = 5, Name = "First order", Description = "Place your first order.", Metric = OrdersPlaced, Threshold = 1 },
        new() { Id = 6, Name = "Regular shopper", Description = "Place 5 orders.", Metric = OrdersPlaced, Threshold = 5 },
        new() { Id = 7, Name = "Big spender", Description = "Spend 5,000 points on orders.", Metric = PointsSpent, Threshold = 5_000 },
    ];

    public int Id { get; set; }

    public int? SponsorCompanyId { get; set; }

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string Metric { get; set; } = null!;

    public int Threshold { get; set; }

    public int? CreatedBy { get; set; }

    public bool Active { get; set; } = true;
}
