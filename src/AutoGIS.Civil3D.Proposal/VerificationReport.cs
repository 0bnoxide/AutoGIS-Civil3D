using System.Collections.Immutable;

namespace AutoGIS.Civil3D.Proposal;

public sealed class VerificationReport
{
    public ImmutableArray<string> VerifiedArtifacts { get; }
    public ImmutableArray<ProposalIssue> FailedChecks { get; }
    public ImmutableArray<string> ManualSteps { get; }

    public VerificationReport(IEnumerable<string> verifiedArtifacts, IEnumerable<ProposalIssue> failedChecks,
        IEnumerable<string> manualSteps)
    {
        VerifiedArtifacts = verifiedArtifacts.ToImmutableArray();
        FailedChecks = failedChecks.ToImmutableArray();
        ManualSteps = manualSteps.ToImmutableArray();
    }
}
