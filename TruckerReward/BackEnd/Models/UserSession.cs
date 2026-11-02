namespace BackEnd.Models;

// one signed-in browser. the cookie holds the key, only its hash is kept
public class UserSession
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string KeyHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTime? RevokedAt { get; set; }
}
