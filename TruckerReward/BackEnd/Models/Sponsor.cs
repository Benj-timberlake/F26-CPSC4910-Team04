namespace BackEnd.Models;

public partial class Sponsor
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string CompanyName { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
