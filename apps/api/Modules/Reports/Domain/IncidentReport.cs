namespace ResidentialAmenities.Api.Modules.Reports.Domain;

/// <summary>
/// An incident/damage report (issue #91, DEC-014/OQ-012): evidence for an
/// Administrator to review manually, never an automatic penalty/fine
/// engine. Deliberately a separate aggregate from #78's
/// <c>ReservationMessage</c> — a report is building-scoped (so an
/// Administrator can review every report for their building, not just
/// building-wide chat) with an <em>optional</em> reservation context,
/// while a reservation message is always reservation-scoped and never
/// visible to an Administrator outside that one reservation. Nothing here
/// computes or stores a monetary consequence.
/// </summary>
public sealed class IncidentReport
{
    public const int MaxContentLength = 2000;

    /// <summary>
    /// A deliberately small, documented cap — not a technical limitation of
    /// the storage layer, just a sane bound so one report cannot balloon
    /// into an unbounded evidence dump.
    /// </summary>
    public const int MaxAttachments = 5;

    private readonly List<IncidentReportAttachment> _attachments = [];

    private IncidentReport()
    {
    }

    public IncidentReport(
        Guid id,
        Guid buildingId,
        Guid reportedByUserId,
        Guid? reservationId,
        string content,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Report id is required.", nameof(id));
        }

        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException("Building id is required.", nameof(buildingId));
        }

        if (reportedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Reporter user id is required.", nameof(reportedByUserId));
        }

        Id = id;
        BuildingId = buildingId;
        ReportedByUserId = reportedByUserId;
        ReservationId = reservationId;
        Content = NormalizeContent(content);
        Status = IncidentReportStatus.Open;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid BuildingId { get; private set; }

    public Guid ReportedByUserId { get; private set; }

    /// <summary>Optional context — a report is not required to be about a specific reservation.</summary>
    public Guid? ReservationId { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public IncidentReportStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<IncidentReportAttachment> Attachments => _attachments;

    public IncidentReportAttachment AddAttachment(Guid id, Guid mediaAttachmentId)
    {
        if (_attachments.Count >= MaxAttachments)
        {
            throw new InvalidOperationException(
                $"A report may not have more than {MaxAttachments} attachments.");
        }

        var attachment = new IncidentReportAttachment(id, Id, mediaAttachmentId);
        _attachments.Add(attachment);
        return attachment;
    }

    /// <returns>true only when this call actually changed the status.</returns>
    public bool UpdateStatus(IncidentReportStatus status)
    {
        if (Status == status)
        {
            return false;
        }

        Status = status;
        return true;
    }

    private static string NormalizeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Report content is required.", nameof(content));
        }

        var trimmed = content.Trim();

        return trimmed.Length > MaxContentLength
            ? throw new ArgumentException(
                $"Report content must be at most {MaxContentLength} characters.",
                nameof(content))
            : trimmed;
    }
}
