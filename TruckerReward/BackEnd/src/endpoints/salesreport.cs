using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public static class SalesReportEndpoints
{
    public static void MapSalesReportEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/reports/sales", BySponsor);
        app.MapGet("/users/{id:int}/reports/sales/drivers", ByDriver);
    }

    // no companyIds means every sponsor
    private static async Task<IResult> BySponsor(int id, int[]? companyIds, DateOnly? from, DateOnly? to, AppDbContext db,
        bool detailed = true)
    {
        if (await Refuse(id, from, to, db) is { } refused)
            return refused;

        var picked = companyIds is { Length: > 0 } ? companyIds.Distinct().ToList() : null;
        var companies = await db.Companies.AsNoTracking()
            .Where(c => picked == null || picked.Contains(c.Id))
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Name }).ToListAsync();
        if (picked is not null && companies.Count != picked.Count)
            return Results.NotFound(new { message = "One of those sponsors doesn't exist." });

        var sales = (await Sales(db.PointsHistory.Where(h => picked == null || picked.Contains(h.CompanyId)), from, to))
            .ToLookup(s => s.CompanyId);
        return Results.Ok(companies.Select(c => new SponsorSales(c.Id, c.Name, sales[c.Id].Count(),
            sales[c.Id].Sum(s => s.Items), sales[c.Id].Sum(s => s.Points), sales[c.Id].Sum(s => s.Dollars),
            detailed ? sales[c.Id].ToList() : [])).ToList());
    }

    // drivers from one sponsor; no driverIds means all of them
    private static async Task<IResult> ByDriver(int id, int companyId, int[]? driverIds, DateOnly? from, DateOnly? to,
        AppDbContext db, bool detailed = true)
    {
        if (await Refuse(id, from, to, db) is { } refused)
            return refused;
        var company = await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync();
        if (company is null)
            return Results.NotFound(new { message = "That sponsor doesn't exist." });

        var picked = driverIds is { Length: > 0 } ? driverIds.Distinct().ToList() : null;
        var drivers = await db.Users.AsNoTracking()
            .Where(u => u.UserType == AuthEndpoints.Driver && (picked == null || picked.Contains(u.Id))
                && db.AccountsCompanies.Any(m => m.AccountId == u.Id && m.CompanyId == companyId))
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ThenBy(u => u.Id)
            .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName, u.Username }).ToListAsync();
        if (picked is not null && drivers.Count != picked.Count)
            return Results.NotFound(new { message = "One of those drivers isn't with this sponsor." });

        var ids = drivers.Select(d => d.Id).ToList();
        var sales = (await Sales(db.PointsHistory.Where(h => h.CompanyId == companyId && ids.Contains(h.UserId)), from, to))
            .ToLookup(s => s.DriverId);
        return Results.Ok(drivers.Select(d => new DriverSales(d.Id, d.Name.Trim(), d.Username, company, sales[d.Id].Count(),
            sales[d.Id].Sum(s => s.Items), sales[d.Id].Sum(s => s.Points), sales[d.Id].Sum(s => s.Dollars),
            detailed ? sales[d.Id].ToList() : [])).ToList());
    }

    private static async Task<IResult?> Refuse(int id, DateOnly? from, DateOnly? to, AppDbContext db)
    {
        if (from > to)
            return Results.BadRequest(new { message = "The start date must be on or before the end date." });
        var type = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.UserType).FirstOrDefaultAsync();
        if (type is null)
            return Results.NotFound();
        return type == AuthEndpoints.Admin ? null : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static async Task<List<Sale>> Sales(IQueryable<PointsHistory> history, DateOnly? from, DateOnly? to)
    {
        // rejected orders were refunded, so they aren't sales
        var orders = history.AsNoTracking().Where(h => h.RejectionReason == null && h.Carts.Any());
        if (from is not null)
        {
            var start = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            orders = orders.Where(h => h.Timestamp >= start);
        }
        if (to is not null)
        {
            var end = to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            orders = orders.Where(h => h.Timestamp <= end);
        }
        var rows = await orders.OrderByDescending(h => h.Timestamp).ThenByDescending(h => h.Id)
            .Select(h => new
            {
                h.Id, h.CompanyId, Sponsor = h.Company.Name, h.UserId, h.Timestamp, h.PointsDelta,
                Driver = h.User.FirstName + " " + h.User.LastName, h.User.Username,
                Items = h.Carts.Select(c => new { c.Name, c.Quantity, c.Price, c.WasOrdered }).ToList()
            }).ToListAsync();
        return rows.Select(h => new Sale(h.Id, h.CompanyId, h.Sponsor, h.UserId, h.Timestamp, h.Driver.Trim(), h.Username,
            string.Join(", ", h.Items.Select(i => i.Quantity == 1 ? i.Name : $"{i.Name} x{i.Quantity}")),
            h.Items.Sum(i => i.Quantity), -(long)h.PointsDelta, h.Items.Sum(i => i.Price * i.Quantity),
            h.Items.All(i => i.WasOrdered) ? "Ordered" : "Pending")).ToList();
    }
}

public record SponsorSales(int CompanyId, string Name, int Orders, int Items, long Points, decimal Dollars, List<Sale> Sales);
public record DriverSales(int DriverId, string Name, string Username, string Sponsor, int Orders, int Items, long Points,
    decimal Dollars, List<Sale> Sales);
public record Sale(int OrderId, int CompanyId, string Sponsor, int DriverId, DateTime? At, string DriverName, string Username,
    string Products, int Items, long Points, decimal Dollars, string Status);
