namespace ResidentialAmenities.Api.Modules.Payments.Application;

/// <summary>A payment request rejected for a business/availability reason; carries the HTTP status to map to.</summary>
public sealed class PaymentRequestException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
