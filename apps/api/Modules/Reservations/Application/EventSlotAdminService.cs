using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Payments.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Reservations.Domain;

namespace ResidentialAmenities.Api.Modules.Reservations.Application;

/// <summary>
/// Administrative Event slot configuration. Slots are activated/deactivated,
/// never deleted. A change only affects future bookings (creation and
/// reschedule validate against the slots that are active at that moment);
/// existing reservations are untouched. The seeded slot times remain
/// placeholders pending issue #2 — nothing here makes them final.
/// </summary>
public sealed class EventSlotAdminService(
    AppDbContext dbContext,
    IAuditRecorder auditRecorder) : IEventSlotAdminContract
{
    public async Task<IReadOnlyList<AdminEventSlot>> ListAsync(
        Guid buildingId,
        CancellationToken cancellationToken)
    {
        await EnsureBuildingAsync(buildingId, cancellationToken);

        var slots = await dbContext.EventSlotDefinitions
            .AsNoTracking()
            .Where(slot => slot.BuildingId == buildingId)
            .OrderBy(slot => slot.StartTime)
            .ThenBy(slot => slot.EndTime)
            .ToListAsync(cancellationToken);

        return slots.Select(ToView).ToList();
    }

    public async Task<AdminEventSlot> CreateAsync(
        Guid buildingId,
        string name,
        TimeOnly startTime,
        TimeOnly endTime,
        bool isOvernight,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await EnsureBuildingAsync(buildingId, cancellationToken);
        EnsureName(name);

        EventSlotDefinition slot;

        try
        {
            slot = new EventSlotDefinition(
                Guid.NewGuid(), buildingId, name, startTime, endTime, isOvernight);
        }
        catch (ArgumentException error)
        {
            throw new ReservationRequestException(error.Message, StatusCodes.Status400BadRequest);
        }

        dbContext.EventSlotDefinitions.Add(slot);

        auditRecorder.Record(AuditRecord.ByUser(
            actorUserId,
            AuditAction.EventSlotCreated,
            AuditTargetType.EventSlot,
            slot.Id,
            buildingId,
            AuditMetadata.EventSlotCreated(slot.Name, slot.StartTime, slot.EndTime)));

        await SaveAsync(cancellationToken);
        return ToView(slot);
    }

    public async Task<AdminEventSlot> UpdateAsync(
        Guid slotId,
        string name,
        TimeOnly startTime,
        TimeOnly endTime,
        bool isOvernight,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureName(name);
        var slot = await LoadAsync(slotId, cancellationToken);

        var previousName = slot.Name;
        var previousStart = slot.StartTime;
        var previousEnd = slot.EndTime;

        try
        {
            slot.Update(name, startTime, endTime, isOvernight);
        }
        catch (ArgumentException error)
        {
            throw new ReservationRequestException(error.Message, StatusCodes.Status400BadRequest);
        }

        if (slot.Name == previousName && slot.StartTime == previousStart && slot.EndTime == previousEnd)
        {
            return ToView(slot);
        }

        auditRecorder.Record(AuditRecord.ByUser(
            actorUserId,
            AuditAction.EventSlotUpdated,
            AuditTargetType.EventSlot,
            slot.Id,
            slot.BuildingId,
            AuditMetadata.EventSlotUpdated(
                previousName,
                previousStart,
                previousEnd,
                slot.Name,
                slot.StartTime,
                slot.EndTime,
                slot.IsActive)));

        await SaveAsync(cancellationToken);
        return ToView(slot);
    }

    public async Task<AdminEventSlot> DeactivateAsync(
        Guid slotId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var slot = await LoadAsync(slotId, cancellationToken);

        // Idempotent: only a real transition is audited.
        if (slot.Deactivate())
        {
            auditRecorder.Record(AuditRecord.ByUser(
                actorUserId,
                AuditAction.EventSlotDeactivated,
                AuditTargetType.EventSlot,
                slot.Id,
                slot.BuildingId,
                AuditMetadata.EventSlotState(slot.Name, active: false)));

            await SaveAsync(cancellationToken);
        }

        return ToView(slot);
    }

    public async Task<AdminEventSlot> ActivateAsync(
        Guid slotId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var slot = await LoadAsync(slotId, cancellationToken);

        if (slot.Activate())
        {
            auditRecorder.Record(AuditRecord.ByUser(
                actorUserId,
                AuditAction.EventSlotUpdated,
                AuditTargetType.EventSlot,
                slot.Id,
                slot.BuildingId,
                AuditMetadata.EventSlotState(slot.Name, active: true)));

            await SaveAsync(cancellationToken);
        }

        return ToView(slot);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (UniqueViolation.IsUniqueViolation(error))
        {
            throw new ReservationRequestException(
                "An event slot with the same times already exists for this building.",
                StatusCodes.Status409Conflict);
        }
    }

    private async Task<EventSlotDefinition> LoadAsync(Guid slotId, CancellationToken cancellationToken) =>
        await dbContext.EventSlotDefinitions.SingleOrDefaultAsync(
            slot => slot.Id == slotId,
            cancellationToken)
        ?? throw new ReservationRequestException(
            "Event slot not found.",
            StatusCodes.Status404NotFound);

    private async Task EnsureBuildingAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Buildings.AnyAsync(building => building.Id == buildingId, cancellationToken))
        {
            throw new ReservationRequestException(
                "Building not found.",
                StatusCodes.Status404NotFound);
        }
    }

    private static void EnsureName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
        {
            throw new ReservationRequestException(
                "A name of at most 80 characters is required.",
                StatusCodes.Status400BadRequest);
        }
    }

    private static AdminEventSlot ToView(EventSlotDefinition slot) =>
        new(slot.Id, slot.BuildingId, slot.Name, slot.StartTime, slot.EndTime, slot.IsOvernight, slot.IsActive);
}
