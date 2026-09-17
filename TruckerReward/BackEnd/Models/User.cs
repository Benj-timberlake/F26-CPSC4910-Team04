using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class User
{
    public int Id { get; set; }

    public string UserType { get; set; } = null!;

    public string Username { get; set; } = null!;

    public string? Password { get; set; }

    public string Email { get; set; } = null!;

    public string PhoneNumber { get; set; } = null!;

    public string Address { get; set; } = null!;

    public int Points { get; set; }
}
