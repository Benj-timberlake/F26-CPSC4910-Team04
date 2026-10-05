using System.Net;
using System.Net.Mail;
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

// any smtp server with a login, e.g. gmail with an app password, for when ses isn't available
public sealed record SmtpSettings(string Host, int Port, string User, string Password);

public sealed class SmtpEmailSender(SmtpSettings smtp, string from, ILogger<SmtpEmailSender> log) : IEmailSender
{
    // a slow mail server shouldn't hold up the request that sends the email
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task SendAsync(string to, string subject, string body)
    {
        try
        {
            using var client = new SmtpClient(smtp.Host, smtp.Port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(smtp.User, smtp.Password)
            };
            using var message = new MailMessage(from, to, subject, body);
            using var cancel = new CancellationTokenSource(Timeout);
            await client.SendMailAsync(message, cancel.Token);
        }
        catch (Exception ex) when (ex is SmtpException or FormatException or ArgumentException or OperationCanceledException)
        {
            // a mail failure shouldn't fail the request that triggered it
            log.LogError(ex, "could not send email to {To}: {Subject}", to, subject);
        }
    }
}

public static class EmailSetup
{
    public const string FromKey = "EMAIL_FROM";
    public const string SmtpHostKey = "SMTP_HOST";
    public const string SmtpPortKey = "SMTP_PORT";
    public const string SmtpUserKey = "SMTP_USER";
    public const string SmtpPasswordKey = "SMTP_PASSWORD";

    // SMTP_HOST picks smtp, otherwise EMAIL_FROM alone picks ses, otherwise emails only go to the log
    public static void AddEmail(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var from = config[FromKey];
        if (!string.IsNullOrEmpty(config[SmtpHostKey]))
        {
            var smtp = new SmtpSettings(
                config[SmtpHostKey]!,
                int.TryParse(config[SmtpPortKey], out var port) ? port : 587,
                config[SmtpUserKey] ?? "",
                config[SmtpPasswordKey] ?? "");
            var sender = string.IsNullOrEmpty(from) ? smtp.User : from;
            builder.Services.AddSingleton<IEmailSender>(sp => new SmtpEmailSender(
                smtp, sender, sp.GetRequiredService<ILogger<SmtpEmailSender>>()));
            return;
        }
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
