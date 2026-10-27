using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AdminAccountsTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private readonly DateTime t0 = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private async Task Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var users = new[]
        {
            User("bob", "Bob", "Baker", "driver"),
            User("amy", "Amy", "Zane", "driver", BackEnd.Models.User.Inactive),
            User("sue", "Sue", "Moss", "sponsor"),
            User("ann", "Ann", "Admin", "admin")
        };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        // stand-in for the live users insert trigger
        for (var i = 0; i < users.Length; i++)
            db.AccountsHistories.Add(History(users[i], t0.AddDays(i)));
        db.AccountsHistories.Add(History(users[0], t0.AddDays(10)));
        db.LoginAttempts.AddRange(
            new LoginAttempt { Username = "bob", UserId = users[0].Id, Succeeded = true, IpAddress = "", AttemptedAt = t0.AddDays(5) },
            new LoginAttempt { Username = "bob", UserId = users[0].Id, Succeeded = false, IpAddress = "", AttemptedAt = t0.AddDays(9) },
            new LoginAttempt { Username = "sue", UserId = users[2].Id, Succeeded = true, IpAddress = "", AttemptedAt = t0.AddDays(7) });
        await db.SaveChangesAsync();
    }

    private static User User(string name, string first, string last, string type, string status = "active") => new()
    {
        Username = name, FirstName = first, LastName = last, Email = $"{name}@example.com",
        UserType = type, PhoneNumber = "", Address = "", Password = "x", Status = status
    };

    private static AccountsHistory History(User u, DateTime at) => new()
    {
        UserId = u.Id, Timestamp = at, UserType = u.UserType, Username = u.Username, Password = "x", Email = u.Email,
        FirstName = u.FirstName, LastName = u.LastName
    };

    private async Task<List<AdminAccount>> List(string query = "") =>
        (await client.GetFromJsonAsync<List<AdminAccount>>("/admin/accounts" + query))!;

    [Fact]
    public async Task ListsEveryAccountByNameWithCreatedAndLastLogin()
    {
        await Start();

        var all = await List();

        Assert.Equal(["ann", "bob", "sue", "amy"], all.Select(a => a.Username));
        var bob = all.Single(a => a.Username == "bob");
        Assert.Equal(t0, bob.CreatedAt);
        Assert.Equal(t0.AddDays(5), bob.LastLoginAt);
        Assert.Null(all.Single(a => a.Username == "amy").LastLoginAt);
        Assert.Equal("inactive", all.Single(a => a.Username == "amy").Status);
    }

    [Theory]
    [InlineData("?search=BAKER", "bob")]
    [InlineData("?search=bob baker", "bob")]
    [InlineData("?search=sue@example", "sue")]
    [InlineData("?search=amy", "amy")]
    [InlineData("?type=sponsor", "sue")]
    [InlineData("?status=inactive", "amy")]
    [InlineData("?type=driver&status=active", "bob")]
    public async Task SearchesAndFilters(string query, string expected)
    {
        await Start();
        Assert.Equal(expected, Assert.Single(await List(query)).Username);
    }

    [Fact]
    public async Task SortsByCreatedOrLastLogin()
    {
        await Start();

        Assert.Equal(["ann", "sue", "amy", "bob"], (await List("?sort=created")).Select(a => a.Username));
        Assert.Equal(["sue", "bob"], (await List("?sort=last-login")).Select(a => a.Username).Take(2));
    }

    [Fact]
    public async Task CountsActiveAccountsByType()
    {
        await Start();

        var counts = await client.GetFromJsonAsync<AccountCounts>("/admin/accounts/counts");

        Assert.Equal(new AccountCounts(1, 1, 1, 1), counts);
    }

    [Fact]
    public async Task FiltersBySponsorCompany()
    {
        await Start();
        int acme;
        using (var db = app.Db())
        {
            var company = new Company { Name = "Acme" };
            db.Add(company);
            await db.SaveChangesAsync();
            acme = company.Id;
            var ids = await db.Users.Where(u => u.Username == "bob" || u.Username == "sue").Select(u => u.Id).ToListAsync();
            db.AccountsCompanies.AddRange(ids.Select(id => new AccountsCompany { AccountId = id, CompanyId = acme }));
            await db.SaveChangesAsync();
        }

        Assert.Equal(["bob", "sue"], (await List($"?companyId={acme}")).Select(a => a.Username).Order());
        Assert.Equal("bob", Assert.Single(await List($"?companyId={acme}&type=driver")).Username);
    }

    [Fact]
    public async Task FindsDriversAndSponsorsWithoutASponsor()
    {
        await Start();
        using (var db = app.Db())
        {
            var company = new Company { Name = "Acme" };
            db.Add(company);
            await db.SaveChangesAsync();
            var bobId = await db.Users.Where(u => u.Username == "bob").Select(u => u.Id).SingleAsync();
            db.AccountsCompanies.Add(new AccountsCompany { AccountId = bobId, CompanyId = company.Id });
            await db.SaveChangesAsync();
        }

        Assert.Equal(["amy", "sue"], (await List("?noSponsor=true")).Select(a => a.Username).Order());
    }

    private async Task<int> Id(string username)
    {
        using var db = app.Db();
        return await db.Users.Where(u => u.Username == username).Select(u => u.Id).SingleAsync();
    }

    [Fact]
    public async Task AdminEditsAnyAccount()
    {
        await Start();
        int acme;
        using (var db = app.Db())
        {
            var company = new Company { Name = "Acme" };
            db.Add(company);
            await db.SaveChangesAsync();
            acme = company.Id;
        }
        var (ann, bob) = (await Id("ann"), await Id("bob"));

        var response = await client.PutAsJsonAsync($"/admin/accounts/{bob}", new
        {
            adminId = ann, firstName = " Robert ", lastName = "Baker", email = "rob@example.com", phoneNumber = "555", address = "1 Main St", companyId = acme
        });

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        using var check = app.Db();
        var saved = await check.Users.SingleAsync(u => u.Id == bob);
        Assert.Equal(("Robert", "rob@example.com", "555", "1 Main St", (int?)acme),
            (saved.FirstName, saved.Email, saved.PhoneNumber, saved.Address,
                await check.AccountsCompanies.Where(membership => membership.AccountId == bob).Select(membership => (int?)membership.CompanyId).FirstOrDefaultAsync()));
        Assert.Equal("bob@example.com", Assert.Single(app.Email.Sent).To);
    }

    [Theory]
    [InlineData("sue", "bob", "Bob", "bob@example.com", 403)]
    [InlineData("ann", "bob", "", "bob@example.com", 400)]
    [InlineData("ann", "bob", "Bob", "not an email", 400)]
    [InlineData("ann", "bob", "Bob", "sue@example.com", 409)]
    public async Task AdminEditsAreChecked(string actor, string target, string first, string email, int status)
    {
        await Start();

        var response = await client.PutAsJsonAsync($"/admin/accounts/{await Id(target)}", new
        {
            adminId = await Id(actor), firstName = first, lastName = "Baker", email, phoneNumber = "", address = ""
        });

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task AdminsCantBeGivenACompany()
    {
        await Start();
        var ann = await Id("ann");

        var response = await client.PutAsJsonAsync($"/admin/accounts/{ann}", new
        {
            adminId = ann, firstName = "Ann", lastName = "Admin", email = "ann@example.com", companyId = 999
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminCreatesAnAccountThatSetsItsOwnPassword()
    {
        await Start();
        var ann = await Id("ann");

        var response = await client.PostAsJsonAsync("/admin/accounts", new
        {
            adminId = ann, userType = "admin", username = " kim ", firstName = "Kim", lastName = "Lee", email = "kim@example.com"
        });

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        using (var db = app.Db())
        {
            var kim = await db.Users.SingleAsync(u => u.Username == "kim");
            Assert.Equal(("admin", null, BackEnd.Models.User.Active), (kim.UserType, kim.Password, kim.Status));
        }
        var invite = Assert.Single(app.Email.Sent, m => m.To == "kim@example.com");
        var token = System.Text.RegularExpressions.Regex.Match(invite.Body, "token=([A-Za-z0-9_-]+)").Groups[1].Value;

        app.Clock.Advance(TimeSpan.FromDays(2));
        var reset = await client.PostAsJsonAsync("/auth/reset", new { token, newPassword = "Correct-Horse-9", ipAddress = "" });

        Assert.Equal(System.Net.HttpStatusCode.NoContent, reset.StatusCode);
    }

    [Theory]
    [InlineData("sue", "driver", "kim", "kim@example.com", 403)]
    [InlineData("ann", "boss", "kim", "kim@example.com", 400)]
    [InlineData("ann", "driver", "bob", "kim@example.com", 409)]
    [InlineData("ann", "driver", "kim", "bob@example.com", 409)]
    [InlineData("ann", "driver", "kim", "nope", 400)]
    public async Task AdminCreatesAreChecked(string actor, string type, string username, string email, int status)
    {
        await Start();

        var response = await client.PostAsJsonAsync("/admin/accounts", new
        {
            adminId = await Id(actor), userType = type, username, firstName = "Kim", lastName = "Lee", email
        });

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task AdminDeletesAnAccountWithNoHistory()
    {
        await Start();
        var (bob, ann) = (await Id("bob"), await Id("ann"));
        using (var db = app.Db())
        {
            var acme = new Company { Name = "Acme" };
            db.Add(acme);
            await db.SaveChangesAsync();
            db.AccountsCompanies.Add(new AccountsCompany { AccountId = bob, CompanyId = acme.Id });
            db.Applications.Add(new Application { ApplicantId = bob, CompanyId = acme.Id, Status = "approved", ApplicationTimestamp = t0 });
            db.NotificationsHistories.Add(new NotificationsHistory { UserId = bob, Subject = "hi", Body = "hi", Timestamp = t0 });
            db.CartItems.Add(new CartItem { UserId = bob, Name = "Mug", Price = 1m, InCart = true });
            await db.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/admin/accounts/{bob}?adminId={ann}");

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        using var after = app.Db();
        Assert.False(await after.Users.AnyAsync(u => u.Id == bob));
        Assert.False(await after.AccountsHistories.AnyAsync(h => h.UserId == bob));
        Assert.Equal(2, await after.LoginAttempts.CountAsync(a => a.Username == "bob" && a.UserId == null));
    }

    [Theory]
    [InlineData("sue", "bob", 403)]
    [InlineData("ann", "ann", 400)]
    [InlineData("ann", "nobody", 404)]
    [InlineData("ann", "amy", 409)]
    public async Task AdminDeletesAreChecked(string actor, string target, int status)
    {
        await Start();
        using (var db = app.Db())
        {
            db.PointsHistory.Add(new PointsHistory { UserId = await Id("amy"), PointsDelta = 5, Timestamp = t0 });
            await db.SaveChangesAsync();
        }
        var targetId = target == "nobody" ? 999 : await Id(target);

        var response = await client.DeleteAsync($"/admin/accounts/{targetId}?adminId={await Id(actor)}");

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(4, await app.Db().Users.CountAsync());
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
