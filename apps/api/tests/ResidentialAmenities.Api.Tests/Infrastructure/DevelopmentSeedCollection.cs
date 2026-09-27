using Xunit;

namespace ResidentialAmenities.Api.Tests.Infrastructure;

/// <summary>
/// Every test class that spins up a WebApplicationFactory shares this
/// collection so their app-startup instances never run concurrently.
/// DevelopmentDataSeeder seeds the shared pilot Building/Units/Amenities
/// with a check-then-insert pattern that is not safe against two factories
/// racing to seed the same rows against the same database.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DevelopmentSeedCollection
{
    public const string Name = "Development seed";
}
