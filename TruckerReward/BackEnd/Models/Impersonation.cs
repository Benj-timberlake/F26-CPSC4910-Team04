namespace BackEnd.Models;

// one row per view-as session
public class Impersonation
{
    public int Id { get; set; }

    public int ViewerId { get; set; }

    public int UserId { get; set; }

    public DateTime StartedAt { get; set; }

    public string? IpAddress { get; set; }
}
