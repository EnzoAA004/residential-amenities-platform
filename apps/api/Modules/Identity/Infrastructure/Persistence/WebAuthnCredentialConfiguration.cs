using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialAmenities.Api.Modules.Identity.Domain;

namespace ResidentialAmenities.Api.Modules.Identity.Infrastructure.Persistence;

public sealed class WebAuthnCredentialConfiguration : IEntityTypeConfiguration<WebAuthnCredential>
{
    public void Configure(EntityTypeBuilder<WebAuthnCredential> builder)
    {
        builder.ToTable("WebAuthnCredentials");

        builder.HasKey(credential => credential.Id);

        builder.Property(credential => credential.CredentialId).IsRequired();

        builder.Property(credential => credential.PublicKey).IsRequired();

        builder.HasIndex(credential => credential.CredentialId).IsUnique();

        builder.HasIndex(credential => credential.UserId);
    }
}
