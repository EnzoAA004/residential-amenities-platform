using ResidentialAmenities.Api.Modules.Pricing.Domain;

namespace ResidentialAmenities.Api.Modules.Pricing.Application;

public sealed class PricingAdminException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed record AdminPriceRule(
    Guid Id,
    Guid BuildingId,
    Guid AmenityId,
    string ComponentType,
    string UseType,
    string Currency,
    decimal Amount,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc);

public sealed record AdminPriceRulePage(
    IReadOnlyList<AdminPriceRule> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record CreatePriceRuleCommand(
    Guid BuildingId,
    Guid AmenityId,
    PriceComponentType ComponentType,
    ReservationUseType UseType,
    string Currency,
    decimal Amount,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc);

public sealed record PriceRuleCreation(
    AdminPriceRule Created,
    IReadOnlyList<AdminPriceRule> Superseded);

/// <summary>
/// Price configuration owned by Pricing. Rules are effective-dated and never
/// edited: a new rule closes the one it replaces, and existing reservation
/// snapshots are never touched (RB-007, RB-008).
/// </summary>
public interface IPricingAdminContract
{
    Task<AdminPriceRulePage> ListRulesAsync(
        Guid buildingId,
        Guid? amenityId,
        DateTimeOffset? activeAtUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<PriceRuleCreation> CreateRuleAsync(
        CreatePriceRuleCommand command,
        Guid actorUserId,
        CancellationToken cancellationToken);
}
