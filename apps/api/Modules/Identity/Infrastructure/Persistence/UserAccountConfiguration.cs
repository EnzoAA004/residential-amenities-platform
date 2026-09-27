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

        builder.Property(user => user.Email)
            .HasMaxLength(320);

        builder.Property(user => user.NormalizedEmail)
            .HasMaxLength(320);

        builder.Property(user => user.UserName)
            .HasMaxLength(320);

        builder.Property(user => user.NormalizedUserName)
            .HasMaxLength(320);

        builder.Property(user => user.DisplayName)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(user => user.IsActive)
            .IsRequired();

        builder.Property(user => user.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("UX_UserAccounts_NormalizedEmail");
    }
}
