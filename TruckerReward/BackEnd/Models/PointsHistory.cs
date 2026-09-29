using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class PointsHistory
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public DateTime? Timestamp { get; set; }

    public int PointsDelta { get; set; }

    public virtual ICollection<AuditHistory> AuditHistories { get; set; } = new List<AuditHistory>();

    public virtual ICollection<Cart> Carts { get; set; } = new List<Cart>();

    public virtual User User { get; set; } = null!;
}
