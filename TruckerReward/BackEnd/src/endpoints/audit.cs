using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// one audit log over sign-ins, password changes, point changes and view-as sessions
public static class AuditEndpoints
{
    public const string Logins = "logins";
    public const string Passwords = "passwords";
    public const string Points = "points";
    public const string Views = "views";
    public const int MaxRows = 500;

    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/audit", async (int id, string? category, DateOnly? from, DateOnly? to, int? companyId, int? driverId, AppDbContext db) =>
        {
            var (error, rows) = await Query(db, id, category, from, to, companyId, driverId);
            return error ?? Results.Ok(rows);
        });

        app.MapGet("/users/{id:int}/audit.csv", async (int id, string? category, DateOnly? from, DateOnly? to, int? companyId, int? driverId, AppDbContext db) =>
        {
            var (error, rows) = await Query(db, id, category, from, to, companyId, driverId);
            return error ?? Results.File(System.Text.Encoding.UTF8.GetBytes(Csv(rows!)), "text/csv", "audit-log.csv");
        });
    }

    private static async Task<(IResult? Error, List<AuditEntry>? Rows)> Query(AppDbContext db, int id, string? category, DateOnly? from, DateOnly? to, int? companyId, int? driverId)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (actor is null)
            return (Results.NotFound(), null);
        if (category is not (null or "" or Logins or Passwords or Points or Views))
            return (Results.BadRequest(new { message = "Category must be logins, passwords, points or views." }), null);
        if (!PointsEndpoints.CanManage(actor))
            return (Results.StatusCode(StatusCodes.Status403Forbidden), null);

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
        if (all || category == Views)
            rows.AddRange(await ViewRows(db, ids, start, end));
        return (null, rows.OrderByDescending(r => r.At).Take(MaxRows).ToList());
    }

    public static string Csv(IEnumerable<AuditEntry> rows)
    {
        var csv = new System.Text.StringBuilder("When (UTC),Category,Account,What happened,By\r\n");
        foreach (var r in rows)
            csv.Append(string.Join(",", new[] { r.At.ToString("yyyy-MM-dd HH:mm:ss"), r.Category, r.Username, r.Detail, r.By ?? "" }.Select(Cell))).Append("\r\n");
        return csv.ToString();
    }

    // quotes every cell and defuses spreadsheet formulas
    private static string Cell(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
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

    private static Task<List<AuditEntry>> ViewRows(AppDbContext db, IQueryable<int>? ids, DateTime start, DateTime end)
    {
        var views = db.Impersonations.AsNoTracking().Where(v => v.StartedAt >= start && v.StartedAt < end);
        if (ids is not null)
            views = views.Where(v => ids.Contains(v.UserId));
        return views
            .OrderByDescending(v => v.StartedAt).Take(MaxRows)
            .Join(db.Users, v => v.UserId, u => u.Id, (v, u) => new { v, u.Username })
            .Join(db.Users, x => x.v.ViewerId, u => u.Id, (x, viewer) => new AuditEntry(x.v.StartedAt, Views, x.v.UserId, x.Username,
                "Site viewed as this account" + (x.v.IpAddress == null || x.v.IpAddress == "" ? "" : " from " + x.v.IpAddress), viewer.Username))
            .ToListAsync();
    }

    private static string PasswordText(string changeType) => changeType switch
    {
        PasswordChange.Changed => "Password changed",
        PasswordChange.ResetRequested => "Password reset requested",
        PasswordChange.ResetCompleted => "Password reset completed",
        _ => changeType
    };
}

// by names the point changer or viewer
public record AuditEntry(DateTime At, string Category, int? UserId, string Username, string Detail, string? By);
