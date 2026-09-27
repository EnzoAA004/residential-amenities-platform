namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// Configurable payment-hold duration (RF-011). Bound from
/// <c>Reservations:Hold:DurationMinutes</c> — never a hardcoded constant.
///
/// The default here is a short placeholder chosen for practical local
/// development and testing, NOT the real candidate values under discussion
/// for OQ-010 (24 or 48 hours — see
/// docs/01-discovery/assumptions-and-open-questions.md). Production must set
/// this via configuration once issue #2 answers OQ-010.
/// </summary>
public sealed class ReservationHoldOptions
{
    public const string SectionName = "Reservations:Hold";

    public int DurationMinutes { get; set; } = 30;

    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);
}
