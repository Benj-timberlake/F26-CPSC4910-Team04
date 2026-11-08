using System.Security.Cryptography;
using System.Text;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

public static class MassAccountCreationEndpoints
{
    private const int MaxFileBytes = 1_000_000;
    private const int MaxAccountsPerFile = 100;

    public static void MapMassAccountCreationEndpoints(this WebApplication app)
    {
        app.MapPost("/users/{id:int}/mass-accounts", CreateAccounts);
    }

    private static async Task<IResult> CreateAccounts(
        int id, MassAccountCreationRequest request, AppDbContext db, Notifications notifications)
    {
        var actor = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == id && user.Status == User.Active);
        if (actor is null || actor.UserType is not (AuthEndpoints.Admin or AuthEndpoints.Sponsor))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var sponsorCompanyId = actor.UserType == AuthEndpoints.Sponsor
            ? await CompanyMembershipQueries.FirstCompanyId(db, actor.Id)
            : null;
        if (actor.UserType == AuthEndpoints.Sponsor && sponsorCompanyId is null)
            return Results.BadRequest(new { message = "You must belong to an organization to add users as a sponsor." });

        if (string.IsNullOrWhiteSpace(request.Content))
            return Results.BadRequest(new { message = "Choose a non-empty text file." });
        if (request.Content.Length > MaxFileBytes || Encoding.UTF8.GetByteCount(request.Content) > MaxFileBytes)
            return Results.BadRequest(new { message = "The text file must be no larger than 1 MB." });

        var rows = new List<MassAccountRow>();
        var errors = new List<string>();
        var lines = request.Content.Split('\n');
        var accountLines = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;
            accountLines++;
            if (accountLines > MaxAccountsPerFile)
            {
                errors.Add($"Line {index + 1}: A file can contain at most {MaxAccountsPerFile} accounts.");
                break;
            }

            var fields = line.Split('|', 7);
            if (fields.Length is < 5 or > 7)
            {
                errors.Add($"Line {index + 1}: Expected 5 to 7 pipe-separated fields.");
                continue;
            }

            var type = fields[0].Trim() switch
            {
                "D" => AuthEndpoints.Driver,
                "S" => AuthEndpoints.Sponsor,
                _ => null
            };
            if (type is null)
            {
                errors.Add($"Line {index + 1}: Type must be D or S.");
                continue;
            }

            var organization = fields[1].Trim();
            var firstName = fields[2].Trim();
            var lastName = fields[3].Trim();
            var email = fields[4].Trim();
            var pointsText = fields.Length > 5 ? fields[5].Trim() : "";
            var reason = fields.Length > 6 ? fields[6].Trim() : "";
            var validRow = true;
            if (actor.UserType == AuthEndpoints.Admin && organization.Length == 0)
            {
                errors.Add($"Line {index + 1}: Organization name is required.");
                validRow = false;
            }
            if (actor.UserType == AuthEndpoints.Sponsor && organization.Length != 0)
            {
                errors.Add($"Line {index + 1}: Leave organization blank when uploading as a sponsor; your organization will be used.");
                validRow = false;
            }
            if (firstName.Length is 0 or > 100 || lastName.Length is 0 or > 100)
            {
                errors.Add($"Line {index + 1}: First and last names are required and must be 100 characters or fewer.");
                validRow = false;
            }
            var validEmail = email.Length is > 0 and <= 255
                && email.Contains('@')
                && System.Net.Mail.MailAddress.TryCreate(email, out var parsedEmail)
                && string.Equals(parsedEmail.Address, email, StringComparison.OrdinalIgnoreCase);
            if (!validEmail)
            {
                errors.Add($"Line {index + 1}: Enter a valid email address of 255 characters or fewer.");
                validRow = false;
            }
            var hasPoints = pointsText.Length > 0;
            var validPoints = !hasPoints || (int.TryParse(pointsText, out var points) && points >= 0);
            if (!validPoints)
            {
                errors.Add($"Line {index + 1}: Points must be a non-negative whole number when provided.");
                validRow = false;
            }
            if (hasPoints && reason.Length == 0)
            {
                errors.Add($"Line {index + 1}: A reason for points is required.");
                validRow = false;
            }

            if (!validRow)
                continue;

            var username = email[..email.IndexOf('@')];
            if (username.Length is 0 or > 255)
            {
                errors.Add($"Line {index + 1}: The email address must have a username of 1 to 255 characters.");
                continue;
            }
            rows.Add(new MassAccountRow(index + 1, type, username, firstName, lastName, email));
        }

        if (rows.Count == 0 && errors.Count == 0)
            errors.Add("The text file does not contain any account rows.");

        var duplicateRows = new HashSet<int>();
        foreach (var group in rows.GroupBy(row => row.Username, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            duplicateRows.UnionWith(group.Select(row => row.Line));
        foreach (var group in rows.GroupBy(row => row.Email, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            duplicateRows.UnionWith(group.Select(row => row.Line));
        foreach (var row in rows.Where(row => duplicateRows.Contains(row.Line)))
            errors.Add($"Line {row.Line}: This username or email appears more than once in the file.");

        var usernames = rows.Select(row => row.Username).ToArray();
        var emails = rows.Select(row => row.Email).ToArray();
        var normalizedUsernames = usernames.Select(username => username.ToLowerInvariant()).ToArray();
        var normalizedEmails = emails.Select(email => email.ToLowerInvariant()).ToArray();
        var existingAccounts = await db.Users.AsNoTracking()
            .Where(user => normalizedUsernames.Contains(user.Username.ToLower()) || normalizedEmails.Contains(user.Email.ToLower()))
            .Select(user => new { user.Username, user.Email })
            .ToListAsync();
        foreach (var row in rows)
        {
            if (existingAccounts.Any(user => string.Equals(user.Username, row.Username, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Line {row.Line}: The username {row.Username} is already taken.");
            if (existingAccounts.Any(user => string.Equals(user.Email, row.Email, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Line {row.Line}: An account with {row.Email} already exists.");
        }

        if (errors.Count > 0)
            return Results.BadRequest(new { message = string.Join("\n", errors) });

        var created = rows.Select(row =>
        {
            var user = new User
            {
                UserType = row.UserType,
                Username = row.Username,
                FirstName = row.FirstName,
                LastName = row.LastName,
                Email = row.Email,
                PhoneNumber = "",
                Address = ""
            };
            var password = GeneratePassword();
            user.Password = Passwords.Hash(user, password);
            return (User: user, Password: password);
        }).ToList();

        if (sponsorCompanyId is int companyId)
        {
            foreach (var account in created)
                account.User.AccountsCompanies.Add(new AccountsCompany { CompanyId = companyId, CurrentCompanyViewed = true });
        }
        db.Users.AddRange(created.Select(account => account.User));
        await db.SaveChangesAsync();

        foreach (var account in created)
        {
            await notifications.MassAccountCreated(account.User, account.Password);
            await notifications.AccountCreated(account.User);
        }

        return Results.Ok(new { message = $"Added {created.Count} account{(created.Count == 1 ? "" : "s")}." });
    }

    private static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!#$%&*+-=?";
        const string all = upper + lower + digits + special;
        var password = new char[20];
        password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        password[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        password[3] = special[RandomNumberGenerator.GetInt32(special.Length)];
        for (var i = 4; i < password.Length; i++)
            password[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        for (var i = password.Length - 1; i > 0; i--)
        {
            var swap = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[swap]) = (password[swap], password[i]);
        }
        return new string(password);
    }

    private sealed record MassAccountRow(int Line, string UserType, string Username, string FirstName, string LastName, string Email);
}

public sealed record MassAccountCreationRequest(string? Content);
