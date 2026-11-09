using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class AccountDataTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob, root, acme;
    private DateTime t0;

    private const string Password = "Hunter22x!";

    private async Task Start()
    {
        client = await app.StartWithBobAndRoot();
        t0 = app.Clock.GetUtcNow().UtcDateTime;
        using var db = app.Db();
        bob = (await db.Users.SingleAsync(u => u.Username == "bob")).Id;
        root = (await db.Users.SingleAsync(u => u.Username == "root")).Id;
        var company = new Company { Name = "Acme" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        acme = company.Id;
        db.AccountsCompanies.Add(new AccountsCompany { AccountId = bob, CompanyId = acme, CurrentCompanyViewed = true });
        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> Delete(int id, string? confirmation) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/users/{id}/account") { Content = JsonContent.Create(new { confirmation }) });

    private async Task AddPointsAndOrder()
    {
        using var db = app.Db();
        var earned = new PointsHistory { UserId = bob, CompanyId = acme, PointsDelta = 300, Timestamp = t0 };
        var spent = new PointsHistory { UserId = bob, CompanyId = acme, PointsDelta = -100, Timestamp = t0.AddHours(1) };
        db.PointsHistory.AddRange(earned, spent);
        await db.SaveChangesAsync();
        db.AuditHistories.Add(new AuditHistory { UserId = root, PointsHistoryId = earned.Id, Resoning = "Safe week" });
        db.CartItems.Add(new CartItem { UserId = bob, Name = "Mug", Price = 1m, InCart = false, WasOrdered = true, PointsHistoryId = spent.Id });
        db.CartItems.Add(new CartItem { UserId = bob, Name = "Hat", Price = 2m, InCart = true });
        await db.SaveChangesAsync();
    }

    private async Task AddSecurityRows(int id, string name)
    {
        using var db = app.Db();
        db.UserSessions.Add(new UserSession { UserId = id, KeyHash = Tokens.Hash(name + "-key"), CreatedAt = t0, LastSeenAt = t0, IpAddress = "10.0.0.1", UserAgent = "Firefox/140" });
        db.PasskeyCredentials.Add(new PasskeyCredential { UserId = id, CredentialId = [1, 2, (byte)id], PublicKey = [9, 9, 9, 9, 9, 9, (byte)id], Name = name + "'s phone", CreatedAt = t0 });
        db.SignInCodes.Add(new SignInCode { UserId = id, ChallengeHash = Tokens.Hash(name + "-challenge"), CodeHash = Tokens.Hash(name + "-code"), CreatedAt = t0, ExpiresAt = t0.AddMinutes(10) });
        db.PasswordResets.Add(new PasswordReset { UserId = id, TokenHash = Tokens.Hash(name + "-reset"), ExpiresAt = t0.AddHours(1) });
        db.EmailConfirmations.Add(new EmailConfirmation { UserId = id, Email = name + "@example.com", TokenHash = Tokens.Hash(name + "-confirm"), ExpiresAt = t0.AddDays(1) });
        db.WishlistItems.Add(new WishlistItem { UserId = id, ItemId = name + "-item" });
        db.NotificationPreferences.Add(new NotificationPreference { UserId = id, Category = "points", Enabled = false });
        db.NotificationsHistories.Add(new NotificationsHistory { UserId = id, Subject = name + " note", Body = "hello", Timestamp = t0 });
        db.PasswordChanges.Add(new PasswordChange { UserId = id, ChangeType = PasswordChange.Changed, IpAddress = "10.0.0.2", ChangedAt = t0 });
        db.LoginAttempts.Add(new LoginAttempt { UserId = id, Username = name, IpAddress = "10.0.0.3", Succeeded = true, AttemptedAt = t0 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ExportHoldsTheUsersOwnDataAndNoSecrets()
    {
        await Start();
        await AddPointsAndOrder();
        await AddSecurityRows(bob, "bob");
        await AddSecurityRows(root, "root");
        await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = Password });
        using (var db = app.Db())
        {
            db.EarnedBadges.Add(new EarnedBadge { UserId = bob, AchievementId = 1, EarnedAt = t0 });
            db.Announcements.Add(new Announcement { CompanyId = acme, AuthorId = bob, Title = "Bob's news", Body = "hi", CreatedAt = t0 });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/users/{bob}/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("bob", json.GetProperty("profile").GetProperty("username").GetString());
        Assert.True(json.GetProperty("profile").GetProperty("hasPassword").GetBoolean());
        Assert.Equal("Acme", json.GetProperty("companies")[0].GetProperty("name").GetString());
        Assert.Equal(2, json.GetProperty("points").GetArrayLength());
        Assert.Contains(json.GetProperty("points").EnumerateArray(), p => p.GetProperty("reason").GetString() == "Safe week");
        Assert.Equal(2, json.GetProperty("orders").GetArrayLength());
        Assert.Equal("bob-item", json.GetProperty("wishlist")[0].GetString());
        Assert.Equal("bob note", json.GetProperty("notifications")[0].GetProperty("subject").GetString());
        Assert.Equal("points", json.GetProperty("notificationPreferences")[0].GetProperty("category").GetString());
        Assert.Equal("Firefox", json.GetProperty("sessions")[0].GetProperty("device").GetString());
        Assert.NotEqual(0, json.GetProperty("loginAttempts").GetArrayLength());
        Assert.Equal("bob's phone", json.GetProperty("passkeys")[0].GetProperty("name").GetString());
        Assert.Equal("First points", json.GetProperty("badges")[0].GetProperty("name").GetString());
        Assert.Equal("Bob's news", json.GetProperty("announcements")[0].GetProperty("title").GetString());

        Assert.DoesNotContain("root", raw);
        using var check = app.Db();
        var user = await check.Users.SingleAsync(u => u.Id == bob);
        var secrets = new List<string> { user.Password!, Tokens.Hash("bob-key"), Tokens.Hash("bob-challenge"), Tokens.Hash("bob-code"), Tokens.Hash("bob-reset"), Tokens.Hash("bob-confirm"),
            Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, (byte)bob }), Convert.ToBase64String(new byte[] { 1, 2, (byte)bob }) };
        Assert.All(secrets, s => Assert.DoesNotContain(s, raw));
        Assert.DoesNotContain("\"password\"", raw);
        Assert.DoesNotContain("hash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("publicKey", raw);
    }

    [Fact]
    public async Task ExportOfAMissingUserIsNotFound()
    {
        await Start();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/users/999/export")).StatusCode);
    }

    [Fact]
    public async Task AccountWithoutHistoryIsRemovedAlongWithRowsInNewerTables()
    {
        await Start();
        await AddSecurityRows(bob, "bob");
        int achievement, announcement, banner;
        using (var db = app.Db())
        {
            var goal = new Achievement { SponsorCompanyId = acme, Name = "Big week", Description = "x", Metric = Achievement.PointsEarned, Threshold = 5, CreatedBy = bob };
            var news = new Announcement { CompanyId = acme, AuthorId = bob, Title = "News", Body = "hi", CreatedAt = t0 };
            var notice = new SiteBanner { Message = "Down tonight", Level = "info", CreatedAt = t0, SetBy = bob };
            db.AddRange(goal, news, notice);
            await db.SaveChangesAsync();
            db.EarnedBadges.Add(new EarnedBadge { UserId = bob, AchievementId = goal.Id, EarnedAt = t0 });
            await db.SaveChangesAsync();
            (achievement, announcement, banner) = (goal.Id, news.Id, notice.Id);
        }

        var response = await Delete(bob, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"anonymized\":false", await response.Content.ReadAsStringAsync());
        using var after = app.Db();
        Assert.False(await after.Users.AnyAsync(u => u.Id == bob));
        Assert.False(await after.EarnedBadges.AnyAsync(b => b.UserId == bob));
        Assert.False(await after.PasskeyCredentials.AnyAsync(p => p.UserId == bob));
        Assert.Null((await after.Achievements.SingleAsync(a => a.Id == achievement)).CreatedBy);
        Assert.Null((await after.Announcements.SingleAsync(a => a.Id == announcement)).AuthorId);
        Assert.Null((await after.SiteBanners.SingleAsync(b => b.Id == banner)).SetBy);
        // no sign in row should still name bob
        Assert.DoesNotContain(await after.LoginAttempts.ToListAsync(), a => a.Username == "bob" || a.IpAddress == "10.0.0.3");
    }

    [Fact]
    public async Task AccountWithHistoryIsAnonymizedAndKeepsIt()
    {
        await Start();
        await AddPointsAndOrder();
        await AddSecurityRows(bob, "bob");
        using (var db = app.Db())
        {
            db.AccountsHistories.Add(new AccountsHistory { UserId = bob, Timestamp = t0, UserType = "driver", Username = "bob", Password = "oldhash", Email = "bob@example.com", FirstName = "Bob", PhoneNumber = "555" });
            await db.SaveChangesAsync();
        }

        var response = await Delete(bob, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"anonymized\":true", await response.Content.ReadAsStringAsync());
        using var after = app.Db();
        var user = await after.Users.SingleAsync(u => u.Id == bob);
        Assert.Equal($"deleted-{bob}", user.Username);
        Assert.Equal($"deleted-{bob}@deleted.invalid", user.Email);
        Assert.Equal(("Deleted", "user", "", ""), (user.FirstName, user.LastName, user.PhoneNumber, user.Address));
        Assert.Null(user.Password);
        Assert.Equal(User.Inactive, user.Status);
        Assert.True(user.LeaderboardHidden);
        Assert.Equal(1, user.SessionVersion);

        Assert.Equal(2, await after.PointsHistory.CountAsync(h => h.UserId == bob));
        Assert.Equal(1, await after.AuditHistories.CountAsync());
        Assert.Equal("Mug", (await after.CartItems.SingleAsync(c => c.UserId == bob)).Name);
        Assert.True(await after.AccountsCompanies.AnyAsync(m => m.AccountId == bob));
        Assert.All(await after.UserSessions.Where(s => s.UserId == bob).ToListAsync(), s => Assert.NotNull(s.RevokedAt));
        Assert.False(await after.PasskeyCredentials.AnyAsync(p => p.UserId == bob));
        Assert.False(await after.SignInCodes.AnyAsync(c => c.UserId == bob));
        Assert.False(await after.EmailConfirmations.AnyAsync(c => c.UserId == bob));
        Assert.False(await after.PasswordResets.AnyAsync(r => r.UserId == bob));
        Assert.False(await after.NotificationPreferences.AnyAsync(p => p.UserId == bob));
        Assert.False(await after.WishlistItems.AnyAsync(w => w.UserId == bob));
        var snapshot = await after.AccountsHistories.SingleAsync(h => h.UserId == bob);
        Assert.Equal(($"deleted-{bob}", "", null), (snapshot.Username, snapshot.Password, snapshot.PhoneNumber));
        Assert.DoesNotContain(await after.LoginAttempts.Where(a => a.UserId == bob).Select(a => a.Username).ToListAsync(), n => n == "bob");
        Assert.All(await after.LoginAttempts.Where(a => a.UserId == bob).ToListAsync(), a => Assert.Equal("", a.IpAddress));
        Assert.All(await after.UserSessions.Where(s => s.UserId == bob).ToListAsync(), s => Assert.Null(s.IpAddress ?? s.UserAgent));
        Assert.All(await after.PasswordChanges.Where(c => c.UserId == bob).ToListAsync(), c => Assert.Null(c.IpAddress));
    }

    [Fact]
    public async Task AnonymizedAccountCantSignInAgain()
    {
        await Start();
        await AddPointsAndOrder();
        using (var db = app.Db())
        {
            (await db.Users.SingleAsync(u => u.Id == bob)).EmailCodes = true;
            await db.SaveChangesAsync();
        }
        // a code is pending when the account goes
        var pending = await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = Password });
        Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode);
        var challenge = (await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challenge").GetString();
        var code = app.Email.Sent.Last().Subject[^6..];

        Assert.Equal(HttpStatusCode.OK, (await Delete(bob, Password)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/auth/login", new { username = "bob", password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/auth/login", new { username = $"deleted-{bob}", password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync("/auth/login/code", new { challenge, code })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/auth/external", new { provider = "Google", email = $"deleted-{bob}@deleted.invalid" })).StatusCode);
        // the old address only reaches a brand new account
        var sso = await client.PostAsJsonAsync("/auth/external", new { provider = "Google", email = "bob@example.com", name = "Bob" });
        Assert.NotEqual(bob, (await sso.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task WrongPasswordIsRefused()
    {
        await Start();

        var response = await Delete(bob, "nope");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Current password is wrong.", await response.Content.ReadAsStringAsync());
        Assert.Equal("bob", (await app.Db().Users.SingleAsync(u => u.Id == bob)).Username);
    }

    [Fact]
    public async Task AdminsCantDeleteThemselves()
    {
        await Start();

        var response = await Delete(root, Password);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Ask another admin", await response.Content.ReadAsStringAsync());
        Assert.True(await app.Db().Users.AnyAsync(u => u.Id == root));
    }

    [Fact]
    public async Task AccountsWithoutAPasswordConfirmWithTheirUsername()
    {
        await Start();
        var sso = await client.PostAsJsonAsync("/auth/external", new { provider = "Google", email = "sue@gmail.com", name = "Sue Smith" });
        var sue = (await sso.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var wrong = await Delete(sue, "someone");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Contains("username", await wrong.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await Delete(sue, "sue")).StatusCode);
        Assert.False(await app.Db().Users.AnyAsync(u => u.Id == sue));
    }

    [Fact]
    public async Task AdminDeleteStillRefusesAccountsWithHistory()
    {
        await Start();
        await AddPointsAndOrder();

        var response = await client.DeleteAsync($"/admin/accounts/{bob}?adminId={root}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("bob", (await app.Db().Users.SingleAsync(u => u.Id == bob)).Username);
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}
