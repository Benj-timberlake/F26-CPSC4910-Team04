namespace BackEnd.Models;

// a link sent to prove an address belongs to the user. only a hash of the token is kept
public class EmailConfirmation
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Email { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }
}
