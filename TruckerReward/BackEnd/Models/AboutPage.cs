using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class AboutPage
{
    public uint Id { get; set; }

    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Body { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
