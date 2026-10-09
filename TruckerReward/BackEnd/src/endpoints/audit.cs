using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// one audit log over sign-ins, password changes and point changes
public static class AuditEndpoints
{
    public const string Logins = "logins";
    public const string Passwords = "passwords";
    public const string Points = "points";
    public const int MaxRows = 500;

    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/audit", async (int id, string? category, DateOnly? from, DateOnly? to, int? companyId, int? driverId, AppDbContext db) =>
        {
            var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (actor is null)
                return Results.NotFound();
            if (category is not (null or "" or Logins or Passwords or Points))
                return Results.BadRequest(new { message = "Category must be logins, passwords or points." });
            if (!PointsEndpoints.CanManage(actor))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var ids = Scope(db, actor, companyId, driverId);
            var start = from?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
            var end = to?.AddDays(1).ToDateTime(TimeOnly.MinValue) ?? DateTime.MaxValue;
            var all = string.IsNullOrEmpty(category);
            var rows = new List<AuditEntry>();
            if (all || category == Logins)
                rows.AddRange(await LoginRows(db, ids, start, end));
            if (all || category == Passwords)
                rows.AddRange(await PasswordRows(db, ids, start, end));
            if (all || category == Points)
                rows.AddRange(await PointRows(db, ids, start, end));
            return Results.Ok(rows.OrderByDescending(r => r.At).Take(MaxRows).ToList());
        });
    }

    // null means every account, which only admins get
    private static IQueryable<int>? Scope(AppDbContext db, User actor, int? companyId, int? driverId)
    {
        IQueryable<User>? scope = actor.UserType == AuthEndpoints.Sponsor
            ? db.Users.Where(u => u.UserType == AuthEndpoints.Driver && u.CompanyId == actor.CompanyId)
            : companyId is null ? null : db.Users.Where(u => u.CompanyId == companyId);
        if (driverId is not null)
            scope = (scope ?? db.Users).Where(u => u.Id == driverId);
        return scope?.Select(u => u.Id);
    }

    private static Task<List<AuditEntry>> LoginRows(AppDbContext db, IQueryable<int>? ids, DateTime start, DateTime end)
    {
        var logins = db.LoginAttempts.AsNoTracking().Where(a => a.AttemptedAt >= start && a.AttemptedAt < end);
        if (ids is not null)
            logins = logins.Where(a => a.UserId != null && ids.Contains(a.UserId.Value));
        return logins
            .OrderByDescending(a => a.AttemptedAt).Take(MaxRows)
            .Select(a => new AuditEntry(a.AttemptedAt, Logins, a.UserId, a.Username,
                (a.Succeeded ? "Signed in" : "Failed sign-in") + (a.IpAddress == "" ? "" : " from " + a.IpAddress), null))
            .ToListAsync();
    }

    private static async Task<IEnumerable<AuditEntry>> PasswordRows(AppDbContext db, IQueryable<int>? ids, DateTime start, DateTime end)
    {
        var changes = db.PasswordChanges.AsNoTracking().Where(c => c.ChangedAt >= start && c.ChangedAt < end);
        if (ids is not null)
            changes = changes.Where(c => ids.Contains(c.UserId));
        var rows = await changes
            .OrderByDescending(c => c.ChangedAt).Take(MaxRows)
            .Join(db.Users, c => c.UserId, u => u.Id, (c, u) => new { c.ChangedAt, c.ChangeType, c.IpAddress, u.Id, u.Username })
            .ToListAsync();
        return rows.Select(c => new AuditEntry(c.ChangedAt, Passwords, c.Id, c.Username,
            PasswordText(c.ChangeType) + (string.IsNullOrEmpty(c.IpAddress) ? "" : " from " + c.IpAddress), null));
    }

    private static async Task<IEnumerable<AuditEntry>> PointRows(AppDbContext db, IQueryable<int>? ids, DateTime start, DateTime end)
    {
        var points = db.PointsHistory.AsNoTracking().Where(h => h.Timestamp >= start && h.Timestamp < end);
        if (ids is not null)
            points = points.Where(h => ids.Contains(h.UserId));
        var rows = await points
            .OrderByDescending(h => h.Timestamp).Take(MaxRows)
            .Select(h => new
            {
                h.Timestamp, h.PointsDelta, h.UserId, h.User.Username,
                Order = h.Carts.Any(),
                Audit = h.AuditHistories.Select(a => new { a.Resoning, a.User.Username }).FirstOrDefault()
            })
            .ToListAsync();
        return rows.Select(h => new AuditEntry(h.Timestamp ?? DateTime.MinValue, Points, h.UserId, h.Username,
            $"{h.PointsDelta:+#,0;-#,0} points: " + (h.Order ? "order" : h.Audit?.Resoning ?? "no reason given"),
            h.Order ? null : h.Audit?.Username));
    }

    private static string PasswordText(string changeType) => changeType switch
    {
        PasswordChange.Changed => "Password changed",
        PasswordChange.ResetRequested => "Password reset requested",
        PasswordChange.ResetCompleted => "Password reset completed",
        _ => changeType
    };
}

// by is whoever made a point change
public record AuditEntry(DateTime At, string Category, int? UserId, string Username, string Detail, string? By);
