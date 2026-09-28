using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Amenities.Application;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Pricing.Domain;
using ResidentialAmenities.Api.Modules.Reservations.Infrastructure.Persistence;

namespace ResidentialAmenities.Api.Modules.Pricing.Application;

/// <summary>
/// Creates effective-dated price rules (RF-019).
///
/// Semantics: a new rule takes effect at its <c>effectiveFrom</c> (default:
/// now; backdating is refused so the history of which price applied is never
/// rewritten). A rule of the same building + amenity + component + use type
/// that is still open at that instant is superseded (its effective end is set
/// to the new start) — its amount is never edited. If a rule for the same
/// combination already starts at or after the new start and would overlap the
/// new one, the request is rejected as ambiguous instead of leaving two
/// active rules.
///
/// The whole change and its audit entries commit in one transaction, under
/// the amenity advisory lock (which also orders it against reservation
/// creation, so a quote is always taken against a consistent rule set).
/// Amounts are configuration, never hardcoded, and no price is final until
/// issue #2 confirms them.
/// </summary>
public sealed class PricingAdminService(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IAuditRecorder auditRecorder,
    IAmenityAdminContract amenities) : IPricingAdminContract
{
    public const int MaxPageSize = 100;

    public async Task<AdminPriceRulePage> ListRulesAsync(
        Guid buildingId,
        Guid? amenityId,
        DateTimeOffset? activeAtUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = dbContext.PriceRules.AsNoTracking()
            .Where(rule => rule.BuildingId == buildingId);

        if (amenityId is { } amenity)
        {
            query = query.Where(rule => rule.AmenityId == amenity);
        }

        if (activeAtUtc is { } at)
        {
            var instant = at.ToUniversalTime();
            query = query.Where(rule =>
                rule.EffectiveFromUtc <= instant &&
                (rule.EffectiveToUtc == null || rule.EffectiveToUtc > instant));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rules = await query
            .OrderBy(rule => rule.AmenityId)
            .ThenBy(rule => rule.ComponentType)
            .ThenBy(rule => rule.UseType)
            .ThenByDescending(rule => rule.EffectiveFromUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new AdminPriceRulePage(rules.Select(ToView).ToList(), page, pageSize, totalCount);
    }

    public async Task<PriceRuleCreation> CreateRuleAsync(
        CreatePriceRuleCommand command,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var effectiveFrom = UtcInstant.Normalize(command.EffectiveFromUtc ?? nowUtc);
        var effectiveTo = command.EffectiveToUtc is { } to ? UtcInstant.Normalize(to) : (DateTimeOffset?)null;

        if (!Enum.IsDefined(command.ComponentType) || !Enum.IsDefined(command.UseType))
        {
            throw new PricingAdminException(
                "Unknown component or use type.",
                StatusCodes.Status400BadRequest);
        }

        if (command.EffectiveFromUtc is not null && effectiveFrom < nowUtc.AddMinutes(-1))
        {
            throw new PricingAdminException(
                "A price rule cannot be backdated; it takes effect from now or later.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var amenity = await amenities.GetSummaryAsync(command.AmenityId, cancellationToken);

        if (amenity is null || amenity.BuildingId != command.BuildingId)
        {
            throw new PricingAdminException(
                "The amenity was not found in this building.",
                StatusCodes.Status404NotFound);
        }

        PriceRule created;

        try
        {
            created = new PriceRule(
                Guid.NewGuid(),
                command.BuildingId,
                command.AmenityId,
                command.ComponentType,
                command.UseType,
                command.Currency,
                command.Amount,
                effectiveFrom,
                effectiveTo);
        }
        catch (ArgumentException error)
        {
            throw new PricingAdminException(error.Message, StatusCodes.Status400BadRequest);
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await ResourceAdvisoryLock.AcquireAsync(dbContext, [command.AmenityId], cancellationToken);

        var sameCombination = await dbContext.PriceRules
            .Where(rule =>
                rule.BuildingId == command.BuildingId &&
                rule.AmenityId == command.AmenityId &&
                rule.ComponentType == command.ComponentType &&
                rule.UseType == command.UseType)
            .ToListAsync(cancellationToken);

        // Anything that starts at/after the new start and overlaps its range
        // would leave two active rules for the same combination.
        var ambiguous = sameCombination.Any(rule =>
            rule.EffectiveFromUtc >= effectiveFrom &&
            (effectiveTo is null || rule.EffectiveFromUtc < effectiveTo));

        if (ambiguous)
        {
            throw new PricingAdminException(
                "Another price rule for the same amenity, component and use type starts " +
                "at or after this rule's start and overlaps it; the result would be ambiguous.",
                StatusCodes.Status409Conflict);
        }

        var toSupersede = sameCombination
            .Where(rule =>
                rule.EffectiveFromUtc < effectiveFrom &&
                (rule.EffectiveToUtc is null || rule.EffectiveToUtc > effectiveFrom))
            .ToList();

        foreach (var rule in toSupersede)
        {
            rule.Supersede(effectiveFrom);

            auditRecorder.Record(AuditRecord.ByUser(
                actorUserId,
                AuditAction.PriceRuleSuperseded,
                AuditTargetType.PriceRule,
                rule.Id,
                rule.BuildingId,
                RuleMetadata(rule)));
        }

        dbContext.PriceRules.Add(created);

        auditRecorder.Record(AuditRecord.ByUser(
            actorUserId,
            AuditAction.PriceRuleCreated,
            AuditTargetType.PriceRule,
            created.Id,
            created.BuildingId,
            RuleMetadata(created)));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PriceRuleCreation(ToView(created), toSupersede.Select(ToView).ToList());
    }

    private static AuditMetadata RuleMetadata(PriceRule rule) =>
        AuditMetadata.PriceRule(
            rule.AmenityId,
            rule.UseType.ToString(),
            rule.ComponentType.ToString(),
            rule.Currency,
            rule.Amount,
            rule.EffectiveFromUtc,
            rule.EffectiveToUtc);

    private static AdminPriceRule ToView(PriceRule rule) =>
        new(
            rule.Id,
            rule.BuildingId,
            rule.AmenityId,
            rule.ComponentType.ToString(),
            rule.UseType.ToString(),
            rule.Currency,
            rule.Amount,
            rule.EffectiveFromUtc,
            rule.EffectiveToUtc);
}
