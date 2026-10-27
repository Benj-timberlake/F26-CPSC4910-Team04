using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// what the dashboard shows about the signed in user
public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/drivers/{driverId:int}/view", async (int id, int driverId, AppDbContext db) =>
        {
            var sponsor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            var sponsorCompanyId = sponsor is null ? null : await CompanyMembershipQueries.FirstCompanyId(db, id);
            if (sponsor is null || sponsor.UserType != "sponsor" || sponsorCompanyId is null ||
                sponsor.Status != User.Active)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var driver = await db.Users.AsNoTracking()
                .Where(u => u.Id == driverId && u.UserType == "driver" && u.Status == User.Active &&
                    CompanyMembershipQueries.FirstPerUser(db).Any(membership => membership.AccountId == u.Id && membership.CompanyId == sponsorCompanyId))
                .Select(u => new { u.Id, u.UserType, u.Username, u.Email, u.PhoneNumber, u.Address, u.Points })
                .FirstOrDefaultAsync();
            return driver is null ? Results.NotFound() : Results.Ok(driver);
        });

        app.MapGet("/companies", async (AppDbContext db) =>
            Results.Ok(await db.Companies.AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CompanyOption(c.Id, c.Name, c.Description))
                .ToListAsync()));

        app.MapPost("/users/{id:int}/companies/{companyId:int}/switch", async (int id, int companyId, AppDbContext db) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(user => user.Id == id && user.UserType == AuthEndpoints.Driver);
            if (user is null)
                return Results.NotFound();

            var membership = await db.AccountsCompanies.FirstOrDefaultAsync(link => link.AccountId == id && link.CompanyId == companyId);
            if (membership is null)
                return Results.NotFound(new { message = "You are not associated with that company." });

            await db.AccountsCompanies.Where(link => link.AccountId == id)
                .ExecuteUpdateAsync(update => update.SetProperty(link => link.CurrentCompanyViewed, link => link.CompanyId == companyId));
            return Results.Ok();
        });

        app.MapPost("/users/{id:int}/applications", async (int id, CreateApplicationRequest request, AppDbContext db, Notifications notifications) =>
        {
            var applicant = await db.Users.FirstOrDefaultAsync(user => user.Id == id);
            if (applicant is null)
                return Results.NotFound();
            if (applicant.UserType != "driver" && applicant.UserType != "sponsor")
                return Results.BadRequest(new { message = "Only drivers or sponsors can apply." });
            if (applicant.UserType == "sponsor" && await CompanyMembershipQueries.FirstCompanyId(db, id) is not null)
                return Results.BadRequest(new { message = "Sponsors can only belong to one company." });
            var companyName = await db.Companies.Where(company => company.Id == request.CompanyId)
                .Select(company => company.Name).FirstOrDefaultAsync();
            if (companyName is null)
                return Results.BadRequest(new { message = "The selected company does not exist." });
            if (await db.Applications.AnyAsync(application =>
                application.ApplicantId == id && application.CompanyId == request.CompanyId &&
                (application.Status == "active" || application.Status == "approved")))
                return Results.Conflict(new { message = "You already have an active or accepted application for this company." });

            db.Applications.Add(new Application
            {
                ApplicantId = id,
                CompanyId = request.CompanyId,
                ApplicantExtraInfo = string.IsNullOrWhiteSpace(request.ApplicantExtraInfo)
                    ? null
                    : request.ApplicantExtraInfo.Trim()
            });

            await db.SaveChangesAsync();
            await notifications.CompanyApplicationSubmitted(applicant, request.CompanyId, companyName);
            return Results.Created($"/users/{id}/applications", new { message = "Application submitted." });
        });

        app.MapPost("/users/{id:int}/applications/cancel", async (int id, CancelApplicationRequest request, AppDbContext db, Notifications notifications) =>
        {
            var reasoning = request.Reasoning?.Trim();
            if (string.IsNullOrWhiteSpace(reasoning))
                return Results.BadRequest(new { message = "A cancellation reason is required." });

            var applicant = await db.Users.FirstOrDefaultAsync(user => user.Id == id);
            if (applicant is null)
                return Results.NotFound();
            var application = await db.Applications.FirstOrDefaultAsync(item => item.ApplicantId == id && item.Status == "active");
            if (application is null)
                return Results.Conflict(new { message = "No active application was found." });

            application.Status = "canceled";
            application.ReviewerId = applicant.Id;
            application.ReviewerReasoning = reasoning;
            await db.SaveChangesAsync();
            var companyName = await db.Companies.Where(company => company.Id == application.CompanyId)
                .Select(company => company.Name).FirstOrDefaultAsync() ?? "the company";
            await notifications.CompanyApplicationCanceled(applicant, application.CompanyId, companyName, reasoning);
            return Results.Ok();
        });

        app.MapGet("/users/{reviewerId:int}/applications/{applicantType}", async (int reviewerId, string applicantType, AppDbContext db) =>
        {
            if (applicantType is not ("sponsor" or "driver"))
                return Results.BadRequest(new { message = "Applicant type must be sponsor or driver." });

            var reviewer = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == reviewerId);
            if (reviewer is null || (reviewer.UserType != "admin" && reviewer.UserType != "sponsor"))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (reviewer.UserType == "sponsor" && applicantType != "driver")
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var query = db.Applications.AsNoTracking()
                .Join(db.Users.AsNoTracking(), application => application.ApplicantId, applicant => applicant.Id,
                    (application, applicant) => new { application, applicant })
                .Where(row => row.applicant.UserType == applicantType);

            var reviewerCompanyId = reviewer.UserType == "sponsor"
                ? await CompanyMembershipQueries.FirstCompanyId(db, reviewerId) : null;
            if (reviewer.UserType == "sponsor")
                query = query.Where(row => reviewerCompanyId != null && row.application.CompanyId == reviewerCompanyId);

            var applicationData = await query
                .OrderByDescending(row => row.application.Id)
                .Join(db.Companies.AsNoTracking(), row => row.application.CompanyId, company => company.Id,
                    (row, company) => new ApplicationListingData(
                        row.application.Id,
                        row.application.ApplicantId,
                        row.application.CompanyId,
                        row.applicant.FirstName + " " + row.applicant.LastName,
                        row.applicant.Email,
                        row.application.ApplicantExtraInfo,
                        company.Name,
                        row.application.Status,
                        row.application.ReviewerId,
                        row.application.ReviewerReasoning,
                        row.application.ApplicationTimestamp,
                        row.application.ResponseTimestamp))
                .ToListAsync();

            var reviewerIds = applicationData
                .Where(application => application.ReviewerId.HasValue)
                .Select(application => application.ReviewerId!.Value)
                .Distinct()
                .ToArray();
            var applicantIds = applicationData.Select(application => application.ApplicantId).Distinct().ToArray();
            var applicantsWithOpenOrAcceptedApplication = (await db.Applications.AsNoTracking()
                .Where(application => applicantIds.Contains(application.ApplicantId) &&
                    (application.Status == "active" || application.Status == "approved"))
                .Select(application => application.ApplicantId)
                .Distinct()
                .ToListAsync())
                .ToHashSet();
            var reviewers = await db.Users.AsNoTracking()
                .Where(user => reviewerIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id);
            var applications = applicationData.Select(application =>
            {
                User? applicationReviewer = null;
                if (application.ReviewerId is int id)
                    reviewers.TryGetValue(id, out applicationReviewer);

                return new ApplicationListing(
                    application.Id,
                    application.ApplicantId,
                    application.CompanyId,
                    application.ApplicantName,
                    application.ApplicantEmail,
                    application.ApplicantExtraInfo,
                    application.CompanyName,
                    application.Status,
                    applicationReviewer?.UserType,
                    applicationReviewer is null ? null : $"{applicationReviewer.FirstName} {applicationReviewer.LastName}",
                    applicationReviewer?.Email,
                    application.ReviewerReasoning,
                    application.Status == "approved" ||
                        (application.Status == "rejected" && !applicantsWithOpenOrAcceptedApplication.Contains(application.ApplicantId)),
                    application.ApplicationTimestamp,
                    application.ResponseTimestamp);
            }).ToList();

            return Results.Ok(applications);
        });

        app.MapPost("/users/{reviewerId:int}/applications/{applicationId:int}/review", async
            (int reviewerId, int applicationId, ReviewApplicationRequest request, AppDbContext db, TimeProvider clock, Notifications notifications) =>
        {
            var reviewer = await db.Users.FirstOrDefaultAsync(user => user.Id == reviewerId);
            if (reviewer is null || (reviewer.UserType != "admin" && reviewer.UserType != "sponsor"))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var decision = request.Decision?.Trim().ToLowerInvariant();
            if (decision is not ("approved" or "rejected" or "active"))
                return Results.BadRequest(new { message = "Decision must be approved, rejected, or active." });
            var reasoning = request.Reasoning?.Trim();
            if (string.IsNullOrWhiteSpace(reasoning))
                return Results.BadRequest(new { message = "A reason is required." });

            var application = await db.Applications.FirstOrDefaultAsync(item => item.Id == applicationId);
            if (application is null)
                return Results.NotFound();

            var applicant = await db.Users.FirstOrDefaultAsync(user => user.Id == application.ApplicantId);
            if (applicant is null)
                return Results.NotFound();
            var companyName = await db.Companies.Where(company => company.Id == application.CompanyId)
                .Select(company => company.Name).FirstOrDefaultAsync() ?? "the company";
            var reviewerCompanyId = reviewer.UserType == "sponsor"
                ? await CompanyMembershipQueries.FirstCompanyId(db, reviewerId) : null;
            if (reviewer.UserType == "sponsor" &&
                (reviewerCompanyId is null || application.CompanyId != reviewerCompanyId || applicant.UserType != "driver"))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            if (decision == "active" && application.Status == "active")
                return Results.Conflict(new { message = "This application is already active." });
            if (decision != "active" && application.Status != "active")
                return Results.Conflict(new { message = "Only active applications can be accepted or denied." });
            if (decision == "approved" && await db.Applications.AnyAsync(item =>
                item.Id != application.Id && item.ApplicantId == application.ApplicantId &&
                item.CompanyId == application.CompanyId && item.Status == "approved"))
                return Results.Conflict(new { message = "This applicant is already accepted by this company." });

            var previous = application.Status;
            application.ReviewerId = reviewer.Id;
            application.ReviewerReasoning = reasoning;
            application.Status = decision;
            application.ResponseTimestamp = clock.GetUtcNow().UtcDateTime;
            if (decision == "approved")
            {
                await AddCompanyMembership(db, applicant, application.CompanyId);
            }
            else if (decision == "active" && previous == "approved")
            {
                await db.AccountsCompanies.Where(membership => membership.AccountId == applicant.Id && membership.CompanyId == application.CompanyId)
                    .ExecuteDeleteAsync();
            }

            await db.SaveChangesAsync();
            await notifications.CompanyApplicationReviewed(applicant, reviewer, previous, decision, companyName, reasoning);
            return Results.Ok();
        });

        app.MapGet("/users/{id:int}", async (int id, AppDbContext db) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
                return Results.NotFound();
            var effectiveCompanyId = await CompanyMembershipQueries.FirstCompanyId(db, id);
            var company = await db.Companies.AsNoTracking()
                .Where(c => c.Id == effectiveCompanyId)
                .Select(c => new { c.Name, c.MonthlyBudget })
                .FirstOrDefaultAsync();
            var companyNames = (await db.AccountsCompanies.AsNoTracking()
                .Where(link => link.AccountId == id)
                .OrderBy(link => link.AccountId)
                .ThenBy(link => link.CompanyId)
                .Select(link => link.Company.Name)
                .ToListAsync())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var memberships = await db.AccountsCompanies.AsNoTracking()
                .Where(link => link.AccountId == id)
                .OrderBy(link => link.Company.Name)
                .Select(link => new UserCompany(link.CompanyId, link.Company.Name, link.CurrentCompanyViewed))
                .ToListAsync();
            if (companyNames.Count == 0 && company is not null)
                companyNames.Add(company.Name);
            var pendingCompany = await db.Applications.AsNoTracking()
                .Where(application => application.ApplicantId == id && application.Status == "active")
                .Join(db.Companies.AsNoTracking(), application => application.CompanyId, c => c.Id, (application, c) => c.Name)
                .FirstOrDefaultAsync();
            var displayedPoints = user.UserType == AuthEndpoints.Driver && effectiveCompanyId is int currentCompanyId
                ? await db.PointsHistory.Where(history => history.UserId == id && history.CompanyId == currentCompanyId)
                    .SumAsync(history => (long?)history.PointsDelta) ?? 0L
                : user.Points;
            return Results.Ok(new UserDetails(user.Id, user.UserType, user.Username, user.FirstName, user.LastName, user.Email, user.PhoneNumber, user.Address ?? "", company?.Name, companyNames, memberships, effectiveCompanyId, displayedPoints, pendingCompany,
                user.UserType == "sponsor" ? company?.MonthlyBudget : null));
        });

        // this login, the one before it, failures in between, the last ten attempts and when the
        // password last changed
        app.MapGet("/users/{id:int}/security", async (int id, AppDbContext db) =>
        {
            if (!await db.Users.AnyAsync(u => u.Id == id))
                return Results.NotFound();

            var attempts = db.LoginAttempts.AsNoTracking()
                .Where(a => a.UserId == id)
                .OrderByDescending(a => a.AttemptedAt).ThenByDescending(a => a.Id);
            var logins = await attempts.Where(a => a.Succeeded).Take(2).ToListAsync();
            var last = logins.ElementAtOrDefault(0);
            var previous = logins.ElementAtOrDefault(1);

            var failedSincePrevious = await attempts
                .CountAsync(a => !a.Succeeded && (previous == null || a.AttemptedAt > previous.AttemptedAt));
            var recent = await attempts
                .Take(10)
                .Select(a => new LoginEvent(a.AttemptedAt, a.Succeeded, a.IpAddress))
                .ToListAsync();
            var passwordChangedAt = await db.PasswordChanges.AsNoTracking()
                .Where(c => c.UserId == id && c.ChangeType != PasswordChange.ResetRequested)
                .MaxAsync(c => (DateTime?)c.ChangedAt);

            return Results.Ok(new SecuritySummary(last?.AttemptedAt, last?.IpAddress, previous?.AttemptedAt, failedSincePrevious, recent, passwordChangedAt));
        });

        app.MapPost("/users/{id:int}/username", async (int id, ChangeUsernameRequest req, AppDbContext db, IEmailSender email) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null)
                return Results.NotFound();
            // sso-only accounts have no password yet, so there's nothing to confirm
            if (user.Password is not null && !Passwords.Verify(user, req.CurrentPassword))
                return Results.Json(new { message = "Current password is wrong." }, statusCode: StatusCodes.Status401Unauthorized);

            var username = req.NewUsername?.Trim() ?? "";
            if (username.Length is 0 or > MaxUsernameLength)
                return Results.BadRequest(new { message = $"Usernames are 1 to {MaxUsernameLength} characters." });
            if (username == user.Username)
                return Results.BadRequest(new { message = "That is already your username." });
            if (await db.Users.AnyAsync(u => u.Username == username && u.Id != id))
                return Results.Conflict(new { message = "That username is already taken." });

            var old = user.Username;
            user.Username = username;
            await db.SaveChangesAsync();
            await email.SendAsync(user.Email, "Your TruckerReward username was changed",
                $"Hi {username},\n\nYour username was just changed from {old} to {username}. If that wasn't you, reset your password right away from the login page.");
            return Results.Ok(UserProfile.Of(user));
        });

        app.MapPost("/users/{id:int}/email", async (int id, ChangeEmailRequest req, AppDbContext db, IEmailSender email) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null)
                return Results.NotFound();
            if (user.Password is not null && !Passwords.Verify(user, req.CurrentPassword))
                return Results.Json(new { message = "Current password is wrong." }, statusCode: StatusCodes.Status401Unauthorized);

            var address = req.NewEmail?.Trim() ?? "";
            if (address.Length is 0 or > 255 || !System.Net.Mail.MailAddress.TryCreate(address, out var parsed) || parsed.Address != address)
                return Results.BadRequest(new { message = "Enter a valid email address of 255 characters or fewer." });
            if (string.Equals(address, user.Email, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "That is already your email address." });
            if (await db.Users.AnyAsync(u => u.Email == address && u.Id != id))
                return Results.Conflict(new { message = "That email address is already in use." });

            var oldAddress = user.Email;
            user.Email = address;
            await db.SaveChangesAsync();
            await email.SendAsync(oldAddress, "Your TruckerReward email was changed",
                $"Hi {user.Username},\n\nYour account email was just changed from {oldAddress} to {address}. If that wasn't you, reset your password right away from the login page.");
            return Results.Ok(UserProfile.Of(user));
        });

        app.MapPut("/users/{id:int}/profile", async (int id, UpdateUserProfileRequest req, AppDbContext db) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null)
                return Results.NotFound();

            var firstName = req.FirstName?.Trim() ?? "";
            var lastName = req.LastName?.Trim() ?? "";
            var phoneNumber = req.PhoneNumber?.Trim() ?? "";
            var address = req.Address?.Trim() ?? "";
            if (firstName.Length is 0 or > 100 || lastName.Length is 0 or > 100)
                return Results.BadRequest(new { message = "First and last names are required and must be 100 characters or fewer." });
            if (phoneNumber.Length > 20 || address.Length > 255)
                return Results.BadRequest(new { message = "Phone number must be 20 characters or fewer and address must be 255 characters or fewer." });

            user.FirstName = firstName;
            user.LastName = lastName;
            user.PhoneNumber = phoneNumber;
            user.Address = address;
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }

    // users.username is varchar(255)
    private const int MaxUsernameLength = 255;

    internal static async Task AddCompanyMembership(AppDbContext db, User user, int companyId)
    {
        var alreadyLinked = await db.AccountsCompanies.AnyAsync(link => link.CompanyId == companyId && link.AccountId == user.Id);
        if (alreadyLinked)
            return;

        var hasCurrentCompany = await db.AccountsCompanies.AnyAsync(link => link.AccountId == user.Id && link.CurrentCompanyViewed);
        db.AccountsCompanies.Add(new AccountsCompany
        {
            AccountId = user.Id,
            CompanyId = companyId,
            CurrentCompanyViewed = !hasCurrentCompany
        });
    }
}

public record ChangeUsernameRequest(string? CurrentPassword, string? NewUsername);
public record ChangeEmailRequest(string? CurrentPassword, string? NewEmail);
public record UpdateUserProfileRequest(string? FirstName, string? LastName, string? PhoneNumber, string? Address);
public record CreateApplicationRequest(int CompanyId, string? ApplicantExtraInfo);
public record CancelApplicationRequest(string? Reasoning);
public record ApplicationListingData(int Id, int ApplicantId, int CompanyId, string ApplicantName, string ApplicantEmail, string? ApplicantExtraInfo, string CompanyName, string Status, int? ReviewerId, string? ReviewerReasoning, DateTime ApplicationTimestamp, DateTime? ResponseTimestamp);
public record ApplicationListing(int Id, int ApplicantId, int CompanyId, string ApplicantName, string ApplicantEmail, string? ApplicantExtraInfo, string CompanyName, string Status, string? ReviewerUserType, string? ReviewerName, string? ReviewerEmail, string? ReviewerReasoning, bool CanRevert, DateTime ApplicationTimestamp, DateTime? ResponseTimestamp);
public record ReviewApplicationRequest(string? Decision, string? Reasoning);

public record UserDetails(int Id, string UserType, string Username, string FirstName, string LastName, string Email, string PhoneNumber, string Address, string? CompanyName, List<string> CompanyNames, List<UserCompany> Companies, int? CompanyId, long Points, string? PendingCompanyName, decimal? MonthlyBudget);

public record UserCompany(int Id, string Name, bool CurrentCompanyViewed);

public record CompanyOption(int Id, string Name, string? Description);

public record LoginEvent(DateTime AttemptedAt, bool Succeeded, string? IpAddress);

public record SecuritySummary(
    DateTime? LastLoginAt,
    string? LastLoginIp,
    DateTime? PreviousLoginAt,
    int FailedSincePreviousLogin,
    List<LoginEvent> Recent,
    DateTime? PasswordChangedAt);
