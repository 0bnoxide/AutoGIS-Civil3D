using System.Collections.Immutable;

namespace AutoGIS.Civil3D.Proposal;

public sealed record ProposalIssue(string Code, string Message, string? Location = null);
public sealed record ManifestResult(StandardsManifest? Manifest, ImmutableArray<ProposalIssue> Issues);

public static class ProposalIssueCodes
{
    public const string InvalidJson = "PROPOSAL_INVALID_JSON";
    public const string DuplicateProperty = "PROPOSAL_DUPLICATE_PROPERTY";
    public const string UnsupportedVersion = "PROPOSAL_UNSUPPORTED_VERSION";
    public const string InvalidManifest = "PROPOSAL_INVALID_MANIFEST";
    public const string UnsafePath = "PROPOSAL_UNSAFE_PATH";
    public const string OutputCollision = "PROPOSAL_OUTPUT_COLLISION";
    public const string InvalidReferences = "PROPOSAL_INVALID_REFERENCES";
    public const string InvalidInputs = "PROPOSAL_INVALID_INPUTS";
    public const string UnsupportedSheet = "PROPOSAL_UNSUPPORTED_SHEET";
    public const string UnsupportedYear = "PROPOSAL_UNSUPPORTED_YEAR";
}
