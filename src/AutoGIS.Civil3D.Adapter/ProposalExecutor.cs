using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using AutoGIS.Civil3D.Proposal;

namespace AutoGIS.Civil3D.Adapter;

/// <summary>Filesystem lifecycle around the native host; the command does not invoke it until Task 5 is qualified.</summary>
public sealed class ProposalExecutor
{
    private readonly Action<FileStream>? beforeSuccessReceiptFlush;

    public ProposalExecutor() { }

    internal ProposalExecutor(Action<FileStream> beforeSuccessReceiptFlush) =>
        this.beforeSuccessReceiptFlush = beforeSuccessReceiptFlush;

    public RunReceipt Run(ProposalPlan plan, ProposalApproval? approval, IProposalHost host,
        Guid runId, DateTimeOffset startedAt, string failureReceiptDirectory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReceiptDirectory);
        string planJson = plan.ToJson();
        string planHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(planJson)));
        RunReceipt Blank(string outcome, IEnumerable<ProposalIssue>? checks = null, string? detail = null) =>
            new(runId, startedAt.ToUniversalTime(), planHash, outcome, plan.FinalRoot, null, [], [], null,
                checks?.ToImmutableArray() ?? [], detail, [], null, null);

        if (approval is null)
            return Blank("Cancelled", [new(ExecutionIssueCodes.ApprovalRequired, "Review the current preview and approve it before execution.")]);

        string stage;
        try
        {
            if (!string.Equals(planJson, approval.PlanJson, StringComparison.Ordinal))
                throw new InvalidDataException("The approved plan is no longer the plan selected for execution.");
            stage = ProposalFiles.ValidateAndStagePath(plan, runId, failureReceiptDirectory);
            ProposalFiles.RefuseExisting(plan.FinalRoot, stage);
            EnsureFingerprints(approval);
            ProposalFiles.ProbeWritable(plan.FinalRootComponents[0], runId);
            var native = host.Inspect(plan);
            if (!native.Issues.IsEmpty)
                return Blank("Refused", native.Issues);
            // Inspect can take time; recheck every approved input before the first artifact exists.
            EnsureFingerprints(approval);
            ProposalFiles.RefuseExisting(plan.FinalRoot, stage);
        }
        catch (Exception ex)
        {
            string code = SafeEntryExists(plan.FinalRoot)
                ? ExecutionIssueCodes.TargetExists : ExecutionIssueCodes.PreflightFailed;
            return Blank("Refused", [new(code, ex.Message)], ex.GetType().Name + ": " + ex.Message);
        }

        var ownedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ownedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool stageOwned = false;
        bool closeNeeded = false;
        string? failedActionId = "ReserveStaging";
        VerificationReport? verification = null;
        var cleanupFailures = ImmutableArray.CreateBuilder<string>();
        var completed = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            ProposalFiles.ReserveStage(stage);
            stageOwned = true;
            foreach (var action in plan.Actions)
            {
                if (action.Data is SupportRecordData { Content: SupportContent.ReceiptAfterVerification }) continue;
                failedActionId = action.Id;
                if (!action.Dependencies.All(completed.Contains))
                    throw new InvalidDataException("Planned action dependencies are out of order.");
                ProposalFiles.RejectReparseAncestors(stage);
                if (ProposalFiles.EntryExists(plan.FinalRoot)) throw new IOException("The final target appeared during execution.");
                ProposalFiles.RequireFreshActionTarget(stage, action);
                using FileStream? templateGuard = OpenVerifiedTemplateAtUse(action, approval);
                closeNeeded = true;
                host.Apply(action, stage);
                ProposalFiles.TrackAction(stage, action, ownedFiles, ownedDirectories);
                completed.Add(action.Id);
            }

            failedActionId = "CloseCreatedArtifacts";
            host.CloseCreatedArtifacts();
            closeNeeded = false;
            failedActionId = "Verify";
            closeNeeded = true;
            verification = host.Verify(plan, stage);
            host.CloseCreatedArtifacts();
            closeNeeded = false;
            if (!verification.FailedChecks.IsEmpty)
                throw new InvalidDataException("Independent artifact verification failed.");
            ProposalFiles.ValidateObservedArtifacts(plan, verification, stage);

            failedActionId = "PrepareReceipt";
            string receiptRelative = ProposalFiles.ReceiptRelativePath(plan);
            string stagedReceipt = ProposalFiles.Child(stage, receiptRelative);
            string publishedReceipt = ProposalFiles.Child(plan.FinalRoot, receiptRelative);
            var candidate = new RunReceipt(runId, startedAt.ToUniversalTime(), planHash, "Succeeded", plan.FinalRoot,
                publishedReceipt, verification.VerifiedArtifacts, verification.ManualSteps, null, [], null, [], null, null);
            ProposalFiles.WriteReceipt(stagedReceipt, candidate, () => ownedFiles.Add(stagedReceipt), beforeSuccessReceiptFlush);
            ProposalFiles.ValidateOwnedTree(stage, ownedFiles, ownedDirectories);

            failedActionId = "Promote";
            ProposalFiles.Publish(stage, plan.FinalRoot);
            return candidate;
        }
        catch (Exception ex)
        {
            if (closeNeeded)
            {
                try { host.CloseCreatedArtifacts(); }
                catch (Exception closeEx) { cleanupFailures.Add("CloseCreatedArtifacts: " + closeEx.GetType().Name + ": " + closeEx.Message); }
            }
            if (stageOwned)
            {
                string? cleanup = ProposalFiles.Cleanup(stage);
                if (cleanup is not null) cleanupFailures.Add(cleanup);
            }
            var checks = verification?.FailedChecks ?? [];
            if (checks.IsEmpty) checks = [new(ExecutionIssueCodes.ExecutionFailed, ex.Message)];
            var failure = Blank("Failed", checks, ex.GetType().Name + ": " + ex.Message) with
            {
                FailedActionId = failedActionId,
                CleanupFailures = cleanupFailures.ToImmutable(),
                RetainedStagingRoot = stageOwned && SafeEntryExists(stage) ? stage : null
            };
            string failurePath = Path.Combine(failureReceiptDirectory, $"NewProposal-{runId:N}.failed.json");
            try
            {
                ProposalFiles.WriteReceipt(failurePath, failure with { ReceiptPath = failurePath }, () => { });
                return failure with { ReceiptPath = failurePath };
            }
            catch (Exception reportingEx)
            {
                return failure with { ReportingFailure = reportingEx.GetType().Name + ": " + reportingEx.Message };
            }
        }
    }

    private static void EnsureFingerprints(ProposalApproval approval)
    {
        if (approval.DependencyFingerprints.Count == 0)
            throw new InvalidDataException("Approval contains no source fingerprints.");
        foreach (var expected in approval.DependencyFingerprints)
            using (OpenVerifiedDependency(expected.Key, expected.Value)) { }
    }

    private static FileStream? OpenVerifiedTemplateAtUse(PlannedAction action, ProposalApproval approval)
    {
        string? path = action.Data switch
        {
            ModelDrawingData model => model.Template,
            SheetDrawingData sheet => sheet.Profile.Template,
            _ => null
        };
        if (path is null) return null;
        path = Path.GetFullPath(path);
        if (!approval.DependencyFingerprints.TryGetValue(path, out string? expected))
            throw new InvalidDataException("The selected template was not captured by approval.");
        return OpenVerifiedDependency(path, expected);
    }

    private static FileStream OpenVerifiedDependency(string path, string expected)
    {
        ProposalFiles.RejectReparseAncestors(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            string actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new InvalidDataException("An approved manifest or template changed; create a new preview and approve it.");
            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static bool SafeEntryExists(string path)
    {
        try { return ProposalFiles.EntryExists(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
