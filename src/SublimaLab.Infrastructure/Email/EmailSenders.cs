using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Abstractions;

namespace SublimaLab.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"Pickup" writes emails to disk (development); "Smtp" delivers them.</summary>
    public string Provider { get; set; } = "Pickup";
    public string FromAddress { get; set; } = "hello@sublimalab.local";
    public string FromName { get; set; } = "SublimaLab";
    public string PickupDirectory { get; set; } = "App_Data/mail";

    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
}

/// <summary>Development sender: writes each email as an HTML file you can open in a browser.</summary>
public sealed partial class PickupDirectoryEmailSender(IOptions<EmailOptions> options, IHostEnvironment environment, TimeProvider clock, ILogger<PickupDirectoryEmailSender> logger)
    : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.PickupDirectory));
        Directory.CreateDirectory(directory);

        var slug = UnsafeChars().Replace(message.Subject.ToLowerInvariant(), "-").Trim('-');
        var path = Path.Combine(directory, $"{clock.GetUtcNow():yyyyMMdd-HHmmss-fff}-{slug}.html");
        var html = $"<!-- To: {WebUtility.HtmlEncode(message.To)} | Subject: {WebUtility.HtmlEncode(message.Subject)} -->\n{message.HtmlBody}";

        await File.WriteAllTextAsync(path, html, Encoding.UTF8, cancellationToken);
        logger.LogInformation("Email '{Subject}' to {To} written to {Path}", message.Subject, message.To, path);
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex UnsafeChars();
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort) { EnableSsl = settings.SmtpUseSsl };
        if (!string.IsNullOrEmpty(settings.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = message.Subject,
            Body = message.TextBody,
        };
        mail.To.Add(message.To);
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, "text/html"));

        await client.SendMailAsync(mail, cancellationToken);
        logger.LogInformation("Email '{Subject}' sent", message.Subject);
    }
}
