namespace ResidentialAmenities.Api.Modules.Messaging.Domain;

/// <summary>
/// A single message in a reservation-scoped conversation between the
/// resident who owns the reservation and building administrators (#78).
/// Deliberately not a building-wide chat: every message is bound to exactly
/// one <see cref="ReservationId"/>, and authorization (resident owner vs.
/// Administrator) is enforced by the endpoint, not by this entity.
/// </summary>
public sealed class ReservationMessage
{
    public const int MaxContentLength = 2000;

    private ReservationMessage()
    {
    }

    public ReservationMessage(
        Guid id,
        Guid reservationId,
        Guid authorUserId,
        bool authorIsAdministrator,
        string content,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Message id is required.", nameof(id));
        }

        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException("Reservation id is required.", nameof(reservationId));
        }

        if (authorUserId == Guid.Empty)
        {
            throw new ArgumentException("Author user id is required.", nameof(authorUserId));
        }

        Id = id;
        ReservationId = reservationId;
        AuthorUserId = authorUserId;
        AuthorIsAdministrator = authorIsAdministrator;
        Content = NormalizeContent(content);
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ReservationId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public bool AuthorIsAdministrator { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private static string NormalizeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Message content is required.", nameof(content));
        }

        var trimmed = content.Trim();

        return trimmed.Length > MaxContentLength
            ? throw new ArgumentException(
                $"Message content must be at most {MaxContentLength} characters.",
                nameof(content))
            : trimmed;
    }
}
