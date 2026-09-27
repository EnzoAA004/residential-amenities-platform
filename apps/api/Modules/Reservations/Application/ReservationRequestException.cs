namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// A reservation request rejected for a reason other than a booking
/// conflict (see <see cref="ReservationConflictException"/>) — malformed
/// input, an unknown/mismatched amenity, or a business rule such as
/// "outside configured availability". <see cref="StatusCode"/> lets the
/// endpoint map it straight to a ProblemDetails response.
/// </summary>
public sealed class ReservationRequestException(string message, int statusCode)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
