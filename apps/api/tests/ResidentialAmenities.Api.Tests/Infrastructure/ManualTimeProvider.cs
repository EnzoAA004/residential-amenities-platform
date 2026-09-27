namespace ResidentialAmenities.Api.Tests.Infrastructure;

/// <summary>
/// A small, explicit test clock (per issue #23 guidance) so time-dependent
/// tests are deterministic — no <c>Thread.Sleep</c>, no reliance on wall
/// clock timing.
/// </summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void SetUtcNow(DateTimeOffset now) => _now = now;

    public void Advance(TimeSpan by) => _now += by;
}
