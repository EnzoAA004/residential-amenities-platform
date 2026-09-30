namespace ResidentialAmenities.Api.Modules.Audit.Domain;

/// <summary>The kind of object an audited action was performed on.</summary>
public enum AuditTargetType
{
    Reservation = 0,
    Payment = 1,
    User = 2,
    PriceRule = 3,
    Amenity = 4,
    EventSlot = 5,
    IncidentReport = 6
}
