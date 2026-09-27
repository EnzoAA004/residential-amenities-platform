namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// Raised when the requested resource/time range is incompatible with an
/// existing confirmed reservation (RB-003, RB-005, RF-010). This is ordinary
/// transactional conflict detection; it does not by itself close the race
/// between two truly concurrent incompatible requests — that is completed
/// in issue #23.
/// </summary>
public sealed class ReservationConflictException(string message) : Exception(message);
