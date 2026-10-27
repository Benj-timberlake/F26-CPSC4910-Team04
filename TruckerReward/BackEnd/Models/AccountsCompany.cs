namespace BackEnd.Models;

public partial class AccountsCompany
{
    public int AccountId { get; set; }

    public int CompanyId { get; set; }

    public bool CurrentCompanyViewed { get; set; }

    public virtual User Account { get; set; } = null!;

    public virtual Company Company { get; set; } = null!;
}
