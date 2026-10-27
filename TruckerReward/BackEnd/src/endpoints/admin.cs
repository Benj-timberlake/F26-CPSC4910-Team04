using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// admin pages: lockouts, sign-in attempts, password changes and account status
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/locked-accounts", async (AppDbContext db, TimeProvider clock) =>
            Results.Ok(await Lockout.All(db, clock.GetUtcNow().UtcDateTime)));

        app.MapGet("/admin/login-attempts", async (bool failedOnly, int? limit, AppDbContext db) =>
        {
            var query = db.LoginAttempts.AsNoTracking();
            if (failedOnly)
                query = query.Where(a => !a.Succeeded);
            return Results.Ok(await query
                .OrderByDescending(a => a.AttemptedAt).ThenByDescending(a => a.Id)
                .Take(Limit(limit))
                .ToListAsync());
        });

        app.MapGet("/admin/password-changes", async (int? limit, AppDbContext db) =>
            Results.Ok(await db.PasswordChanges.AsNoTracking()
                .OrderByDescending(c => c.ChangedAt).ThenByDescending(c => c.Id)
                .Take(Limit(limit))
                .Join(db.Users, c => c.UserId, u => u.Id,
                    (c, u) => new PasswordChangeEntry(c.ChangedAt, u.Id, u.Username, c.ChangeType, c.IpAddress))
                .ToListAsync()));

        app.MapGet("/admin/accounts/counts", async (AppDbContext db) =>
        {
            var active = db.Users.AsNoTracking().Where(u => u.Status == User.Active);
            return Results.Ok(new AccountCounts(
                await active.CountAsync(u => u.UserType == AuthEndpoints.Driver),
                await active.CountAsync(u => u.UserType == AuthEndpoints.Sponsor),
                await active.CountAsync(u => u.UserType == AuthEndpoints.Admin),
                await db.Users.CountAsync(u => u.Status == User.Inactive),
                await active.CountAsync(u => u.UserType == AuthEndpoints.Driver &&
                    !db.AccountsCompanies.Any(membership => membership.AccountId == u.Id))));
        });

        // sort is name, created or last-login
        app.MapGet("/admin/accounts", async (string? search, string? type, string? status, string? sort, int? companyId, bool? noSponsor, AppDbContext db) =>
        {
            var users = db.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                users = users.Where(u => u.Username.ToLower().Contains(term) || u.Email.ToLower().Contains(term)
                    || (u.FirstName + " " + u.LastName).ToLower().Contains(term));
            }
            if (!string.IsNullOrWhiteSpace(type))
                users = users.Where(u => u.UserType == type);
            if (!string.IsNullOrWhiteSpace(status))
                users = users.Where(u => u.Status == status);
            if (companyId is not null)
                users = users.Where(u => db.AccountsCompanies
                    .Any(membership => membership.AccountId == u.Id && membership.CompanyId == companyId));
            if (noSponsor == true)
                users = users.Where(u => u.UserType != AuthEndpoints.Admin &&
                    !db.AccountsCompanies.Any(membership => membership.AccountId == u.Id));

            var rows = users.Select(u => new
            {
                User = u,
                // the users insert trigger writes the first accounts_history row
                CreatedAt = db.AccountsHistories.Where(h => h.UserId == u.Id).Min(h => (DateTime?)h.Timestamp),
                LastLoginAt = db.LoginAttempts.Where(a => a.UserId == u.Id && a.Succeeded).Max(a => (DateTime?)a.AttemptedAt)
            });
            rows = sort switch
            {
                "created" => rows.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.User.Username),
                "last-login" => rows.OrderByDescending(r => r.LastLoginAt).ThenBy(r => r.User.Username),
                _ => rows.OrderBy(r => r.User.LastName).ThenBy(r => r.User.FirstName).ThenBy(r => r.User.Username)
            };
            return Results.Ok(await rows.Take(MaxAccounts)
                .Select(r => new AdminAccount(r.User.Id, r.User.Username, r.User.FirstName, r.User.LastName, r.User.Email,
                    r.User.UserType, r.User.Status, r.CreatedAt, r.LastLoginAt))
                .ToListAsync());
        });

        app.MapGet("/users/{id:int}/status", async (int id, AppDbContext db) =>
            await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Status).FirstOrDefaultAsync() is { } status
                ? Results.Ok(new AccountStatus(status))
                : Results.NotFound());

        app.MapPost("/admin/accounts/{id:int}/status", async (int id, ChangeAccountStatusRequest req, AppDbContext db, Notifications notifications) =>
        {
            if (req.Status is not (User.Active or User.Inactive))
                return Results.BadRequest(new { message = "Status must be active or inactive." });
            var admin = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.AdminId && u.UserType == AuthEndpoints.Admin);
            if (admin is null)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (admin.Id == id)
                return Results.BadRequest(new { message = "You can't change the status of your own account." });
            var account = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (account is null)
                return Results.NotFound();
            if (account.Status == req.Status)
                return Results.Conflict(new { message = $"That account is already {req.Status}." });

            account.Status = req.Status;
            await db.SaveChangesAsync();
            await notifications.AccountStatusChanged(account, admin);
            return Results.NoContent();
        });

        app.MapPost("/admin/accounts", CreateAccount);
        // username, password and type have their own flows
        app.MapPut("/admin/accounts/{id:int}", EditAccount);
        app.MapDelete("/admin/accounts/{id:int}", DeleteAccount);
    }

    private static async Task<IResult> CreateAccount(CreateAccountRequest req, AppDbContext db, Notifications notifications, TimeProvider clock, IConfiguration config)
    {
        if (!await IsAdmin(db, req.AdminId))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (req.UserType is not (AuthEndpoints.Driver or AuthEndpoints.Sponsor or AuthEndpoints.Admin))
            return Results.BadRequest(new { message = "Account type must be driver, sponsor or admin." });
        if (req.UserType == AuthEndpoints.Driver && req.CompanyId is null)
            return Results.BadRequest(new { message = "Pick a sponsor for the driver." });
        return await Invite(db, notifications, clock, config, "An administrator", req.UserType, req.Username, req.FirstName, req.LastName, req.Email, req.CompanyId);
    }

    // the new user sets a password from the emailed link
    internal static async Task<IResult> Invite(AppDbContext db, Notifications notifications, TimeProvider clock, IConfiguration config,
        string invitedBy, string userType, string? usernameIn, string? firstNameIn, string? lastNameIn, string? emailIn, int? companyId)
    {
        var username = usernameIn?.Trim() ?? "";
        var (firstName, lastName, address) = (Trim(firstNameIn), Trim(lastNameIn), Trim(emailIn));
        if (username.Length is 0 or > 255)
            return Results.BadRequest(new { message = "Usernames are 1 to 255 characters." });
        if ((DetailsProblem(firstName, lastName, address) ?? await CompanyProblem(db, userType, companyId)) is { } problem)
            return Results.BadRequest(new { message = problem });
        if (await db.Users.AnyAsync(u => u.Username == username))
            return Results.Conflict(new { message = "That username is already taken." });
        if (await db.Users.AnyAsync(u => u.Email == address))
            return Results.Conflict(new { message = "An account with that email already exists." });

        var user = new User
        {
            UserType = userType, Username = username, FirstName = firstName, LastName = lastName,
            Email = address, PhoneNumber = "", Address = ""
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        if (companyId is int assignedCompanyId)
        {
            await UserEndpoints.AddCompanyMembership(db, user, assignedCompanyId);
            await db.SaveChangesAsync();
        }
        var link = PasswordEndpoints.ResetLink(db, user, clock.GetUtcNow().UtcDateTime + InviteLifetime, config);
        await db.SaveChangesAsync();
        await notifications.AccountInvited(user, link, invitedBy);
        await notifications.AccountCreated(user);
        return Results.Created($"/users/{user.Id}", UserProfile.Of(user));
    }

    private static async Task<IResult> EditAccount(int id, EditAccountRequest req, AppDbContext db, IEmailSender email)
    {
        if (!await IsAdmin(db, req.AdminId))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var account = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (account is null)
            return Results.NotFound();

        var (firstName, lastName, address) = (Trim(req.FirstName), Trim(req.LastName), Trim(req.Email));
        var (phone, street) = (Trim(req.PhoneNumber), Trim(req.Address));
        if (DetailsProblem(firstName, lastName, address) is { } problem)
            return Results.BadRequest(new { message = problem });
        if (phone.Length > 20 || street.Length > 255)
            return Results.BadRequest(new { message = "Phone number must be 20 characters or fewer and address must be 255 characters or fewer." });
        if (await db.Users.AnyAsync(u => u.Email == address && u.Id != id))
            return Results.Conflict(new { message = "That email address is already in use." });
        if (await CompanyProblem(db, account.UserType, req.CompanyId) is { } companyProblem)
            return Results.BadRequest(new { message = companyProblem });

        var oldAddress = account.Email;
        account.FirstName = firstName;
        account.LastName = lastName;
        account.Email = address;
        account.PhoneNumber = phone;
        account.Address = street;
        await db.SaveChangesAsync();
        if (account.UserType == AuthEndpoints.Sponsor)
        {
            await db.AccountsCompanies.Where(membership => membership.AccountId == account.Id).ExecuteDeleteAsync();
            if (req.CompanyId is int companyId)
            {
                await UserEndpoints.AddCompanyMembership(db, account, companyId);
                await db.SaveChangesAsync();
            }
        }
        else if (account.UserType == AuthEndpoints.Driver && req.CompanyId is int driverCompanyId)
        {
            await UserEndpoints.AddCompanyMembership(db, account, driverCompanyId);
            await db.SaveChangesAsync();
        }
        if (!string.Equals(oldAddress, address, StringComparison.OrdinalIgnoreCase))
            await email.SendAsync(oldAddress, "Your TruckerReward email was changed",
                $"Hi {account.Username},\n\nAn administrator changed your account email from {oldAddress} to {address}. If that's wrong, contact an administrator.");
        return Results.NoContent();
    }

    // accounts with history get deactivated instead
    private static async Task<IResult> DeleteAccount(int id, int adminId, AppDbContext db)
    {
        if (!await IsAdmin(db, adminId))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (adminId == id)
            return Results.BadRequest(new { message = "You can't delete your own account." });
        var account = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (account is null)
            return Results.NotFound();
        if (await db.PointsHistory.AnyAsync(h => h.UserId == id) || await db.AuditHistories.AnyAsync(a => a.UserId == id) ||
            await db.Applications.AnyAsync(a => a.ReviewerId == id))
            return Results.Conflict(new { message = $"{account.Username} has history on record, so deactivate the account instead." });

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.LoginAttempts.Where(a => a.UserId == id).ExecuteUpdateAsync(u => u.SetProperty(a => a.UserId, (int?)null));
            await db.Applications.Where(a => a.ApplicantId == id).ExecuteDeleteAsync();
            await db.CartItems.Where(c => c.UserId == id).ExecuteDeleteAsync();
            await db.NotificationsHistories.Where(n => n.UserId == id).ExecuteDeleteAsync();
            await db.NotificationPreferences.Where(n => n.UserId == id).ExecuteDeleteAsync();
            await db.PasswordChanges.Where(c => c.UserId == id).ExecuteDeleteAsync();
            await db.PasswordResets.Where(r => r.UserId == id).ExecuteDeleteAsync();
            await db.PasskeyCredentials.Where(c => c.UserId == id).ExecuteDeleteAsync();
            await db.Impersonations.Where(i => i.UserId == id || i.ViewerId == id).ExecuteDeleteAsync();
            await db.AccountsCompanies.Where(m => m.AccountId == id).ExecuteDeleteAsync();
            await db.AccountsHistories.Where(h => h.UserId == id).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
        catch (System.Data.Common.DbException)
        {
            return Results.Conflict(new { message = $"{account.Username} couldn't be deleted, so deactivate the account instead." });
        }
        return Results.NoContent();
    }

    private static Task<bool> IsAdmin(AppDbContext db, int id) =>
        db.Users.AnyAsync(u => u.Id == id && u.UserType == AuthEndpoints.Admin);

    private static string Trim(string? value) => value?.Trim() ?? "";

    private static string? DetailsProblem(string firstName, string lastName, string email)
    {
        if (firstName.Length is 0 or > 100 || lastName.Length is 0 or > 100)
            return "First and last names are required and must be 100 characters or fewer.";
        if (email.Length is 0 or > 255 || !System.Net.Mail.MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            return "Enter a valid email address of 255 characters or fewer.";
        return null;
    }

    private static async Task<string?> CompanyProblem(AppDbContext db, string? userType, int? companyId) =>
        companyId is not null && (userType == AuthEndpoints.Admin || !await db.Companies.AnyAsync(c => c.Id == companyId))
            ? "Pick an existing sponsor, and only for drivers and sponsors."
            : null;

    public const int MaxAccounts = 500;
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(3);

    // lists take an optional limit, capped so nobody asks for the whole table
    private static int Limit(int? requested) => requested is > 0 and <= 500 ? requested.Value : 100;
}

public record AdminAccount(int Id, string Username, string FirstName, string LastName, string Email, string UserType, string Status, DateTime? CreatedAt, DateTime? LastLoginAt);

public record AccountCounts(int ActiveDrivers, int ActiveSponsors, int ActiveAdmins, int Inactive, int DriversWithoutSponsor);

public record AccountStatus(string Status);

public record CreateAccountRequest(int AdminId, string? UserType, string? Username, string? FirstName, string? LastName, string? Email, int? CompanyId);

public record EditAccountRequest(int AdminId, string? FirstName, string? LastName, string? Email, string? PhoneNumber, string? Address, int? CompanyId);

public record ChangeAccountStatusRequest(int AdminId, string? Status);

public record PasswordChangeEntry(DateTime ChangedAt, int UserId, string Username, string ChangeType, string? IpAddress);
