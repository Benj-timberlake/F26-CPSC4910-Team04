using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class Application
{
    public int Id { get; set; }

    public int ApplicantId { get; set; }

    public int CompanyId { get; set; }

    public int? ReviewerId { get; set; }

    public string? ApplicantExtraInfo { get; set; }

    public string? ReviewerReasoning { get; set; }

    public string Status { get; set; } = null!;

    public DateTime ApplicationTimestamp { get; set; }

    public DateTime? ResponseTimestamp { get; set; }
}
