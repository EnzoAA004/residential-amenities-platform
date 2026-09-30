namespace ResidentialAmenities.Api.Modules.Identity.Application;

/// <summary>
/// Email delivery abstraction (issue #93). No production email provider is
/// decided yet — <c>LoggingEmailSender</c> is the only implementation for
/// now (it logs instead of actually sending), so a real provider can be
/// registered later without changing any caller.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken);
}
