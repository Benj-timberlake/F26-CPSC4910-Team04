using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// company news from sponsors to their drivers
public static class AnnouncementEndpoints
{
    public const int MaxTitle = 100;
    public const int MaxBody = 2000;
    public const int MaxShown = 100;

    public static void MapAnnouncementEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/announcements", async (int id, AppDbContext db) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
                return Results.NotFound();
            List<int> companyIds;
            if (user.UserType == AuthEndpoints.Sponsor)
                companyIds = await CompanyMembershipQueries.FirstCompanyId(db, id) is int own ? [own] : [];
            else if (user.UserType == AuthEndpoints.Driver)
                companyIds = await db.AccountsCompanies.Where(m => m.AccountId == id).Select(m => m.CompanyId).ToListAsync();
            else
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            return Results.Ok(await db.Announcements.AsNoTracking()
                .Where(a => companyIds.Contains(a.CompanyId))
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
                .Take(MaxShown)
                .Select(a => new AnnouncementEntry(a.Id, a.Title, a.Body, a.CreatedAt,
                    db.Companies.Where(c => c.Id == a.CompanyId).Select(c => c.Name).FirstOrDefault() ?? "",
                    db.Users.Where(u => u.Id == a.AuthorId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault()))
                .ToListAsync());
        });

        app.MapPost("/users/{id:int}/announcements", async (int id, PostAnnouncementRequest req, AppDbContext db, Notifications notifications, TimeProvider clock) =>
        {
            var sponsor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.UserType == AuthEndpoints.Sponsor && u.Status == User.Active);
            if (sponsor is null || await CompanyMembershipQueries.FirstCompanyId(db, id) is not int companyId)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var title = req.Title?.Trim() ?? "";
            var body = req.Body?.Trim() ?? "";
            if (title.Length is 0 or > MaxTitle)
                return Results.BadRequest(new { message = $"Give a title of 1 to {MaxTitle} characters." });
            if (body.Length is 0 or > MaxBody)
                return Results.BadRequest(new { message = $"Write a message of 1 to {MaxBody:N0} characters." });

            var now = clock.GetUtcNow().UtcDateTime;
            var row = new Announcement { CompanyId = companyId, AuthorId = id, Title = title, Body = body, CreatedAt = now };
            db.Announcements.Add(row);
            await db.SaveChangesAsync();

            var company = await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).FirstAsync();
            var drivers = await db.Users.AsNoTracking()
                .Where(u => u.UserType == AuthEndpoints.Driver && u.Status == User.Active && db.AccountsCompanies
                    .Any(m => m.AccountId == u.Id && m.CompanyId == companyId))
                .ToListAsync();
            var author = $"{sponsor.FirstName} {sponsor.LastName}";
            foreach (var driver in drivers)
                await notifications.AnnouncementPosted(driver, company, title, body, author);
            return Results.Created($"/users/{id}/announcements", new AnnouncementEntry(row.Id, title, body, now, company, author));
        });
    }
}

public record AnnouncementEntry(int Id, string Title, string Body, DateTime CreatedAt, string Company, string? Author);

public record PostAnnouncementRequest(string? Title, string? Body);
