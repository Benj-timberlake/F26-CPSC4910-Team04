using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class Application
{
    public int Id { get; set; }

    public int DriverId { get; set; }

    public int CompanyId { get; set; }

    public int SponsorId { get; set; }

    public string? ExtraInfo { get; set; }

    public string Status { get; set; } = null!;

    public DateTime ApplicationDate { get; set; }

    public virtual Company Company { get; set; } = null!;

    public virtual User Driver { get; set; } = null!;

    public virtual User Sponsor { get; set; } = null!;
}
