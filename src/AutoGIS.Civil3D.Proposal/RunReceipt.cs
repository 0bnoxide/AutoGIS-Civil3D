using System.Collections.Immutable;

namespace AutoGIS.Civil3D.Proposal;

/// <summary>Observed outcome of one run. Outcome is Cancelled, Refused, Failed, or Succeeded.</summary>
public sealed record RunReceipt(
    Guid RunId,
    DateTimeOffset StartedAtUtc,
    string PlanSha256,
    string Outcome,
    string FinalRoot,
    string? ReceiptPath,
    ImmutableArray<string> VerifiedArtifacts,
    ImmutableArray<string> ManualSteps,
    string? FailedActionId,
    ImmutableArray<ProposalIssue> FailedChecks,
    string? FailureDetail,
    ImmutableArray<string> CleanupFailures,
    string? ReportingFailure,
    string? RetainedStagingRoot);
