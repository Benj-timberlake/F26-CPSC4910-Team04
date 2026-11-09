using System.Diagnostics;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;

// records the /health database check every minute
public sealed class UptimeService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<UptimeService> logger) : BackgroundService
{
    public static readonly TimeSpan Keep = TimeSpan.FromDays(90);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await Record(db, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // a failed check leaves a gap in the samples
                logger.LogWarning(ex, "Uptime sample not saved.");
            }

            try
            {
                var past = TimeSpan.FromTicks(clock.GetUtcNow().Ticks % TimeSpan.TicksPerMinute);
                await Task.Delay(TimeSpan.FromMinutes(1) - past, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public static async Task Record(AppDbContext db, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var timer = Stopwatch.StartNew();
        string? error = null;
        try
        {
            await db.Users.CountAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.Message.Length > 255 ? ex.Message[..255] : ex.Message;
        }
        timer.Stop();
        db.UptimeChecks.Add(new UptimeCheck { CheckedAt = now, Ok = error is null, LatencyMs = (int)timer.ElapsedMilliseconds, Error = error });
        await db.SaveChangesAsync(cancellationToken);
        await db.UptimeChecks.Where(c => c.CheckedAt < now - Keep).ExecuteDeleteAsync(cancellationToken);
    }
}
