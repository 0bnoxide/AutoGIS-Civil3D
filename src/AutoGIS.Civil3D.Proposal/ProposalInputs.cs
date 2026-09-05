namespace AutoGIS.Civil3D.Proposal;

public sealed record ProposalInputs(
    string ClientName, string SiteName, int ProposalYear,
    string Orientation, string SheetSize,
    string? ClientNumber = null, string? ProjectNumber = null,
    string? ProposalNumber = null, string? SiteAddress = null,
    string? ProjectManager = null);
