using System;
using System.Collections.Generic;

namespace BackEnd.Models;

public partial class NotificationType
{
    public int Id { get; set; }

    public string Subject { get; set; } = null!;

    public string Body { get; set; } = null!;

    public virtual ICollection<NotificationsHistory> NotificationsHistories { get; set; } = new List<NotificationsHistory>();
}
