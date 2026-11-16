using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public static class SponsorReportEndpoints
{
    public static void MapSponsorReportEndpoints(this WebApplication app) =>
        app.MapGet("/users/{id:int}/reports/driver-points", Report);

    private static async Task<IResult> Report(int id, int? driverId, DateOnly? from, DateOnly? to, AppDbContext db)
    {
        if (from > to)
            return Results.BadRequest(new { message = "The start date must be on or before the end date." });
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (actor is null)
            return Results.NotFound();
        if (actor.UserType != AuthEndpoints.Sponsor ||
            await CompanyMembershipQueries.FirstCompanyId(db, id) is not int companyId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var drivers = db.Users.AsNoTracking().Where(u => u.UserType == AuthEndpoints.Driver &&
            db.AccountsCompanies.Any(m => m.AccountId == u.Id && m.CompanyId == companyId));
        if (driverId is not null)
        {
            drivers = drivers.Where(u => u.Id == driverId);
            if (!await drivers.AnyAsync())
                return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        var summaries = await drivers.OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ThenBy(u => u.Id)
            .Select(u => new
            {
                u.Id, Name = u.FirstName + " " + u.LastName, u.Username,
                TotalPoints = db.PointsHistory.Where(h => h.UserId == u.Id && h.CompanyId == companyId)
                    .Sum(h => (long)h.PointsDelta)
            }).ToListAsync();
        var history = db.PointsHistory.AsNoTracking().Where(h => h.CompanyId == companyId && drivers.Any(u => u.Id == h.UserId));
        if (from is not null)
        {
            var start = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            history = history.Where(h => h.Timestamp >= start);
        }
        if (to is not null)
        {
            var end = to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            history = history.Where(h => h.Timestamp <= end);
        }
        var changes = await history.OrderByDescending(h => h.Timestamp).ThenByDescending(h => h.Id)
            .Select(h => new
            {
                h.UserId, h.Timestamp, h.PointsDelta,
                Order = h.Carts.Any(),
                Audit = h.AuditHistories.OrderBy(a => a.Id).Select(a => new
                {
                    a.Resoning, Name = a.User.FirstName + " " + a.User.LastName
                }).FirstOrDefault()
            }).ToListAsync();
        var grouped = changes.ToLookup(h => h.UserId);
        return Results.Ok(summaries.Select(d => new DriverPointReport(d.Id, d.Name.Trim(), d.Username, d.TotalPoints,
            grouped[d.Id].Select(h => new DriverPointChange(h.Timestamp, h.PointsDelta,
                h.Audit is null ? null : h.Audit.Name.Trim(),
                h.Audit?.Resoning ?? (h.Order ? "Order" : "No reason recorded"))).ToList())).ToList());
    }
}

public record DriverPointReport(int DriverId, string DriverName, string Username, long TotalPoints, List<DriverPointChange> Changes);
public record DriverPointChange(DateTime? At, int Points, string? SponsorName, string Reason);
