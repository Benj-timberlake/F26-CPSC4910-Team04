using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Bunit;
using FrontEnd.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class UptimeTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;

    private async Task Start() => client = await app.Start();

    // one sample per minute up to the clock
    private async Task Samples(DateTime first, params bool[] oks)
    {
        using var db = app.Db();
        for (var i = 0; i < oks.Length; i++)
            db.UptimeChecks.Add(new UptimeCheck { CheckedAt = first.AddMinutes(i), Ok = oks[i], LatencyMs = 5, Error = oks[i] ? null : "down" });
        await db.SaveChangesAsync();
    }

    private DateTime Now => app.Clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task NoSamplesYetHasNoPercent()
    {
        await Start();
        var report = (await client.GetFromJsonAsync<UptimeReport>("/admin/uptime"))!;
        Assert.Null(report.Day.Percent);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public async Task FailedSamplesCountAgainstTheWindow()
    {
        await Start();
        await Samples(Now.AddMinutes(-9), true, true, false, true, true, true, true, true, true, false);

        var report = (await client.GetFromJsonAsync<UptimeReport>("/admin/uptime"))!;

        Assert.Equal(new UptimeWindow(80, 10, 8), report.Day);
        Assert.Equal(2, report.Failures.Count);
        Assert.Equal("down", report.Failures[0].Error);
    }

    [Fact]
    public async Task MinutesWithNoSampleCountAsDown()
    {
        await Start();
        await Samples(Now.AddMinutes(-9), true, true, true, true, true);
        await Samples(Now, true);

        var report = (await client.GetFromJsonAsync<UptimeReport>("/admin/uptime"))!;

        Assert.Equal(new UptimeWindow(60, 10, 6), report.Day);
    }

    [Fact]
    public async Task TimeBeforeTheFirstSampleIsNotDowntime()
    {
        await Start();
        await Samples(Now.AddHours(-2), Enumerable.Repeat(true, 121).ToArray());

        var report = (await client.GetFromJsonAsync<UptimeReport>("/admin/uptime"))!;

        Assert.Equal(new UptimeWindow(100, 121, 121), report.Month);
    }

    [Fact]
    public async Task WindowsOnlyCountTheirOwnSamples()
    {
        await Start();
        // 3 days up with a 2 hour outage ending now
        await Samples(Now.AddDays(-3), Enumerable.Repeat(true, 3 * 24 * 60 - 120).ToArray());

        var report = (await client.GetFromJsonAsync<UptimeReport>("/admin/uptime"))!;

        Assert.Equal(1320, report.Day.Up);
        Assert.Equal(1441, report.Day.Expected);
        Assert.Equal(91.6, report.Day.Percent!.Value, 1);
        Assert.True(report.Week.Percent > report.Day.Percent);
    }

    [Fact]
    public async Task RecordSavesAnOkSampleAndPrunesOldOnes()
    {
        await Start();
        await Samples(Now.AddDays(-91), true);
        await Samples(Now.AddDays(-89), true);

        using (var db = app.Db())
            await UptimeService.Record(db, app.Clock);

        var rows = await app.Db().UptimeChecks.OrderBy(c => c.CheckedAt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.True(rows[1].Ok);
        Assert.Equal(Now, rows[1].CheckedAt);
        Assert.Null(rows[1].Error);
    }

    [Fact]
    public async Task RecordKeepsAFailureWithItsMessage()
    {
        await Start();
        using var db = app.Db();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE users");

        await UptimeService.Record(db, app.Clock);

        var row = await db.UptimeChecks.SingleAsync();
        Assert.False(row.Ok);
        Assert.Contains("users", row.Error);
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}

public sealed class UptimeCardTests : TestContext
{
    private IRenderedComponent<UptimeCard> Render(string json)
    {
        var backend = new JsonBackend();
        backend.Routes["GET /admin/uptime"] = (HttpStatusCode.OK, json);
        this.UseBackend(backend);
        return RenderComponent<UptimeCard>();
    }

    [Fact]
    public void FlagsAWindowBelowTheTarget()
    {
        var page = Render("""
            {"target":95,"day":{"percent":99.5,"expected":1000,"up":995},"week":{"percent":93.2,"expected":1000,"up":932},"month":{"percent":100,"expected":10,"up":10},
             "failures":[{"checkedAt":"2026-09-17T11:00:00","latencyMs":30000,"error":"timeout"}]}
            """);

        page.WaitForAssertion(() => page.Find("td.low"));
        Assert.Contains("93.2%", page.Find("td.low").TextContent);
        Assert.Single(page.FindAll("td.low"));
        Assert.Contains("2026-09-17 11:00 UTC", page.Markup);
        Assert.Contains("timeout", page.Markup);
    }

    [Fact]
    public void SaysSoWhenNothingIsRecorded()
    {
        var page = Render("""{"target":95,"day":{"percent":null,"expected":0,"up":0},"week":{"percent":null,"expected":0,"up":0},"month":{"percent":null,"expected":0,"up":0},"failures":[]}""");
        page.WaitForAssertion(() => Assert.Contains("No checks recorded yet", page.Markup));
    }
}
