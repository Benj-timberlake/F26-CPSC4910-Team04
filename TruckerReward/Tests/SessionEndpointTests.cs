using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class SessionEndpointTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob;

    private const string Chrome = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36";
    private const string Iphone = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";

    private async Task Start()
    {
        client = await app.Start();
        await client.Register("bob", "Bob", "Driver");
        bob = (await app.Db().Users.SingleAsync()).Id;
    }

    private async Task<string> Session(string userAgent, string ip = "10.0.0.1")
    {
        var response = await client.PostAsJsonAsync($"/users/{bob}/sessions", new { ipAddress = ip, userAgent });
        return (await response.Content.ReadFromJsonAsync<Started>())!.Key;
    }

    private async Task<List<SessionEntry>> List(string current) =>
        (await client.GetFromJsonAsync<List<SessionEntry>>($"/users/{bob}/sessions?current={current}"))!;

    [Fact]
    public async Task EachSignInIsListedWithItsDeviceAndTheCurrentOneMarked()
    {
        await Start();
        var mac = await Session(Chrome);
        app.Clock.Advance(TimeSpan.FromMinutes(1));
        var phone = await Session(Iphone, "10.0.0.2");

        var sessions = await List(mac);

        Assert.Equal([("Safari on iPhone", "10.0.0.2", false), ("Chrome on macOS", "10.0.0.1", true)],
            sessions.Select(s => (s.Device, s.IpAddress, s.Current)));
        Assert.DoesNotContain(mac, string.Join(",", await app.Db().UserSessions.Select(s => s.KeyHash).ToListAsync()));
    }

    [Fact]
    public async Task EndingASessionFailsItsNextCheck()
    {
        await Start();
        var mac = await Session(Chrome);
        var phone = await Session(Iphone);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/sessions/check", new { key = phone })).StatusCode);

        var id = (await List(mac)).Single(s => !s.Current).Id;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/users/{bob}/sessions/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync("/sessions/check", new { key = phone })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/sessions/check", new { key = mac })).StatusCode);
        Assert.Single(await List(mac));
    }

    [Fact]
    public async Task OthersSessionsCantBeEnded()
    {
        await Start();
        await Session(Chrome);
        var id = (await app.Db().UserSessions.SingleAsync()).Id;

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/users/{bob + 1}/sessions/{id}")).StatusCode);
    }

    [Fact]
    public async Task ChecksKeepSessionsFreshAndQuietOnesDropOff()
    {
        await Start();
        var mac = await Session(Chrome);
        var phone = await Session(Iphone);
        app.Clock.Advance(SessionEndpoints.ActiveWindow - TimeSpan.FromMinutes(1));
        await client.PostAsJsonAsync("/sessions/check", new { key = mac, ipAddress = "10.0.0.9" });
        app.Clock.Advance(TimeSpan.FromMinutes(2));

        var left = Assert.Single(await List(mac));
        Assert.Equal("10.0.0.9", left.IpAddress);
    }

    [Fact]
    public async Task LogoutAndSignOutEverywhereEndSessions()
    {
        await Start();
        var mac = await Session(Chrome);
        var phone = await Session(Iphone);

        await client.PostAsJsonAsync("/sessions/end", new { key = mac });
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync("/sessions/check", new { key = mac })).StatusCode);

        await client.PostAsJsonAsync($"/users/{bob}/sessions/revoke", new { actorId = bob });
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync("/sessions/check", new { key = phone })).StatusCode);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36 Edg/141.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:140.0) Gecko/20100101 Firefox/140.0", "Firefox on Linux")]
    [InlineData("Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData(null, "Unknown browser")]
    public void DevicesAreNamedFromTheUserAgent(string? userAgent, string expected) =>
        Assert.Equal(expected, SessionEndpoints.Device(userAgent));

    private sealed record Started(string Key);

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
