using AutoGIS.Civil3D.Proposal;

namespace AutoGIS.Civil3D.Adapter;

public sealed class NativeProposalHost : IProposalHost
{
    private ProposalPlan? inspectedPlan;
    private string? appliedRoot;
    private readonly Dictionary<string, string> createdSha256 = new(StringComparer.OrdinalIgnoreCase);
    private readonly string localData;

    public NativeProposalHost() : this(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) { }

    internal NativeProposalHost(string localData) => this.localData = localData;

    public PreflightReport Inspect(ProposalPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        inspectedPlan = null;
        appliedRoot = null;
        createdSha256.Clear();
        try { ProposalFiles.ProbeWritable(DrawingWriter.ScratchRoot(localData), Guid.NewGuid()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new([new(ex is ProposalConditionException condition ? condition.Code : ExecutionIssueCodes.PreflightFailed,
                @"The drawing scratch folder %LOCALAPPDATA%\AutoGIS\scratch must be creatable and writable by this user " +
                $"before a proposal runs: {ex.Message}")]);
        }
        foreach (var action in plan.Actions)
        {
            if (!IsModelAction(action))
                return new([new(ExecutionIssueCodes.PreflightFailed,
                    $"Native proposal operation is not implemented: {action.Id} ({action.Operation}).", action.RelativePath)]);
        }
        inspectedPlan = plan;
        return new([]);
    }

    public void Apply(PlannedAction action, string stagingRoot)
    {
        if (!IsModelAction(action))
            throw new NotSupportedException($"Native proposal operation is not implemented: {action.Operation}.");
        var plan = inspectedPlan ?? throw new InvalidOperationException("Native plan inspection must pass before applying actions.");
        ValidateModelAction(plan, action, stagingRoot);
        string root = ProposalFiles.Full(stagingRoot);
        if (appliedRoot is not null && !string.Equals(appliedRoot, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Native plan actions must use one staging root.");
        appliedRoot = root;
        switch (action.Operation)
        {
            case ProposalOperation.CreateFolder:
                if (action.RelativePath.Length > 0)
                    ProposalFiles.ReserveStage(ProposalFiles.Child(stagingRoot, action.RelativePath));
                break;
            case ProposalOperation.CreateModelDrawing:
                createdSha256[action.RelativePath] = DrawingWriter.CreateModel(action, stagingRoot, localData);
                break;
            case ProposalOperation.AddXref:
                var xref = (XrefData)action.Data;
                string target = plan.Configuration.Standards.Models.Single(m => m.Role == xref.Reference.ReferenceRole).Path;
                if (!createdSha256.TryGetValue(action.RelativePath, out string? hostHash) ||
                    !createdSha256.TryGetValue(target, out string? targetHash))
                    throw new InvalidDataException("Model overlay lacks writer-captured drawing digests.");
                createdSha256[action.RelativePath] = DrawingWriter.AddModelOverlay(
                    action, stagingRoot, hostHash, targetHash, localData);
                break;
            default:
                throw new NotSupportedException($"Native proposal operation is not implemented: {action.Operation}.");
        }
    }

    // Writer and verifier close their side databases within each call.
    public void CloseCreatedArtifacts() { }

    public VerificationReport Verify(ProposalPlan plan, string artifactRoot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!ReferenceEquals(inspectedPlan, plan) || appliedRoot is null ||
            !string.Equals(appliedRoot, ProposalFiles.Full(artifactRoot), StringComparison.OrdinalIgnoreCase))
            return new([], [new(ExecutionIssueCodes.VerificationFailed,
                "Native verification requires the inspected plan and its writer-captured staging root.")], [], []);
        return NativeProposalVerifier.VerifyModels(plan, artifactRoot, createdSha256);
    }

    internal static void ValidateModelAction(ProposalPlan plan, PlannedAction action, string stagingRoot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(action);
        if (!plan.Actions.Any(planned => ReferenceEquals(planned, action)))
            throw new InvalidDataException("Native action is not an element of the inspected plan.");
        if (!IsModelAction(action))
            throw new NotSupportedException($"Native proposal operation is not implemented: {action.Operation}.");
        if (action.RelativePath.Length > 0)
            _ = ProposalFiles.Child(stagingRoot, action.RelativePath);
        switch (action)
        {
            case { Operation: ProposalOperation.CreateFolder, Data: FolderData }:
                break;
            case { Operation: ProposalOperation.CreateModelDrawing, Data: ModelDrawingData model }:
                if (plan.Configuration.Standards.Models.Count(m => m.Role == model.Role && m.Path == action.RelativePath) != 1 ||
                    !string.Equals(model.Template, plan.Configuration.Standards.ModelTemplate, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Model action does not match the inspected standards.");
                break;
            case { Operation: ProposalOperation.AddXref, Data: XrefData xref }:
                var host = plan.Configuration.Standards.Models.SingleOrDefault(m => m.Role == xref.Reference.HostRole);
                var target = plan.Configuration.Standards.Models.SingleOrDefault(m => m.Role == xref.Reference.ReferenceRole);
                if (host is null || target is null || action.RelativePath != host.Path || xref.Reference.Mode != "Overlay")
                    throw new InvalidDataException("Model overlay action does not match the inspected plan.");
                string expectedTarget = ProposalFiles.Child(stagingRoot, target.Path);
                string resolved = ResolveOverlayTarget(stagingRoot, action.RelativePath, xref.RelativeReferencePath);
                if (!string.Equals(resolved, expectedTarget, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Model overlay target differs from its planned role.");
                break;
            default:
                throw new InvalidDataException("Native action operation and data do not match.");
        }
    }

    internal static string ResolveOverlayTarget(string stagingRoot, string hostRelativePath, string overlayRelativePath)
    {
        string host = ProposalFiles.Child(stagingRoot, hostRelativePath);
        if (string.IsNullOrWhiteSpace(overlayRelativePath) || Path.IsPathRooted(overlayRelativePath) ||
            overlayRelativePath.Contains(':'))
            throw new ProposalConditionException(ExecutionIssueCodes.UnsafeExecutionPath, "Overlay path must be relative.");
        string target = ProposalFiles.Full(Path.Combine(Path.GetDirectoryName(host)!,
            overlayRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!ProposalFiles.Within(target, stagingRoot))
            throw new ProposalConditionException(ExecutionIssueCodes.UnsafeExecutionPath, "Overlay target escapes staging.");
        ProposalFiles.RejectReparseAncestors(target);
        return target;
    }

    private static bool IsModelAction(PlannedAction action) => action switch
    {
        { Operation: ProposalOperation.CreateFolder, Data: FolderData } => true,
        { Operation: ProposalOperation.CreateModelDrawing, Data: ModelDrawingData } => true,
        { Operation: ProposalOperation.AddXref, Data: XrefData { Reference: { HostRole: "ProposedDesignModel", Mode: "Overlay" } } } => true,
        _ => false
    };
}
