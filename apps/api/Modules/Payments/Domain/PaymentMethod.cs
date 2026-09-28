namespace ResidentialAmenities.Api.Modules.Payments.Domain;

/// <summary>How a payment is collected. Both methods are the same <see cref="Payment"/> model.</summary>
public enum PaymentMethod
{
    MercadoPago = 0,

    /// <summary>Cash handed to an authorized person; receipt is confirmed manually (RF-015/016).</summary>
    Cash = 1
}
