using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class NotificationInboxTests : IAsyncDisposable
{
    private readonly TestApp app = new();

    private async Task<(HttpClient client, int bob, int sue)> StartWithTwoUsers()
    {
        var client = await app.Start();
        foreach (var name in new[] { "bob", "sue" })
            await client.PostAsJsonAsync("/auth/register", new { username = name, firstName = name, lastName = "Driver", email = $"{name}@example.com", password = "Hunter22x!", userType = "driver" });
        using var db = app.Db();
        return (client, (await db.Users.SingleAsync(u => u.Username == "bob")).Id, (await db.Users.SingleAsync(u => u.Username == "sue")).Id);
    }

    private async Task Seed(int userId, string subject, int minutesAgo, bool read = false)
    {
        using var db = app.Db();
        db.NotificationsHistories.Add(new NotificationsHistory
        {
            UserId = userId, Subject = subject, Body = subject + " body", BeenRead = read,
            Timestamp = app.Clock.GetUtcNow().UtcDateTime.AddMinutes(-minutesAgo)
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ListsOnlyTheUsersNotificationsNewestFirst()
    {
        var (client, bob, sue) = await StartWithTwoUsers();
        await Seed(bob, "older", 10, read: true);
        await Seed(bob, "newer", 1);
        await Seed(sue, "not bob's", 5);

        var list = (await client.GetFromJsonAsync<List<NotificationEntry>>($"/users/{bob}/notifications"))!;

        Assert.Equal(["newer", "older"], list.Select(n => n.Subject));
        Assert.Equal([false, true], list.Select(n => n.Read));
        Assert.Equal("newer body", list[0].Body);
    }

    [Fact]
    public async Task ListStopsAtTheCap()
    {
        var (client, bob, _) = await StartWithTwoUsers();
        for (var i = 0; i < NotificationEndpoints.MaxShown + 5; i++)
            await Seed(bob, $"n{i}", i);

        var list = (await client.GetFromJsonAsync<List<NotificationEntry>>($"/users/{bob}/notifications"))!;

        Assert.Equal(NotificationEndpoints.MaxShown, list.Count);
        Assert.Equal("n0", list[0].Subject);
    }

    [Fact]
    public async Task UnknownUserIsNotFound()
    {
        var (client, _, _) = await StartWithTwoUsers();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/users/999/notifications")).StatusCode);
    }

    [Fact]
    public async Task MarkingReadClearsOnlyThatUsersCount()
    {
        var (client, bob, sue) = await StartWithTwoUsers();
        await Seed(bob, "one", 2);
        await Seed(bob, "two", 1);
        await Seed(sue, "sue's", 1);

        Assert.Equal(2, (await client.GetFromJsonAsync<UnreadNotifications>($"/users/{bob}/notifications/unread"))!.Count);

        var response = await client.PostAsync($"/users/{bob}/notifications/read", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<UnreadNotifications>($"/users/{bob}/notifications/unread"))!.Count);
        Assert.Equal(1, (await client.GetFromJsonAsync<UnreadNotifications>($"/users/{sue}/notifications/unread"))!.Count);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
