namespace BackEnd.Models;

// one minute's database check. gaps between rows mean the app itself was down
public class UptimeCheck
{
    public int Id { get; set; }

    public DateTime CheckedAt { get; set; }

    public bool Ok { get; set; }

    public int LatencyMs { get; set; }

    public string? Error { get; set; }
}
