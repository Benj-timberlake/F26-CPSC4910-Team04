using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class BannerEndpointTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob;
    private int root;

    private async Task Start()
    {
        client = await app.StartWithBobAndRoot();
        using var db = app.Db();
        bob = (await db.Users.SingleAsync(u => u.Username == "bob")).Id;
        root = (await db.Users.SingleAsync(u => u.Username == "root")).Id;
    }

    private Task<HttpResponseMessage> Set(int adminId, string? message, string? level = "info") =>
        client.PutAsJsonAsync("/admin/site-banner", new { adminId, message, level });

    [Fact]
    public async Task NoBannerAnswersNoContent()
    {
        await Start();

        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/site-banner")).StatusCode);
    }

    [Fact]
    public async Task AdminSetsTheBannerAndAnyoneCanReadIt()
    {
        await Start();

        Assert.Equal(HttpStatusCode.NoContent, (await Set(root, "  Down for upkeep at 9pm.  ", "warning")).StatusCode);

        var banner = await client.GetFromJsonAsync<BannerEntry>("/site-banner");
        Assert.Equal(("Down for upkeep at 9pm.", "warning", app.Clock.GetUtcNow().UtcDateTime), (banner!.Message, banner.Level, banner.CreatedAt));
        using var db = app.Db();
        Assert.Equal(root, (await db.SiteBanners.SingleAsync()).SetBy);
    }

    [Fact]
    public async Task NewBannerReplacesTheOldOne()
    {
        await Start();
        await Set(root, "first");
        app.Clock.Advance(TimeSpan.FromMinutes(5));
        await Set(root, "second");

        Assert.Equal("second", (await client.GetFromJsonAsync<BannerEntry>("/site-banner"))!.Message);
        using var db = app.Db();
        Assert.Single(await db.SiteBanners.Where(b => b.ClearedAt == null).ToListAsync());
        Assert.Equal(2, await db.SiteBanners.CountAsync());
    }

    [Fact]
    public async Task ClearingHidesTheBanner()
    {
        await Start();
        await Set(root, "hello");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/admin/site-banner/clear", new { adminId = root })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/site-banner")).StatusCode);
    }

    [Fact]
    public async Task OnlyAdminsCanSetOrClear()
    {
        await Start();
        await Set(root, "hello");

        Assert.Equal(HttpStatusCode.Forbidden, (await Set(bob, "mine")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/admin/site-banner/clear", new { adminId = bob })).StatusCode);
        Assert.Equal("hello", (await client.GetFromJsonAsync<BannerEntry>("/site-banner"))!.Message);
    }

    [Theory]
    [InlineData("", "info")]
    [InlineData("   ", "info")]
    [InlineData(null, "info")]
    [InlineData("hi", "error")]
    [InlineData("hi", null)]
    public async Task BadMessagesAndLevelsAreRefused(string? message, string? level)
    {
        await Start();

        Assert.Equal(HttpStatusCode.BadRequest, (await Set(root, message, level)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Set(root, new string('x', 501))).StatusCode);
    }

    [Fact]
    public async Task DeletingTheAdminKeepsTheBanner()
    {
        await Start();
        await Set(root, "hello");
        using var db = app.Db();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
        await db.Users.Where(u => u.Id == root).ExecuteDeleteAsync();

        var banner = await db.SiteBanners.SingleAsync();
        Assert.Null(banner.SetBy);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
