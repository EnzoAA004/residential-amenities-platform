namespace ResidentialAmenities.Api.Modules.Reports.Domain;

/// <summary>
/// Links an <see cref="IncidentReport"/> to a file already uploaded through
/// #92's Media module (<c>MediaAttachment</c>). This module never
/// duplicates upload/storage/validation logic — it only stores the
/// reference, and the endpoint is responsible for checking that the
/// referenced attachment was actually uploaded by the same caller before
/// linking it (see <c>ReportsModule</c>).
/// </summary>
public sealed class IncidentReportAttachment
{
    private IncidentReportAttachment()
    {
    }

    public IncidentReportAttachment(Guid id, Guid incidentReportId, Guid mediaAttachmentId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Attachment link id is required.", nameof(id));
        }

        if (incidentReportId == Guid.Empty)
        {
            throw new ArgumentException("Incident report id is required.", nameof(incidentReportId));
        }

        if (mediaAttachmentId == Guid.Empty)
        {
            throw new ArgumentException("Media attachment id is required.", nameof(mediaAttachmentId));
        }

        Id = id;
        IncidentReportId = incidentReportId;
        MediaAttachmentId = mediaAttachmentId;
    }

    public Guid Id { get; private set; }

    public Guid IncidentReportId { get; private set; }

    public Guid MediaAttachmentId { get; private set; }
}
