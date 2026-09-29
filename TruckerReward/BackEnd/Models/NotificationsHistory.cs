using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class NotificationsHistory
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int? NotificationTypeId { get; set; }

    public string Subject { get; set; } = null!;

    public string Body { get; set; } = null!;

    public DateTime Timestamp { get; set; }

    public bool BeenRead { get; set; }

    public virtual NotificationType? NotificationType { get; set; }

    public virtual User User { get; set; } = null!;
}
