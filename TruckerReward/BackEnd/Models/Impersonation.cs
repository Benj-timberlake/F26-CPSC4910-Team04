namespace BackEnd.Models;

// one row each time an admin or sponsor starts viewing the site as someone else
public class Impersonation
{
    public int Id { get; set; }

    public int ViewerId { get; set; }

    public int UserId { get; set; }

    public DateTime StartedAt { get; set; }

    public string? IpAddress { get; set; }
}
