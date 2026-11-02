namespace BackEnd.Models;

// a points reason a sponsor company reuses
public class SavedReason
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public string Reason { get; set; } = null!;
}
