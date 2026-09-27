namespace ResidentialAmenities.Api.Modules.Amenities.Application;

public sealed record AvailabilityInterval(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);
