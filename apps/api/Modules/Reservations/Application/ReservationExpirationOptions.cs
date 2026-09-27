namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// How often <c>ReservationExpirationHostedService</c> checks for expired
/// holds. Bound from <c>Reservations:Expiration:IntervalSeconds</c>.
/// </summary>
public sealed class ReservationExpirationOptions
{
    public const string SectionName = "Reservations:Expiration";

    public int IntervalSeconds { get; set; } = 60;

    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);
}
