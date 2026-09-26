using System.Collections.Immutable;

namespace AutoGIS.Civil3D.Proposal;

public sealed class PreflightReport
{
    public ImmutableArray<ProposalIssue> Issues { get; }

    public PreflightReport(IEnumerable<ProposalIssue> issues) => Issues = issues.ToImmutableArray();
}

public static class ExecutionIssueCodes
{
    public const string ApprovalRequired = "PROPOSAL_APPROVAL_REQUIRED";
    public const string StaleApproval = "PROPOSAL_STALE_APPROVAL";
    public const string TargetExists = "PROPOSAL_TARGET_EXISTS";
    public const string UnsafeExecutionPath = "PROPOSAL_UNSAFE_EXECUTION_PATH";
    public const string PreflightFailed = "PROPOSAL_PREFLIGHT_FAILED";
    public const string VerificationFailed = "PROPOSAL_VERIFICATION_FAILED";
    public const string ExecutionFailed = "PROPOSAL_EXECUTION_FAILED";
}
