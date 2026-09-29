using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class Cart
{
    public uint Id { get; set; }

    public int UserId { get; set; }

    public string Name { get; set; } = null!;

    public decimal Price { get; set; }

    public string? Description { get; set; }

    public int Quantity { get; set; }

    public bool WasOrdered { get; set; }

    public bool InCart { get; set; }

    public int? PointsHistoryId { get; set; }

    public virtual PointsHistory? PointsHistory { get; set; }

    public virtual User User { get; set; } = null!;
}
