using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class AccountsHistory
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public DateTime Timestamp { get; set; }

    public string UserType { get; set; } = null!;

    public string Username { get; set; } = null!;

    public string Password { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? PhoneNumber { get; set; }

    public string? Address { get; set; }

    public int Points { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public int? CompanyId { get; set; }

    public virtual User User { get; set; } = null!;
}
