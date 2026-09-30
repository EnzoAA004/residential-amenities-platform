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

    [Fact]
    public void Overnight_EndBeforeStart_IsAccepted()
    {
        // DEC-014/OQ-002: the night Event slot is 20:00 -> 03:00 next day.
        var slot = new EventSlotDefinition(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Night",
            new TimeOnly(20, 0),
            new TimeOnly(3, 0),
            isOvernight: true);

        Assert.True(slot.IsOvernight);
        Assert.True(slot.Matches(new TimeOnly(20, 0), new TimeOnly(3, 0)));
    }

    [Fact]
    public void Overnight_WithEndAfterStart_Throws()
    {
        // isOvernight: true means "end is on the next day" — a same-day
        // range marked overnight is a contradiction, not a valid input.
        Assert.Throws<ArgumentException>(() => new EventSlotDefinition(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Invalid",
            new TimeOnly(14, 0),
            new TimeOnly(19, 0),
            isOvernight: true));
    }

    [Fact]
    public void NotOvernight_EndBeforeStart_StillThrows()
    {
        // The generic end<=start rejection is unchanged for anything not
        // explicitly marked overnight.
        Assert.Throws<ArgumentException>(() => new EventSlotDefinition(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Invalid",
            new TimeOnly(19, 0),
            new TimeOnly(14, 0),
            isOvernight: false));
    }
}
