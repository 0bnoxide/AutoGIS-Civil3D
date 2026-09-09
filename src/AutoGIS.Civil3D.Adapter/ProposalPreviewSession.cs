using AutoGIS.Civil3D.Proposal;
using System.Security.Cryptography;

namespace AutoGIS.Civil3D.Adapter;

internal sealed class ProposalPreviewSession
{
    public ProposalPlan Plan { get; }
    private readonly ProposalApproval approval;
    private readonly ProposalInputs originalInputs;
    private readonly string originalManifestPath;
    private volatile bool valid = true;

    private ProposalPreviewSession(ProposalPlan plan, ProposalInputs inputs, string manifestPath, Dictionary<string, string> fingerprints)
    {
        Plan = plan;
        originalInputs = inputs;
        originalManifestPath = manifestPath;
        approval = new(plan.ToJson(), fingerprints);
    }

    public static ProposalPreviewSession Create(ProposalInputs inputs, string manifestPath)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        var manifest = StandardsManifest.Parse(manifestBytes);
        if (manifest.Manifest is null) throw new InvalidDataException(Describe(manifest.Issues));
        var result = ProposalPlanner.Build(inputs, manifest.Manifest);
        if (result.Plan is null) throw new InvalidDataException(Describe(result.Issues));
        // Hash the same manifest bytes that were parsed, never reopen it for expected values.
        var fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [manifestPath] = Convert.ToHexString(SHA256.HashData(manifestBytes))
        };
        foreach (string template in result.Plan.Actions.Select(action => action.Data switch
        {
            ModelDrawingData model => model.Template,
            SheetDrawingData sheet => sheet.Profile.Template,
            _ => null
        }).OfType<string>().Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            fingerprints.Add(template, Fingerprint(template));
        return new(result.Plan, inputs, manifestPath, fingerprints);
    }

    public ProposalApproval? TryApprove(ProposalInputs inputs, string manifestPath) =>
        IsCurrent(inputs, manifestPath) ? approval : null;

    public bool IsCurrent(ProposalInputs inputs, string manifestPath)
    {
        if (!valid) return false;
        try
        {
            if (inputs != originalInputs ||
                !string.Equals(Path.GetFullPath(manifestPath), originalManifestPath, StringComparison.OrdinalIgnoreCase) ||
                !approval.DependencyFingerprints.All(pair => valid && Fingerprint(pair.Key) == pair.Value))
                valid = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            valid = false;
        }
        return valid;
    }

    public void Invalidate() => valid = false;

    private static string Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Describe(IEnumerable<ProposalIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));
}
