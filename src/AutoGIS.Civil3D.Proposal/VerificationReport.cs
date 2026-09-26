using System.Collections.Immutable;

namespace AutoGIS.Civil3D.Proposal;

public sealed class VerificationReport
{
    public ImmutableArray<string> VerifiedArtifacts { get; }
    public ImmutableDictionary<string, string> ArtifactSha256 { get; }
    public ImmutableArray<ProposalIssue> FailedChecks { get; }
    public ImmutableArray<string> ManualSteps { get; }

    public VerificationReport(IEnumerable<string> verifiedArtifacts, IEnumerable<ProposalIssue> failedChecks,
        IEnumerable<string> manualSteps, IEnumerable<KeyValuePair<string, string>> artifactSha256)
    {
        VerifiedArtifacts = verifiedArtifacts.ToImmutableArray();
        ArtifactSha256 = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, artifactSha256);
        FailedChecks = failedChecks.ToImmutableArray();
        ManualSteps = manualSteps.ToImmutableArray();
    }
}
