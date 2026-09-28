using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Buildings.Application;
using ResidentialAmenities.Api.Modules.Identity;
using ResidentialAmenities.Api.Modules.Payments.Application;
using ResidentialAmenities.Api.Modules.Reservations.Application;

namespace ResidentialAmenities.Api.Modules.Payments;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPost(
                "/api/reservations/{reservationId:guid}/payments/mercadopago",
                InitiateMercadoPagoPaymentAsync)
            .WithTags("Payments")
            .RequireAuthorization(AuthorizationPolicies.ResidentAccess);

        endpoints
            .MapGet("/api/payments/{paymentId:guid}", GetPaymentAsync)
            .WithTags("Payments")
            .RequireAuthorization(AuthorizationPolicies.ResidentAccess);

        // Public on purpose: Mercado Pago calls it directly. Authenticity is
        // established by verifying x-signature, never by user credentials.
        endpoints
            .MapPost("/api/webhooks/mercadopago", ReceiveMercadoPagoWebhookAsync)
            .WithTags("Payments")
            .AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> InitiateMercadoPagoPaymentAsync(
        Guid reservationId,
        System.Security.Claims.ClaimsPrincipal principal,
        IReservationPaymentContract reservations,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        PaymentCreationService creationService,
        CancellationToken cancellationToken)
    {
        var reservation = await reservations.GetPayableReservationAsync(
            reservationId,
            cancellationToken);

        if (reservation is null)
        {
            return Results.NotFound();
        }

        if (!await membershipAuthorizer.HasAccessAsync(
                principal,
                reservation.BuildingId,
                cancellationToken))
        {
            return Results.Problem(
                title: "You do not have access to this building.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var initiated = await creationService.InitiateMercadoPagoAsync(
                reservation,
                cancellationToken);

            return Results.Ok(new InitiatePaymentResponse(
                initiated.PaymentId,
                initiated.ProviderOrderId,
                initiated.CheckoutUrl,
                initiated.ReservationExpiresAtUtc));
        }
        catch (PaymentRequestException error)
        {
            return Results.Problem(
                title: "Unable to start this payment.",
                detail: error.Message,
                statusCode: error.StatusCode);
        }
    }

    private static async Task<IResult> GetPaymentAsync(
        Guid paymentId,
        System.Security.Claims.ClaimsPrincipal principal,
        AppDbContext dbContext,
        IReservationPaymentContract reservations,
        IBuildingMembershipAuthorizer membershipAuthorizer,
        CancellationToken cancellationToken)
    {
        var payment = await dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == paymentId,
                cancellationToken);

        if (payment is null)
        {
            return Results.NotFound();
        }

        var reservation = await reservations.GetPayableReservationAsync(
            payment.ReservationId,
            cancellationToken);

        if (reservation is null ||
            !await membershipAuthorizer.HasAccessAsync(
                principal,
                reservation.BuildingId,
                cancellationToken))
        {
            return Results.Problem(
                title: "You do not have access to this payment.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        // The browser's return from Mercado Pago lands on a screen that
        // asks THIS endpoint; the return URL itself never confirms anything.
        return Results.Ok(new PaymentResponse(
            payment.Id,
            payment.ReservationId,
            payment.Status.ToString(),
            payment.Amount,
            payment.Currency,
            payment.ApprovedAtUtc,
            payment.ReservationOutcome.ToString(),
            payment.RequiresManualReview));
    }

    private static async Task<IResult> ReceiveMercadoPagoWebhookAsync(
        HttpRequest request,
        IOptions<MercadoPagoOptions> options,
        PaymentReconciliationService reconciliation,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("MercadoPagoWebhook");
        var settings = options.Value;

        var xSignature = request.Headers["x-signature"].ToString();
        var xRequestId = request.Headers["x-request-id"].ToString();
        var dataId = request.Query["data.id"].ToString();

        var verification = MercadoPagoSignatureVerifier.Verify(
            xSignature,
            xRequestId,
            dataId,
            settings.WebhookSecret,
            timeProvider.GetUtcNow(),
            TimeSpan.FromSeconds(settings.WebhookToleranceSeconds));

        if (verification != SignatureVerificationResult.Valid)
        {
            // Only the failure category is logged — never the header, the
            // secret or the body. Nothing is stored or fetched.
            logger.LogWarning(
                "Rejected Mercado Pago webhook: {Reason}.",
                verification);

            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(dataId))
        {
            return Results.Ok();
        }

        var eventType = await ReadActionAsync(request, cancellationToken);

        var providerEventId = string.IsNullOrWhiteSpace(xRequestId)
            ? $"{dataId}:{eventType}:{MercadoPagoSignatureVerifier.TryGetTimestamp(xSignature)}"
            : xRequestId;

        try
        {
            await reconciliation.ProcessAsync(
                providerEventId,
                dataId,
                eventType,
                cancellationToken);

            return Results.Ok();
        }
        catch (MercadoPagoUnavailableException)
        {
            // Non-2xx makes Mercado Pago redeliver; the event was recorded
            // as unprocessed so the redelivery completes it.
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<string> ReadActionAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                request.Body,
                cancellationToken: cancellationToken);

            return document.RootElement.TryGetProperty("action", out var action)
                ? action.GetString() ?? "order"
                : "order";
        }
        catch (JsonException)
        {
            return "order";
        }
    }

    private sealed record InitiatePaymentResponse(
        Guid PaymentId,
        string ProviderOrderId,
        string CheckoutUrl,
        DateTimeOffset ReservationExpiresAtUtc);

    private sealed record PaymentResponse(
        Guid PaymentId,
        Guid ReservationId,
        string Status,
        decimal Amount,
        string Currency,
        DateTimeOffset? ApprovedAtUtc,
        string ReservationOutcome,
        bool RequiresManualReview);
}
