using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class LoginAttempt
{
    public int Id { get; set; }

    public DateTime AttemptedAt { get; set; }

    public string IpAddress { get; set; } = null!;

    public bool Succeeded { get; set; }

    public int? UserId { get; set; }

    public string Username { get; set; } = null!;

    public virtual User? User { get; set; }
}
