using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class PointsHistory
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public DateTime? Timestamp { get; set; }

    public int PointsDelta { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.Column("rejection_reason")]
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? RejectionReason { get; set; }

    public virtual ICollection<AuditHistory> AuditHistories { get; set; } = new List<AuditHistory>();

    public virtual ICollection<CartItem> Carts { get; set; } = new List<CartItem>();

    public virtual User User { get; set; } = null!;
}
