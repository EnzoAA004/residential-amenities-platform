namespace ResidentialAmenities.Api.Modules.Reports.Domain;

/// <summary>
/// Issue #91: a minimal review workflow, not a business-rule engine. There
/// is deliberately no automatic transition and no financial consequence
/// attached to any status — status only reflects whether an Administrator
/// has looked at the report yet.
/// </summary>
public enum IncidentReportStatus
{
    Open = 0,
    Reviewed = 1,
    Resolved = 2
}
