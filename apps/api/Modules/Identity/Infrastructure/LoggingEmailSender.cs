using ResidentialAmenities.Api.Modules.Identity.Application;

namespace ResidentialAmenities.Api.Modules.Identity.Infrastructure;

/// <summary>
/// Development-safe default <see cref="IEmailSender"/> (issue #93): logs
/// the email instead of sending it. No production provider is decided yet
/// — this exists purely so the invitation/verification flow works locally
/// and in CI without needing real credentials, and can be swapped for a
/// real provider later without touching any caller.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Email (dev-mode, not actually sent) to {ToEmail} — {Subject}: {Body}",
            toEmail, subject, body);

        return Task.CompletedTask;
    }
}
