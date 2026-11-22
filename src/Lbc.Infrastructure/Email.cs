using Lbc.Core.Abstractions;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Lbc.Infrastructure;

public sealed class SmtpOptions
{
    public const string Section = "Smtp";
    /// <summary>"Smtp" sends real mail; "Log" just writes emails to the log (local development).</summary>
    public string Mode { get; set; } = "Log";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    /// <summary>"StartTls" (port 587), "SslOnConnect" (port 465) or "None" (local test servers only).</summary>
    public string Security { get; set; } = "StartTls";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "info@leicesterbhangraclasses.co.uk";
    public string FromName { get; set; } = "Leicester Bhangra Classes";
    /// <summary>
    /// Testing aid: when set, every email goes to this address instead of its real recipient,
    /// and the subject is prefixed with the original recipient. Leave empty in production.
    /// </summary>
    public string? RedirectAllTo { get; set; }
}

/// <summary>Sends mail through any SMTP server (the client's own mailbox provider).</summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var o = options.Value;
        if (!string.IsNullOrWhiteSpace(o.RedirectAllTo))
            message = message with { To = o.RedirectAllTo.Trim(), Subject = $"[to {message.To}] {message.Subject}" };

        if (!string.Equals(o.Mode, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("EMAIL (not sent, Smtp:Mode={Mode}) To={To} Subject={Subject}\n{Body}", o.Mode, message.To, message.Subject, message.HtmlBody);
            return;
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        if (message.ReplyTo is not null) mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody();

        var security = o.Security.ToLowerInvariant() switch
        {
            "sslonconnect" => SecureSocketOptions.SslOnConnect,
            "none" => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls,
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(o.Host, o.Port, security, ct);
        if (!string.IsNullOrEmpty(o.Username)) await client.AuthenticateAsync(o.Username, o.Password ?? "", ct);
        await client.SendAsync(new SendRequest(mime), ct);
        await client.DisconnectAsync(true, ct);
    }
}
