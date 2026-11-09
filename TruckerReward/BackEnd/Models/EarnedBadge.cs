namespace BackEnd.Models;

// a driver's earned badge
public class EarnedBadge
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int AchievementId { get; set; }

    public DateTime EarnedAt { get; set; }
}
