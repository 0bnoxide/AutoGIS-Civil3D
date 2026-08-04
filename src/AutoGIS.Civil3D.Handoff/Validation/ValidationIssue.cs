namespace AutoGIS.Civil3D.Handoff.Validation;

public sealed record ValidationIssue(
    string Code,
    IssueSeverity Severity,
    string Message,
    string? Location = null);
