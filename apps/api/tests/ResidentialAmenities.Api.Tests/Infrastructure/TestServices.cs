using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Payments.Application;
using ResidentialAmenities.Api.Modules.Reservations.Application;

namespace ResidentialAmenities.Api.Tests.Infrastructure;

/// <summary>
/// Builds application services outside the DI container (separate DbContexts
/// and explicit clocks, for concurrency tests) wired exactly like production,
/// including audit recording.
/// </summary>
public static class TestServices
{
    public static IAuditRecorder Audit(AppDbContext db, TimeProvider clock) =>
        new AuditRecorder(db, clock, new HttpContextAccessor());

    public static ReservationPaymentContract Contract(AppDbContext db, TimeProvider clock) =>
        new(db, clock, Audit(db, clock));

    public static ReservationExpirationService Expiration(AppDbContext db, TimeProvider clock) =>
        new(db, clock, Audit(db, clock));

    public static CashPaymentService Cash(AppDbContext db, TimeProvider clock)
    {
        var contract = Contract(db, clock);
        var audit = Audit(db, clock);

        return new CashPaymentService(
            db,
            contract,
            new PaymentOutcomeRecorder(
                db, contract, audit, clock, NullLogger<PaymentOutcomeRecorder>.Instance),
            audit,
            clock);
    }
}
