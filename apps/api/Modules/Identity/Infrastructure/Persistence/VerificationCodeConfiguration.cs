using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Identity.Domain;

namespace ResidentialAmenities.Api.Modules.Identity.Infrastructure.Persistence;

public sealed class VerificationCodeConfiguration : IEntityTypeConfiguration<VerificationCode>
{
    public void Configure(EntityTypeBuilder<VerificationCode> builder)
    {
        builder.ToTable("VerificationCodes");

        builder.HasKey(code => code.Id);

        builder.Property(code => code.Purpose)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(code => code.CodeHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(code => new { code.UserId, code.Purpose, code.ConsumedAtUtc });
    }
}
