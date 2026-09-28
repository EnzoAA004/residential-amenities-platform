using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Payments.Domain;

namespace ResidentialAmenities.Api.Modules.Payments.Infrastructure.Persistence;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Method)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(payment => payment.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(payment => payment.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(payment => payment.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(payment => payment.IdempotencyKey)
            .HasMaxLength(64);

        builder.Property(payment => payment.RequestedExpirationTime)
            .HasMaxLength(40);

        builder.Property(payment => payment.ProviderOrderId)
            .HasMaxLength(100);

        builder.Property(payment => payment.CheckoutUrl)
            .HasMaxLength(2048);

        builder.Property(payment => payment.ProviderStatus)
            .HasMaxLength(100);

        builder.Property(payment => payment.ProviderStatusDetail)
            .HasMaxLength(100);

        builder.Property(payment => payment.ReservationOutcome)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        // Cash-only facts; NULL for other methods.
        builder.Property(payment => payment.CashDeclaredAtUtc);
        builder.Property(payment => payment.CashConfirmedAtUtc);
        builder.Property(payment => payment.CashConfirmedByUserId);

        builder.Property(payment => payment.CreatedAtUtc).IsRequired();
        builder.Property(payment => payment.UpdatedAtUtc).IsRequired();

        builder.Ignore(payment => payment.ExternalReference);
        builder.Ignore(payment => payment.RequiresManualReview);

        // Unique when present (NULLs are distinct in PostgreSQL, so cash payments
        // — which have no key — never collide).
        builder.HasIndex(payment => payment.IdempotencyKey).IsUnique();

        // PostgreSQL treats NULLs as distinct, so many not-yet-created
        // provider orders coexist while a real order id stays unique.
        builder.HasIndex(payment => payment.ProviderOrderId).IsUnique();

        // At most one live attempt per reservation: two concurrent "start
        // payment" requests cannot fork into two provider orders, and a
        // paid reservation cannot be charged again. Rejected/Cancelled
        // attempts drop out of the index, so a genuinely new attempt is
        // allowed while the hold lasts. No FK to Reservations (module
        // boundary: referenced by id only).
        builder.HasIndex(payment => payment.ReservationId)
            .IsUnique()
            .HasFilter("\"Status\" IN ('Created', 'Pending', 'Approved')")
            .HasDatabaseName("UX_Payments_ActivePerReservation");

        builder.HasIndex(payment => payment.ReservationId);
    }
}
