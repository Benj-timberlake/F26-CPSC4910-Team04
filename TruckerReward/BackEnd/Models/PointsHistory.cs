using System.ComponentModel.DataAnnotations.Schema;

namespace BackEnd.Models;

[Table("Points_History")]
public sealed class PointsHistory
{
    [Column("points_history_id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [Column("points_delta")]
    public int PointsDelta { get; set; }

    [Column("timestamp", TypeName = "datetime")]
    public DateTime? Timestamp { get; set; }
}
