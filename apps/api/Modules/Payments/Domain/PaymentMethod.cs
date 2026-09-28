namespace ResidentialAmenities.Api.Modules.Payments.Domain;

/// <summary>
/// How a payment is collected. Cash (#25) is added when that flow exists;
/// an unused member would be dead surface.
/// </summary>
public enum PaymentMethod
{
    MercadoPago = 0
}
