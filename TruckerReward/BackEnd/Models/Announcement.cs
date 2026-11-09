namespace BackEnd.Models;

// company news from a sponsor to the company's drivers
public class Announcement
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int? AuthorId { get; set; }

    public string Title { get; set; } = null!;

    public string Body { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
