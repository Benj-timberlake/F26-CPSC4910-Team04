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
            var companyId = await CompanyMembershipQueries.FirstCompanyId(db, id);
            return Results.Ok(await db.Users.AsNoTracking()
                .Where(u => u.UserType == AuthEndpoints.Sponsor && db.AccountsCompanies
                    .Any(membership => membership.AccountId == u.Id && membership.CompanyId == companyId))
                .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                .Select(u => new TeamMember(u.Id, u.Username, u.FirstName, u.LastName, u.Email, u.PhoneNumber, u.Address ?? "", u.Status))
                .ToListAsync());
        });

        app.MapPost("/users/{id:int}/sponsor-team", async (int id, InviteSponsorRequest req, AppDbContext db, Notifications notifications, TimeProvider clock, IConfiguration config) =>
        {
            var sponsor = await Sponsor(db, id);
            if (sponsor is null)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var companyId = await CompanyMembershipQueries.FirstCompanyId(db, id);
            var company = await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).FirstAsync();
            return await AdminEndpoints.Invite(db, notifications, clock, config, $"{sponsor.FirstName} {sponsor.LastName} at {company}",
                AuthEndpoints.Sponsor, req.Username, req.FirstName, req.LastName, req.Email, companyId);
        });

        app.MapPut("/users/{id:int}/sponsor-team/{memberId:int}", (int id, int memberId, UpdateSponsorMemberRequest req, AppDbContext db, IEmailSender email, TimeProvider clock, IConfiguration config) =>
            UpdateMember(id, memberId, AuthEndpoints.Sponsor, req, db, email, clock, config));

        app.MapDelete("/users/{id:int}/sponsor-team/{memberId:int}", async (int id, int memberId, AppDbContext db) =>
        {
            var sponsor = await Sponsor(db, id);
            if (sponsor is null || id == memberId)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var companyId = await CompanyMembershipQueries.FirstCompanyId(db, id);
            var member = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == memberId && u.UserType == AuthEndpoints.Sponsor);
            if (member is null || !await db.AccountsCompanies.AnyAsync(m => m.AccountId == memberId && m.CompanyId == companyId))
                return Results.NotFound();
            var wasCurrent = await db.AccountsCompanies.AnyAsync(m => m.AccountId == memberId && m.CompanyId == companyId && m.CurrentCompanyViewed);
            await db.AccountsCompanies.Where(m => m.AccountId == memberId && m.CompanyId == companyId).ExecuteDeleteAsync();
            if (wasCurrent)
            {
                var nextCompany = await db.AccountsCompanies.Where(m => m.AccountId == memberId)
                    .OrderBy(m => m.CompanyId).Select(m => (int?)m.CompanyId).FirstOrDefaultAsync();
                if (nextCompany is int next)
                    await db.AccountsCompanies.Where(m => m.AccountId == memberId && m.CompanyId == next)
                        .ExecuteUpdateAsync(update => update.SetProperty(m => m.CurrentCompanyViewed, true));
            }
            return Results.NoContent();
        });

        app.MapPost("/users/{id:int}/drivers", async (int id, InviteDriverRequest req, AppDbContext db, Notifications notifications, TimeProvider clock, IConfiguration config) =>
        {
            var sponsor = await Sponsor(db, id);
            if (sponsor is null)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var companyId = await CompanyMembershipQueries.FirstCompanyId(db, id);
            var company = await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).FirstAsync();
            return await AdminEndpoints.Invite(db, notifications, clock, config, $"{sponsor.FirstName} {sponsor.LastName} at {company}",
                AuthEndpoints.Driver, req.Username, req.FirstName, req.LastName, req.Email, companyId);
        });

        app.MapPut("/users/{id:int}/drivers/{driverId:int}", (int id, int driverId, UpdateSponsorMemberRequest req, AppDbContext db, IEmailSender email, TimeProvider clock, IConfiguration config) =>
            UpdateMember(id, driverId, AuthEndpoints.Driver, req, db, email, clock, config));
    }

    private static async Task<IResult> UpdateMember(int sponsorId, int memberId, string memberType, UpdateSponsorMemberRequest req,
        AppDbContext db, IEmailSender email, TimeProvider clock, IConfiguration config)
    {
        var sponsor = await Sponsor(db, sponsorId);
        if (sponsor is null || sponsorId == memberId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var companyId = await CompanyMembershipQueries.FirstCompanyId(db, sponsorId);
        var member = await db.Users.FirstOrDefaultAsync(u => u.Id == memberId && u.UserType == memberType);
        if (member is null || !await db.AccountsCompanies.AnyAsync(m => m.AccountId == memberId && m.CompanyId == companyId))
            return Results.NotFound();

        var first = req.FirstName?.Trim() ?? "";
        var last = req.LastName?.Trim() ?? "";
        var address = req.Email?.Trim() ?? "";
        var phone = req.PhoneNumber?.Trim() ?? "";
        var street = req.Address?.Trim() ?? "";
        if (first.Length is 0 or > 100 || last.Length is 0 or > 100)
            return Results.BadRequest(new { message = "First and last names are required and must be 100 characters or fewer." });
        if (address.Length is 0 or > 255 || !System.Net.Mail.MailAddress.TryCreate(address, out var parsedEmail) || parsedEmail.Address != address)
            return Results.BadRequest(new { message = "Enter a valid email address of 255 characters or fewer." });
        if (phone.Length > 20 || street.Length > 255)
            return Results.BadRequest(new { message = "Phone number must be 20 characters or fewer and address must be 255 characters or fewer." });
        if (await db.Users.AnyAsync(u => u.Email == address && u.Id != memberId))
            return Results.Conflict(new { message = "That email address is already in use." });

        var oldEmail = member.Email;
        var emailChanged = !string.Equals(oldEmail, address, StringComparison.OrdinalIgnoreCase);
        member.FirstName = first;
        member.LastName = last;
        member.Email = address;
        member.PhoneNumber = phone;
        member.Address = street;
        if (emailChanged) member.EmailConfirmedAt = null;
        await db.SaveChangesAsync();
        if (emailChanged)
        {
            await email.SendAsync(oldEmail, "Your TruckerReward email was changed",
                $"Hi {member.Username},\n\nA sponsor changed your account email from {oldEmail} to {address}. If that's wrong, contact an administrator.");
            await EmailConfirmEndpoints.Send(db, member, email, clock, config);
        }
        return Results.NoContent();
    }

    // active sponsors with a company
    private static async Task<User?> Sponsor(AppDbContext db, int id) =>
        await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.UserType == AuthEndpoints.Sponsor && u.Status == User.Active)
            is { } sponsor && await CompanyMembershipQueries.FirstCompanyId(db, id) is not null ? sponsor : null;
}

public record TeamMember(int Id, string Username, string FirstName, string LastName, string Email, string PhoneNumber, string Address, string Status);

public record InviteSponsorRequest(string? Username, string? FirstName, string? LastName, string? Email);
public record InviteDriverRequest(string? Username, string? FirstName, string? LastName, string? Email);
public record UpdateSponsorMemberRequest(string? FirstName, string? LastName, string? Email, string? PhoneNumber, string? Address);
