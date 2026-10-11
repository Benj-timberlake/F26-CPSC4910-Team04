using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// sponsors list and invite the other sponsor users at their company
public static class SponsorTeamEndpoints
{
    public static void MapSponsorTeamEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/sponsor-team", async (int id, AppDbContext db) =>
        {
            var sponsor = await Sponsor(db, id);
            if (sponsor is null)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Ok(await db.Users.AsNoTracking()
                .Where(u => u.UserType == AuthEndpoints.Sponsor && u.CompanyId == sponsor.CompanyId)
                .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                .Select(u => new TeamMember(u.Id, u.Username, u.FirstName, u.LastName, u.Email, u.Status))
                .ToListAsync());
        });

        app.MapPost("/users/{id:int}/sponsor-team", async (int id, InviteSponsorRequest req, AppDbContext db, Notifications notifications, TimeProvider clock, IConfiguration config) =>
        {
            var sponsor = await Sponsor(db, id);
            if (sponsor is null)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var company = await db.Companies.AsNoTracking().Where(c => c.Id == sponsor.CompanyId).Select(c => c.Name).FirstAsync();
            return await AdminEndpoints.Invite(db, notifications, clock, config, $"{sponsor.FirstName} {sponsor.LastName} at {company}",
                AuthEndpoints.Sponsor, req.Username, req.FirstName, req.LastName, req.Email, sponsor.CompanyId);
        });
    }

    // active sponsors with a company
    private static Task<User?> Sponsor(AppDbContext db, int id) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.UserType == AuthEndpoints.Sponsor && u.CompanyId != null && u.Status == User.Active);
}

public record TeamMember(int Id, string Username, string FirstName, string LastName, string Email, string Status);

public record InviteSponsorRequest(string? Username, string? FirstName, string? LastName, string? Email);
