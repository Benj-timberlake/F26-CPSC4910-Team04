namespace BackEnd.Models;

// an admin lifting a lockout early
public class LockoutReset
{
    public int Id { get; set; }

    public string Username { get; set; } = null!;

    public int? UserId { get; set; }

    public int AdminId { get; set; }

    public DateTime ResetAt { get; set; }
}
