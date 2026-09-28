namespace ResidentialAmenities.Api.Infrastructure.Persistence;

public static class UtcInstant
{
    /// <summary>
    /// Normalizes a client-sent instant to what PostgreSQL will actually store:
    /// UTC (Npgsql only writes zero-offset values) and microsecond precision
    /// (timestamptz drops sub-microsecond ticks). Comparing before/after
    /// persistence is then exact.
    /// </summary>
    public static DateTimeOffset Normalize(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}
