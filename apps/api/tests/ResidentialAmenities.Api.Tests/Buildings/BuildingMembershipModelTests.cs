using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity.Domain;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Buildings;

public sealed class BuildingMembershipModelTests
{
    [Fact]
    public void UnitLabel_IsUniqueWithinBuilding()
    {
        using var dbContext = CreateDbContext();

        var entityType = dbContext.Model.FindEntityType(typeof(Unit));
        Assert.NotNull(entityType);

        var index = entityType.GetIndexes().Single(candidate =>
            candidate.Properties.Select(property => property.Name)
                .SequenceEqual(
                    new[]
                    {
                        nameof(Unit.BuildingId),
                        nameof(Unit.Label)
                    }));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Membership_ReferencesUnitThroughBuildingScopedForeignKey()
    {
        using var dbContext = CreateDbContext();

        var entityType =
            dbContext.Model.FindEntityType(typeof(ResidentMembership));

        Assert.NotNull(entityType);

        var unitForeignKey = entityType.GetForeignKeys().Single(candidate =>
            candidate.PrincipalEntityType.ClrType == typeof(Unit));

        Assert.Equal(
            new[]
            {
                nameof(ResidentMembership.BuildingId),
                nameof(ResidentMembership.UnitId)
            },
            unitForeignKey.Properties.Select(property => property.Name));

        Assert.Equal(
            new[]
            {
                nameof(Unit.BuildingId),
                nameof(Unit.Id)
            },
            unitForeignKey.PrincipalKey.Properties
                .Select(property => property.Name));
    }

    [Fact]
    public void UserEmail_IsNormalizedForUniqueIdentityLookup()
    {
        var user = new UserAccount(
            Guid.NewGuid(),
            "  resident@example.com  ",
            "Resident");

        Assert.Equal("resident@example.com", user.Email);
        Assert.Equal("RESIDENT@EXAMPLE.COM", user.NormalizedEmail);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=model_tests;" +
                "Username=model_tests;Password=model_tests")
            .Options;

        return new AppDbContext(options);
    }
}
