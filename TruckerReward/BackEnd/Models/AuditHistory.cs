using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class AuditHistory
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int PointsHistoryId { get; set; }

    public string Resoning { get; set; } = null!;

    public virtual PointsHistory PointsHistory { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
