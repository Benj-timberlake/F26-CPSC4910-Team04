namespace BackEnd.Models;

// a missing row means the category's defaults apply
public class NotificationPreference
{
    public int UserId { get; set; }

    public string Category { get; set; } = null!;

    public bool Enabled { get; set; } = true;

    public bool Emailed { get; set; } = true;
}
