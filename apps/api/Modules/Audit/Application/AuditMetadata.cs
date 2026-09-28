using System.Text.Json;

namespace ResidentialAmenities.Api.Modules.Audit.Application;

/// <summary>
/// Small, safe, server-built audit metadata. There is deliberately no way to
/// pass an arbitrary dictionary or object: every shape is a typed factory
/// below that accepts only allowlisted, non-sensitive values (ids, enums,
/// amounts, currency, instants). Nothing here can carry a password, token,
/// cookie, secret, signature, header, request/response body, checkout URL,
/// idempotency key, e-mail or payer data because no factory accepts one.
/// </summary>
public sealed class AuditMetadata
{
    public const int MaxReasonLength = 500;

    /// <summary>Every key that can ever appear in audit metadata.</summary>
    public static readonly IReadOnlySet<string> AllowedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "useType",
        "startsAtUtc",
        "endsAtUtc",
        "resourceCount",
        "status",
        "originalExpiresAtUtc",
        "reason",
        "reservationId",
        "amount",
        "currency",
        "method",
        "reservationOutcome",
        "outcome",
        "result",
        "failure"
    };

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private AuditMetadata(SortedDictionary<string, object?> values)
    {
        foreach (var key in values.Keys)
        {
            if (!AllowedKeys.Contains(key))
            {
                throw new InvalidOperationException($"Audit metadata key '{key}' is not allowlisted.");
            }
        }

        Json = JsonSerializer.Serialize(values, Options);
    }

    public string Json { get; }

    public static AuditMetadata ReservationCreated(
        string useType,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        int resourceCount,
        string status) =>
        new(new()
        {
            ["useType"] = useType,
            ["startsAtUtc"] = startsAtUtc.ToUniversalTime(),
            ["endsAtUtc"] = endsAtUtc.ToUniversalTime(),
            ["resourceCount"] = resourceCount,
            ["status"] = status
        });

    public static AuditMetadata ReservationConfirmed() =>
        new(new() { ["status"] = "Confirmed" });

    public static AuditMetadata ReservationExpired(DateTimeOffset originalExpiresAtUtc) =>
        new(new() { ["originalExpiresAtUtc"] = originalExpiresAtUtc.ToUniversalTime() });

    /// <summary>
    /// For issue #26 (admin cancellation). The reason is optional here; #26
    /// decides where it is mandatory (RB-014).
    /// </summary>
    public static AuditMetadata ReservationCancelled(string? reason) =>
        new(new()
        {
            ["reason"] = reason is { Length: > MaxReasonLength } ? reason[..MaxReasonLength] : reason
        });

    public static AuditMetadata PaymentInitiated(
        Guid reservationId,
        decimal amount,
        string currency,
        string method) =>
        new(new()
        {
            ["reservationId"] = reservationId,
            ["amount"] = amount,
            ["currency"] = currency,
            ["method"] = method
        });

    /// <summary>Approved / confirmed payments: which reservation, and what Reservations decided.</summary>
    public static AuditMetadata PaymentSettled(
        string method,
        Guid reservationId,
        decimal amount,
        string currency,
        string reservationOutcome) =>
        new(new()
        {
            ["method"] = method,
            ["reservationId"] = reservationId,
            ["amount"] = amount,
            ["currency"] = currency,
            ["reservationOutcome"] = reservationOutcome
        });

    public static AuditMetadata PaymentTransition(string method, Guid reservationId) =>
        new(new()
        {
            ["method"] = method,
            ["reservationId"] = reservationId
        });

    public static AuditMetadata ManualReview(string outcome, Guid reservationId) =>
        new(new()
        {
            ["outcome"] = outcome,
            ["reservationId"] = reservationId
        });

    public static AuditMetadata WebhookProcessed(string result) =>
        new(new() { ["result"] = result });

    /// <summary>A general failure category only — never the submitted e-mail or password.</summary>
    public static AuditMetadata AuthenticationFailure(string failure) =>
        new(new() { ["failure"] = failure });
}
