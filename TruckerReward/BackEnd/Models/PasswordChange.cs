namespace BackEnd.Models;

public partial class PasswordChange
{
    // values in the password_changes.change_type enum
    public const string Changed = "changed";
    public const string ResetRequested = "reset_requested";
    public const string ResetCompleted = "reset_completed";

    public int Id { get; set; }
    public int UserId { get; set; }
    public string ChangeType { get; set; } = null!;
    public string? IpAddress { get; set; }
    public DateTime ChangedAt { get; set; }
}
