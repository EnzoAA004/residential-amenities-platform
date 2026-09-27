using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Buildings.Domain;

namespace ResidentialAmenities.Api.Modules.Buildings.Infrastructure.Persistence;

public sealed class ResidentMembershipConfiguration :
    IEntityTypeConfiguration<ResidentMembership>
{
    public void Configure(EntityTypeBuilder<ResidentMembership> builder)
    {
        builder.ToTable("ResidentMemberships");

        builder.HasKey(membership => membership.Id);

        builder.Property(membership => membership.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(membership => membership.StartedAtUtc)
            .IsRequired();

        builder.HasIndex(membership => new
        {
            membership.BuildingId,
            membership.UnitId,
            membership.UserId
        }).IsUnique();

        builder.HasOne(membership => membership.Unit)
            .WithMany(unit => unit.Memberships)
            .HasForeignKey(membership => new
            {
                membership.BuildingId,
                membership.UnitId
            })
            .HasPrincipalKey(unit => new
            {
                unit.BuildingId,
                unit.Id
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(membership => membership.User)
            .WithMany(user => user.Memberships)
            .HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
