using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TruckerReward.Tests;

public sealed class EmailSetupTests
{
    private readonly ErrorLog log = new();

    private static IEmailSender SenderFor(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        foreach (var (key, value) in settings)
            builder.Configuration[key] = value;
        builder.AddEmail();
        return builder.Build().Services.GetRequiredService<IEmailSender>();
    }

    [Fact]
    public void NothingConfiguredOnlyLogs()
    {
        Assert.IsType<LogEmailSender>(SenderFor([]));
    }

    [Fact]
    public void SmtpHostPicksSmtpEvenWithEmailFrom()
    {
        var sender = SenderFor(new()
        {
            ["SMTP_HOST"] = "smtp.gmail.com",
            ["SMTP_USER"] = "TruckerRewards@gmail.com",
            ["SMTP_PASSWORD"] = "app-password",
            ["EMAIL_FROM"] = "TruckerRewards@gmail.com",
        });
        Assert.IsType<SmtpEmailSender>(sender);
    }

    [Fact]
    public async Task UnreachableServerIsLoggedNotThrown()
    {
        // a port nothing listens on, so the connection is refused straight away
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var sender = new SmtpEmailSender(new SmtpSettings("127.0.0.1", port, "user", "password"),
            "TruckerRewards@gmail.com", log);
        await sender.SendAsync("bob@example.com", "subject", "body");
        Assert.Contains(log.Errors, e => e.Contains("bob@example.com"));
    }

    [Fact]
    public async Task MissingSenderAddressIsLoggedNotThrown()
    {
        var sender = new SmtpEmailSender(new SmtpSettings("127.0.0.1", 1, "", ""), "", log);
        await sender.SendAsync("bob@example.com", "subject", "body");
        Assert.Contains(log.Errors, e => e.Contains("bob@example.com"));
    }

    private sealed class ErrorLog : ILogger<SmtpEmailSender>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }
}
