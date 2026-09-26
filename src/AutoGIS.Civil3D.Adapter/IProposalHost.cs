using AutoGIS.Civil3D.Proposal;

namespace AutoGIS.Civil3D.Adapter;

public interface IProposalHost
{
    PreflightReport Inspect(ProposalPlan plan);
    // Creation actions must use exclusive no-overwrite leaves; a failed Apply may leave ambiguous output for inspection.
    void Apply(PlannedAction action, string stagingRoot);
    void CloseCreatedArtifacts();
    VerificationReport Verify(ProposalPlan plan, string artifactRoot);
}
