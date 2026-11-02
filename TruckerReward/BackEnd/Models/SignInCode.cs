namespace BackEnd.Models;

// the second step of a sign in, emailed after the right password. only hashes are kept
public class SignInCode
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string ChallengeHash { get; set; } = null!;

    public string CodeHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public int Attempts { get; set; }

    public DateTime? UsedAt { get; set; }
}
