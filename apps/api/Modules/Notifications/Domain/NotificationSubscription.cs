namespace ResidentialAmenities.Api.Modules.Notifications.Domain;

public sealed class NotificationSubscription
{
    private NotificationSubscription()
    {
        Endpoint = string.Empty;
        EndpointHash = string.Empty;
        P256Dh = string.Empty;
        Auth = string.Empty;
        UserAgent = string.Empty;
    }

    public NotificationSubscription(
        Guid id,
        Guid userId,
        NotificationSubscriptionPlatform platform,
        string endpoint,
        string endpointHash,
        string p256dh,
        string auth,
        string? userAgent,
        DateTimeOffset nowUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The subscription id is required.", nameof(id));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user id is required.", nameof(userId));
        }

        Id = id;
        UserId = userId;
        Platform = platform;
        Endpoint = Required(endpoint, nameof(endpoint));
        EndpointHash = Required(endpointHash, nameof(endpointHash));
        P256Dh = Required(p256dh, nameof(p256dh));
        Auth = Required(auth, nameof(auth));
        UserAgent = userAgent?.Trim();
        IsEnabled = true;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public NotificationSubscriptionPlatform Platform { get; private set; }

    public string Endpoint { get; private set; }

    public string EndpointHash { get; private set; }

    public string P256Dh { get; private set; }

    public string Auth { get; private set; }

    public string? UserAgent { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Refresh(
        NotificationSubscriptionPlatform platform,
        string endpoint,
        string p256dh,
        string auth,
        string? userAgent,
        DateTimeOffset nowUtc)
    {
        Platform = platform;
        Endpoint = Required(endpoint, nameof(endpoint));
        P256Dh = Required(p256dh, nameof(p256dh));
        Auth = Required(auth, nameof(auth));
        UserAgent = userAgent?.Trim();
        IsEnabled = true;
        UpdatedAtUtc = nowUtc;
    }

    private static string Required(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", paramName);
        }

        return value.Trim();
    }
}
