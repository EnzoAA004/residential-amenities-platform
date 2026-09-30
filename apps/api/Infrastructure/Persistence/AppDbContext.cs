using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Modules.Amenities.Domain;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using ResidentialAmenities.Api.Modules.Media.Domain;
using ResidentialAmenities.Api.Modules.Messaging.Domain;
using ResidentialAmenities.Api.Modules.Notifications.Domain;
using ResidentialAmenities.Api.Modules.Identity.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Payments.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reports.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Infrastructure.Persistence;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options)
    : IdentityDbContext<UserAccount, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Building> Buildings => Set<Building>();

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();

    public DbSet<ResidentMembership> ResidentMemberships =>
        Set<ResidentMembership>();

    public DbSet<Amenity> Amenities => Set<Amenity>();

    public DbSet<AmenityAvailabilityWindow> AmenityAvailabilityWindows =>
        Set<AmenityAvailabilityWindow>();

    public DbSet<AmenityUnavailablePeriod> AmenityUnavailablePeriods =>
        Set<AmenityUnavailablePeriod>();

    public DbSet<PriceRule> PriceRules => Set<PriceRule>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<ReservationResource> ReservationResources =>
        Set<ReservationResource>();

    public DbSet<ReservationPriceLine> ReservationPriceLines =>
        Set<ReservationPriceLine>();

    public DbSet<EventSlotDefinition> EventSlotDefinitions =>
        Set<EventSlotDefinition>();

    public DbSet<ReservationEntryPoint> ReservationEntryPoints =>
        Set<ReservationEntryPoint>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentProviderEvent> PaymentProviderEvents =>
        Set<PaymentProviderEvent>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<NotificationSubscription> NotificationSubscriptions =>
        Set<NotificationSubscription>();

    public DbSet<ReservationMessage> ReservationMessages =>
        Set<ReservationMessage>();

    public DbSet<MediaAttachment> MediaAttachments =>
        Set<MediaAttachment>();

    public DbSet<IncidentReport> IncidentReports =>
        Set<IncidentReport>();

    public DbSet<IncidentReportAttachment> IncidentReportAttachments =>
        Set<IncidentReportAttachment>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditLogsAreAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureAuditLogsAreAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // The audit trail is append-only: the application never rewrites history.
    private void EnsureAuditLogsAreAppendOnly()
    {
        if (ChangeTracker.Entries<AuditLog>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "AuditLog entries are append-only and cannot be modified or deleted.");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly);

        IdentityPersistenceModel.Configure(modelBuilder);
    }
}
