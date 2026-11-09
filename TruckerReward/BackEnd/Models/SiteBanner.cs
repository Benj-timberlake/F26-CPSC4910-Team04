namespace BackEnd.Models;

// the message shown above every page. the active one has no cleared_at
public class SiteBanner
{
    public int Id { get; set; }

    public string Message { get; set; } = null!;

    public string Level { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public int? SetBy { get; set; }

    public DateTime? ClearedAt { get; set; }
}
