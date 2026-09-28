using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ResidentialAmenities.Api.Modules.Payments.Infrastructure.Persistence;

internal static class UniqueViolation
{
    /// <summary>PostgreSQL SQLSTATE 23505 (unique_violation).</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
