using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class Company
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public decimal MonthlyBudget { get; set; }

    public virtual ICollection<Application> Applications { get; set; } = new List<Application>();

    public virtual ICollection<AccountsCompany> AccountsCompanies { get; set; } = new List<AccountsCompany>();

    public virtual ICollection<PointsHistory> PointsHistories { get; set; } = new List<PointsHistory>();
}
