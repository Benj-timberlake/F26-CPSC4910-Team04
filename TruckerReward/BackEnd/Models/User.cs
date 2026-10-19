using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class User
{
    public int Id { get; set; }

    public string UserType { get; set; } = null!;

    public string Username { get; set; } = null!;

    // null for google/microsoft-only accounts
    public string? Password { get; set; }

    public string Email { get; set; } = null!;

    public string PhoneNumber { get; set; } = null!;

    public string? Address { get; set; }

    public int Points { get; set; }

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public int? CompanyId { get; set; }

    // values in the users.status enum
    public const string Active = "active";
    public const string Inactive = "inactive";

    public string Status { get; set; } = Active;

    public virtual ICollection<AccountsHistory> AccountsHistories { get; set; } = new List<AccountsHistory>();

    public virtual Application? ApplicationDriver { get; set; }

    public virtual ICollection<Application> ApplicationSponsors { get; set; } = new List<Application>();

    public virtual ICollection<AuditHistory> AuditHistories { get; set; } = new List<AuditHistory>();

    public virtual ICollection<CartItem> Carts { get; set; } = new List<CartItem>();

    public virtual Company? Company { get; set; }

    public virtual ICollection<LoginAttempt> LoginAttempts { get; set; } = new List<LoginAttempt>();

    public virtual ICollection<NotificationsHistory> NotificationsHistories { get; set; } = new List<NotificationsHistory>();

    public virtual ICollection<PasswordChange> PasswordChanges { get; set; } = new List<PasswordChange>();

    public virtual ICollection<PasswordReset> PasswordResets { get; set; } = new List<PasswordReset>();

    public virtual ICollection<PointsHistory> PointsHistories { get; set; } = new List<PointsHistory>();

}
