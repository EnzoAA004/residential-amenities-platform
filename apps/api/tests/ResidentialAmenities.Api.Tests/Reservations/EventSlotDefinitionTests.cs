using ResidentialAmenities.Api.Modules.Reservations.Domain;
using Xunit;

namespace ResidentialAmenities.Api.Tests.Reservations;

public sealed class EventSlotDefinitionTests
{
    [Fact]
    public void Matches_ExactBoundaries_ReturnsTrue()
    {
        var slot = new EventSlotDefinition(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Afternoon",
            new TimeOnly(14, 0),
            new TimeOnly(19, 0));

        Assert.True(slot.Matches(new TimeOnly(14, 0), new TimeOnly(19, 0)));
    }

    [Theory]
    [InlineData(13, 59, 19, 0)]
    [InlineData(14, 0, 19, 1)]
    [InlineData(14, 1, 18, 59)]
    public void Matches_AnyBoundaryOff_ReturnsFalse(
        int startHour,
        int startMinute,
        int endHour,
        int endMinute)
    {
        var slot = new EventSlotDefinition(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Afternoon",
            new TimeOnly(14, 0),
            new TimeOnly(19, 0));

        Assert.False(slot.Matches(
            new TimeOnly(startHour, startMinute),
            new TimeOnly(endHour, endMinute)));
    }

    [Fact]
    public void EndTimeNotAfterStartTime_Throws()
    {
        Assert.Throws<ArgumentException>(() => new EventSlotDefinition(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Invalid",
            new TimeOnly(19, 0),
            new TimeOnly(14, 0)));
    }
}
