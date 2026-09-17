namespace BackEnd.Models;

public partial class LoginAttempt
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public int? UserId { get; set; }
    public bool Succeeded { get; set; }
    public string? IpAddress { get; set; }
    public DateTime AttemptedAt { get; set; }
}
