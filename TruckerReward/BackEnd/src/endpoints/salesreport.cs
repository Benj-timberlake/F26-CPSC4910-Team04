using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public static class SalesReportEndpoints
{
    public static void MapSalesReportEndpoints(this WebApplication app) =>
        app.MapGet("/users/{id:int}/reports/sales", Report);

    // no companyIds means every sponsor
    private static async Task<IResult> Report(int id, int[]? companyIds, DateOnly? from, DateOnly? to, AppDbContext db,
        bool detailed = true)
    {
        if (from > to)
            return Results.BadRequest(new { message = "The start date must be on or before the end date." });
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (actor is null)
            return Results.NotFound();
        if (actor.UserType != AuthEndpoints.Admin)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var picked = companyIds is { Length: > 0 } ? companyIds.Distinct().ToList() : null;
        var companies = await db.Companies.AsNoTracking()
            .Where(c => picked == null || picked.Contains(c.Id))
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Name }).ToListAsync();
        if (picked is not null && companies.Count != picked.Count)
            return Results.NotFound(new { message = "One of those sponsors doesn't exist." });

        // rejected orders were refunded, so they aren't sales
        var orders = db.PointsHistory.AsNoTracking()
            .Where(h => h.RejectionReason == null && h.Carts.Any() && (picked == null || picked.Contains(h.CompanyId)));
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
                h.Id, h.CompanyId, h.Timestamp, h.PointsDelta,
                Driver = h.User.FirstName + " " + h.User.LastName, h.User.Username,
                Items = h.Carts.Select(c => new { c.Name, c.Quantity, c.Price, c.WasOrdered }).ToList()
            }).ToListAsync();

        var sales = rows.Select(h => new Sale(h.Id, h.CompanyId, h.Timestamp, h.Driver.Trim(), h.Username,
            string.Join(", ", h.Items.Select(i => i.Quantity == 1 ? i.Name : $"{i.Name} x{i.Quantity}")),
            h.Items.Sum(i => i.Quantity), -(long)h.PointsDelta, h.Items.Sum(i => i.Price * i.Quantity),
            h.Items.All(i => i.WasOrdered) ? "Ordered" : "Pending")).ToLookup(s => s.CompanyId);
        return Results.Ok(companies.Select(c => new SponsorSales(c.Id, c.Name, sales[c.Id].Count(),
            sales[c.Id].Sum(s => s.Items), sales[c.Id].Sum(s => s.Points), sales[c.Id].Sum(s => s.Dollars),
            detailed ? sales[c.Id].ToList() : [])).ToList());
    }
}

public record SponsorSales(int CompanyId, string Name, int Orders, int Items, long Points, decimal Dollars, List<Sale> Sales);
public record Sale(int OrderId, int CompanyId, DateTime? At, string DriverName, string Username, string Products, int Items,
    long Points, decimal Dollars, string Status);
