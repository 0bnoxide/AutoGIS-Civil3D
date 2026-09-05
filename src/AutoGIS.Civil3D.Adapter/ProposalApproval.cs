using System.Collections.ObjectModel;

namespace AutoGIS.Civil3D.Adapter;

public sealed class ProposalApproval
{
    public string PlanJson { get; }
    public IReadOnlyDictionary<string, string> DependencyFingerprints { get; }

    internal ProposalApproval(string planJson, Dictionary<string, string> dependencyFingerprints)
    {
        PlanJson = planJson;
        DependencyFingerprints = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(dependencyFingerprints, StringComparer.OrdinalIgnoreCase));
    }
}
