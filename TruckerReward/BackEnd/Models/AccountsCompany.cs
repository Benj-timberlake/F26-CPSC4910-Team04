namespace BackEnd.Models;

public partial class AccountsCompany
{
    public int AccountId { get; set; }

    public int CompanyId { get; set; }

    public virtual AccountsHistory Account { get; set; } = null!;

    public virtual Company Company { get; set; } = null!;
}
