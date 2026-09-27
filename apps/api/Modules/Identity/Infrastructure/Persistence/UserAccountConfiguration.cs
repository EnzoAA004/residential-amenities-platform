using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Identity.Domain;

namespace ResidentialAmenities.Api.Modules.Identity.Infrastructure.Persistence;

public sealed class UserAccountConfiguration :
    IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("UserAccounts");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Email)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(user => user.NormalizedEmail)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(user => user.DisplayName)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(user => user.IsActive)
            .IsRequired();

        builder.Property(user => user.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique();
    }
}
