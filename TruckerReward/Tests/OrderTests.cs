using System.Net;
using System.Net.Http.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class OrderTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(86399, true)]
    [InlineData(86400, false)]
    [InlineData(86401, false)]
    [InlineData(-1, false)]
    public async Task OrdersGroupItemsAndRefundEnforcesWindow(int ageSeconds, bool eligible)
    {
        await using var app = new TestApp();
        using var client = await app.Start();
        using var db = app.Db();
        var user = new User { Username = "buyer", Email = "buyer@test.com", UserType = "driver", PhoneNumber = "", Address = "", Points = 100 };
        var other = new User { Username = "other", Email = "other@test.com", UserType = "driver", PhoneNumber = "", Address = "" };
        db.Users.AddRange(user, other);
        await db.SaveChangesAsync();
        var history = new PointsHistory { UserId = user.Id, PointsDelta = -300, Timestamp = app.Clock.GetUtcNow().UtcDateTime.AddSeconds(-ageSeconds) };
        db.CartItems.AddRange(
            new CartItem { UserId = user.Id, Name = "Mug", Price = 1, Quantity = 2, InCart = false, WasOrdered = true, PointsHistory = history },
            new CartItem { UserId = user.Id, Name = "Pen", Price = 1, InCart = false, WasOrdered = true, PointsHistory = history },
            new CartItem { UserId = user.Id, Name = "Unordered", Price = 1 });
        await db.SaveChangesAsync();
        var orders = await client.GetFromJsonAsync<List<PastOrderDetails>>($"/users/{user.Id}/orders");
        var order = Assert.Single(orders!);
        Assert.Equal(history.Id, order.PointsHistoryId);
        Assert.Equal(history.Timestamp, order.Timestamp);
        Assert.Equal(300, order.TotalPoints);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(eligible, order.CanRefund);
        Assert.Empty((await client.GetFromJsonAsync<List<PastOrderDetails>>($"/users/{other.Id}/orders"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/users/{other.Id}/orders/{history.Id}/refund", null)).StatusCode);
        var path = $"/users/{user.Id}/orders/{history.Id}/refund";
        Assert.Equal(eligible ? HttpStatusCode.NoContent : HttpStatusCode.BadRequest, (await client.PostAsync(path, null)).StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(eligible ? 400 : 100, (await db.Users.FindAsync(user.Id))!.Points);
        Assert.Equal(eligible ? 0 : 1, await db.PointsHistory.CountAsync());
        Assert.Equal(eligible ? 1 : 3, await db.CartItems.CountAsync());
        if (eligible)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(path, null)).StatusCode);
            Assert.Equal(400, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).Points);
        }
    }

    [Fact]
    public async Task FailedDeletionRollsBackRefund()
    {
        await using var app = new TestApp();
        using var client = await app.Start();
        using var db = app.Db();
        var user = new User { Username = "buyer", Email = "buyer@test.com", UserType = "driver", PhoneNumber = "", Address = "", Points = 10 };
        var history = new PointsHistory { User = user, PointsDelta = -100, Timestamp = app.Clock.GetUtcNow().UtcDateTime };
        db.CartItems.Add(new CartItem { User = user, Name = "Mug", Price = 1, InCart = false, WasOrdered = true, PointsHistory = history });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_refund BEFORE DELETE ON Points_History BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        await Assert.ThrowsAnyAsync<Exception>(() => client.PostAsync($"/users/{user.Id}/orders/{history.Id}/refund", null));
        db.ChangeTracker.Clear();
        Assert.Equal(10, (await db.Users.FindAsync(user.Id))!.Points);
        Assert.Single(await db.PointsHistory.ToListAsync());
        Assert.Single(await db.CartItems.ToListAsync());
    }
}
