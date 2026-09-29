using ResidentialAmenities.Api.Modules.Pricing.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Domain;

/// <summary>
/// Opaque, resident-facing entry point for starting a reservation flow from a
/// QR code or similar external link. It points at an amenity context only;
/// availability, pricing and conflict checks still belong to reservation
/// creation.
/// </summary>
public sealed class ReservationEntryPoint
{
    private ReservationEntryPoint()
    {
    }

    public ReservationEntryPoint(
        Guid id,
        string token,
        Guid buildingId,
        Guid amenityId,
        ReservationUseType? suggestedUseType = null,
        string? displayName = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Reservation entry point id is required.", nameof(id));
        }

        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException("Building id is required.", nameof(buildingId));
        }

        if (amenityId == Guid.Empty)
        {
            throw new ArgumentException("Amenity id is required.", nameof(amenityId));
        }

        Id = id;
        Token = NormalizeToken(token);
        BuildingId = buildingId;
        AmenityId = amenityId;
        SuggestedUseType = suggestedUseType;
        DisplayName = NormalizeOptionalText(displayName);
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string Token { get; private set; } = string.Empty;

    public Guid BuildingId { get; private set; }

    public Guid AmenityId { get; private set; }

    public ReservationUseType? SuggestedUseType { get; private set; }

    public string? DisplayName { get; private set; }

    public bool IsActive { get; private set; }

    public void Deactivate()
    {
        IsActive = false;
    }

    private static string NormalizeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token is required.", nameof(token));
        }

        var normalized = token.Trim().ToLowerInvariant();

        if (normalized.Length > 120)
        {
            throw new ArgumentException("Token must be at most 120 characters.", nameof(token));
        }

        if (!normalized.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.'))
        {
            throw new ArgumentException(
                "Token may only contain ASCII letters, digits, dash, underscore or dot.",
                nameof(token));
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length > 160
            ? throw new ArgumentException("Display name must be at most 160 characters.", nameof(value))
            : normalized;
    }
}
