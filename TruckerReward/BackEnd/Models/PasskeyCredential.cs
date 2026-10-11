namespace BackEnd.Models;

// a face id, fingerprint or security key registered to sign in without a password
public class PasskeyCredential
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public byte[] CredentialId { get; set; } = null!;

    public byte[] PublicKey { get; set; } = null!;

    public long SignCount { get; set; }

    public string Name { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }
}
