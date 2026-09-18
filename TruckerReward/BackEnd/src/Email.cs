using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body);
}

public sealed class LogEmailSender(ILogger<LogEmailSender> log) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body)
    {
        log.LogInformation("email to {To}: {Subject}\n{Body}", to, subject, body);
        return Task.CompletedTask;
    }
}

// ses through the instance role. EMAIL_FROM must be a verified identity
public sealed class SesEmailSender(IAmazonSimpleEmailService ses, string from, ILogger<SesEmailSender> log) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body)
    {
        try
        {
            await ses.SendEmailAsync(new SendEmailRequest(
                from,
                new Destination([to]),
                new Message(new Content(subject), new Body { Text = new Content(body) })));
        }
        catch (Exception ex) when (ex is AmazonSimpleEmailServiceException or HttpRequestException)
        {
            // a mail failure shouldn't fail the request that triggered it
            log.LogError(ex, "could not send email to {To}: {Subject}", to, subject);
        }
    }
}

public static class EmailSetup
{
    public const string FromKey = "EMAIL_FROM";

    public static void AddEmail(this WebApplicationBuilder builder)
    {
        var from = builder.Configuration[FromKey];
        if (string.IsNullOrEmpty(from))
        {
            builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
            return;
        }
        builder.Services.AddSingleton<IAmazonSimpleEmailService>(_ => new AmazonSimpleEmailServiceClient());
        builder.Services.AddSingleton<IEmailSender>(sp => new SesEmailSender(
            sp.GetRequiredService<IAmazonSimpleEmailService>(), from, sp.GetRequiredService<ILogger<SesEmailSender>>()));
    }
}
