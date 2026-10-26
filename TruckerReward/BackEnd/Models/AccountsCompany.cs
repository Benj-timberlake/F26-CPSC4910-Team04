namespace BackEnd.Models;

public partial class AccountsCompany
{
    public int UserId { get; set; }

    public int CompanyId { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual Company Company { get; set; } = null!;
}
