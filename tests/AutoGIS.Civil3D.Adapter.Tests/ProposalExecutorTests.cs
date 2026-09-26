using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoGIS.Civil3D.Proposal;
using Xunit;
using Xunit.Abstractions;

namespace AutoGIS.Civil3D.Adapter.Tests;

public sealed class ProposalExecutorTests
{
    private readonly ITestOutputHelper output;

    public ProposalExecutorTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void MissingApprovalCancelsWithoutHostCallsOrArtifacts()
    {
        using var harness = new Harness();
        var host = new RecordingProposalHost();
        var result = new ProposalExecutor().Run(harness.Plan, null, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Cancelled", result.Outcome);
        Assert.Empty(host.Calls);
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        Assert.Empty(Directory.GetFileSystemEntries(harness.BaseRoot));
    }

    [Fact]
    public void ExistingTargetIsPreservedByteForByte()
    {
        using var harness = new Harness();
        Directory.CreateDirectory(harness.Plan.FinalRoot);
        string foreign = Path.Combine(harness.Plan.FinalRoot, "foreign.bin");
        byte[] bytes = [0, 1, 2, 255];
        File.WriteAllBytes(foreign, bytes);
        var host = new RecordingProposalHost();
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Refused", result.Outcome);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.TargetExists);
        Assert.Equal(bytes, File.ReadAllBytes(foreign));
        Assert.Empty(host.Calls);
        Assert.Single(Directory.GetFileSystemEntries(harness.BaseRoot));
    }

    [Fact]
    public void SuccessPublishesOnlyVerifiedArtifactsAndCompleteReceipt()
    {
        using var harness = new Harness();
        var host = new RecordingProposalHost();
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Succeeded", result.Outcome);
        Assert.Equal(2, host.CloseCount);
        Assert.True(Directory.Exists(harness.Plan.FinalRoot));
        Assert.NotNull(result.ReceiptPath);
        Assert.True(File.Exists(result.ReceiptPath));
        Assert.Null(result.RetainedStagingRoot);
        Assert.False(Directory.Exists(harness.StageRoot));
        Assert.Equal(ProposalFiles.ExpectedArtifacts(harness.Plan).Count, result.VerifiedArtifacts.Length);
        var published = JsonSerializer.Deserialize<RunReceipt>(File.ReadAllText(result.ReceiptPath));
        Assert.NotNull(published);
        Assert.Equal("Succeeded", published.Outcome);
        Assert.Equal(result.PlanSha256, published.PlanSha256);
        Assert.Equal(result.ReceiptPath, published.ReceiptPath);
    }

    [Fact]
    public void MissingPlannedEmptyFolderPreventsPublication()
    {
        using var harness = new Harness();
        string shortcutId = harness.Plan.Actions.Single(action => action.Operation == ProposalOperation.CreateFolder &&
            action.RelativePath == "Shortcuts").Id;
        var host = new RecordingProposalHost { SkipActionId = shortcutId };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("Verify", result.FailedActionId);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.VerificationFailed);
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.False(Directory.Exists(ProposalFiles.Child(harness.StageRoot, "Shortcuts")));
        Assert.True(File.Exists(result.ReceiptPath));
    }

    [Fact]
    public void FileRemovedAfterVerificationPreventsPublication()
    {
        using var harness = new Harness();
        string model = harness.Plan.Actions.Single(action => action.Id == "20:model/BaseModel").RelativePath;
        string stagedModel = ProposalFiles.Child(harness.StageRoot, model);
        var executor = new ProposalExecutor(_ => File.Delete(stagedModel));
        var result = executor.Run(harness.Plan, harness.Approval, new RecordingProposalHost(),
            harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("Verify", result.FailedActionId);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.VerificationFailed);
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.False(File.Exists(stagedModel));
        Assert.True(File.Exists(result.ReceiptPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArtifactChangedAfterReadbackPreventsPublication(bool replaceFile)
    {
        using var harness = new Harness();
        string model = harness.Plan.Actions.Single(action => action.Id == "20:model/BaseModel").RelativePath;
        string stagedModel = ProposalFiles.Child(harness.StageRoot, model);
        byte[] tampered = [99, 98, 97];
        var executor = new ProposalExecutor(_ =>
        {
            if (replaceFile) File.Delete(stagedModel);
            using var stream = new FileStream(stagedModel, replaceFile ? FileMode.CreateNew : FileMode.Open,
                FileAccess.Write, FileShare.None);
            stream.SetLength(0);
            stream.Write(tampered);
        });

        var result = executor.Run(harness.Plan, harness.Approval, new RecordingProposalHost(),
            harness.RunId, Harness.FixedTime, harness.FailureDirectory);

        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("Verify", result.FailedActionId);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.VerificationFailed);
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        Assert.Equal(tampered, File.ReadAllBytes(stagedModel));
        Assert.True(File.Exists(result.ReceiptPath));
        Assert.Equal("Failed", JsonSerializer.Deserialize<RunReceipt>(File.ReadAllText(result.ReceiptPath!))!.Outcome);
    }

    [Fact]
    public void MissingReadbackDigestPreventsPublication()
    {
        using var harness = new Harness();
        string model = harness.Plan.Actions.Single(action => action.Id == "20:model/BaseModel").RelativePath;
        var host = new RecordingProposalHost { OmitHashRelativePath = model };

        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host,
            harness.RunId, Harness.FixedTime, harness.FailureDirectory);

        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("Verify", result.FailedActionId);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.VerificationFailed);
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        Assert.True(File.Exists(result.ReceiptPath));
    }

    [Fact]
    public void ArtifactUnreadableAfterReadbackPreventsPublication()
    {
        using var harness = new Harness();
        string model = harness.Plan.Actions.Single(action => action.Id == "20:model/BaseModel").RelativePath;
        string stagedModel = ProposalFiles.Child(harness.StageRoot, model);
        FileStream? locked = null;
        try
        {
            var executor = new ProposalExecutor(_ => locked = new FileStream(stagedModel,
                FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            var result = executor.Run(harness.Plan, harness.Approval, new RecordingProposalHost(),
                harness.RunId, Harness.FixedTime, harness.FailureDirectory);

            Assert.Equal("Failed", result.Outcome);
            Assert.Equal("Verify", result.FailedActionId);
            Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.VerificationFailed);
            Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
            Assert.False(Directory.Exists(harness.Plan.FinalRoot));
            Assert.True(File.Exists(result.ReceiptPath));
        }
        finally { locked?.Dispose(); }
    }

    [Fact]
    public void MissingArtifactDuringHostReadbackIsVerificationFailure()
    {
        using var harness = new Harness();
        string model = harness.Plan.Actions.Single(a => a.Id == "20:model/BaseModel").RelativePath;
        var host = new RecordingProposalHost
        {
            OnVerify = stage => File.Delete(ProposalFiles.Child(stage, model))
        };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host,
            harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("Verify", result.FailedActionId);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.VerificationFailed);
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
    }

    [Fact]
    public void HostReadbackExceptionIsVerificationFailure()
    {
        using var harness = new Harness();
        var host = new RecordingProposalHost
        {
            OnVerify = _ => throw new IOException("Injected readback failure.")
        };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host,
            harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("Verify", result.FailedActionId);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.VerificationFailed);
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
    }

    [Fact]
    public void EveryActionFailureRetainsEvidenceWithoutPublishing()
    {
        using var identity = new Harness();
        string[] ids = identity.Plan.Actions.Where(a => a.Data is not SupportRecordData { Content: SupportContent.ReceiptAfterVerification })
            .Select(a => a.Id).ToArray();
        foreach (string id in ids)
        {
            using var harness = new Harness();
            var host = new RecordingProposalHost { FailActionId = id };
            var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
            Assert.Equal("Failed", result.Outcome);
            Assert.Equal(id, result.FailedActionId);
            Assert.False(Directory.Exists(harness.Plan.FinalRoot));
            if (result.RetainedStagingRoot is null)
            {
                Assert.False(Directory.Exists(harness.StageRoot));
                Assert.Empty(result.CleanupFailures);
            }
            else
            {
                Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
                Assert.NotEmpty(Directory.GetFileSystemEntries(harness.StageRoot));
                Assert.NotEmpty(result.CleanupFailures);
            }
            Assert.True(File.Exists(result.ReceiptPath));
            Assert.Equal(id, JsonSerializer.Deserialize<RunReceipt>(File.ReadAllText(result.ReceiptPath!))!.FailedActionId);
            Assert.True(host.CloseCount >= 1);
        }
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("model")]
    [InlineData("sheet")]
    public void ChangedDependencyAfterApprovalRefusesBeforeStaging(string dependency)
    {
        using var harness = new Harness();
        string path = dependency switch
        {
            "manifest" => harness.ManifestPath,
            "model" => harness.ModelTemplate,
            _ => harness.SheetTemplate
        };
        File.WriteAllText(path, "different after approval");
        var host = new RecordingProposalHost();
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Refused", result.Outcome);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.StaleApproval);
        Assert.Empty(host.Calls);
        Assert.False(Directory.Exists(harness.StageRoot));
    }

    [Fact]
    public void DifferentApprovedPlanIsReportedAsStale()
    {
        using var harness = new Harness();
        var stale = new ProposalApproval(harness.Approval.PlanJson + " ", harness.Approval.DependencyFingerprints.ToDictionary());
        var host = new RecordingProposalHost();
        var result = new ProposalExecutor().Run(harness.Plan, stale, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Refused", result.Outcome);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.StaleApproval);
        Assert.Empty(host.Calls);
        Assert.False(Directory.Exists(harness.StageRoot));
    }

    [Fact]
    public void ChangedTemplateDuringInspectRefusesBeforeStaging()
    {
        using var harness = new Harness();
        var host = new RecordingProposalHost { OnInspect = () => File.WriteAllText(harness.ModelTemplate, "different during inspect") };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Refused", result.Outcome);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.StaleApproval);
        Assert.Equal(["Inspect"], host.Calls);
        Assert.False(Directory.Exists(harness.StageRoot));
    }

    [Fact]
    public void MissingApprovedTemplateIsStaleBeforeStaging()
    {
        using var harness = new Harness();
        File.Delete(harness.ModelTemplate);
        var host = new RecordingProposalHost();
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host,
            harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Refused", result.Outcome);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.StaleApproval);
        Assert.Empty(host.Calls);
        Assert.False(Directory.Exists(harness.StageRoot));
    }

    [Fact]
    public void LockedUnchangedTemplateIsPreflightFailure()
    {
        using var harness = new Harness();
        using var locked = new FileStream(harness.ModelTemplate, FileMode.Open, FileAccess.Read, FileShare.None);
        var host = new RecordingProposalHost();
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Refused", result.Outcome);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.PreflightFailed);
        Assert.Empty(host.Calls);
        Assert.False(Directory.Exists(harness.StageRoot));
    }

    [Fact]
    public void EmptyRunIdIsPreflightFailure()
    {
        using var harness = new Harness();
        var host = new RecordingProposalHost();
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, Guid.Empty, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Refused", result.Outcome);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.PreflightFailed);
        Assert.Empty(host.Calls);
        Assert.Empty(Directory.GetFileSystemEntries(harness.BaseRoot));
    }

    [Fact]
    public void ChangedTemplateBeforeNativeUseFailsAndRetainsStage()
    {
        using var harness = new Harness();
        var host = new RecordingProposalHost();
        host.OnApply = action =>
        {
            if (action.Id == "00:root") File.WriteAllText(harness.ModelTemplate, "different before native use");
        };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.StaleApproval);
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.NotEmpty(result.CleanupFailures);
        Assert.True(File.Exists(result.ReceiptPath));
    }

    [Fact]
    public void MissingApprovedTemplateDuringExecutionIsStale()
    {
        using var harness = new Harness();
        var folder = harness.Plan.Actions.First(a => a.Operation == ProposalOperation.CreateFolder && a.RelativePath.Length > 0);
        var host = new RecordingProposalHost
        {
            OnApply = action =>
            {
                if (action.Id == folder.Id) File.Delete(harness.ModelTemplate);
            }
        };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host,
            harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("20:model/BaseModel", result.FailedActionId);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.StaleApproval);
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
    }

    [Fact]
    public void FailedCloseAndVerificationNeverPublish()
    {
        using var closeHarness = new Harness();
        var closeHost = new RecordingProposalHost { FailClose = true };
        var close = new ProposalExecutor().Run(closeHarness.Plan, closeHarness.Approval, closeHost, closeHarness.RunId, Harness.FixedTime, closeHarness.FailureDirectory);
        Assert.Equal("Failed", close.Outcome);
        Assert.Equal("CloseCreatedArtifacts", close.FailedActionId);
        Assert.NotEmpty(close.CleanupFailures);
        Assert.False(Directory.Exists(closeHarness.Plan.FinalRoot));

        using var verifyHarness = new Harness();
        var verifyHost = new RecordingProposalHost { FailVerify = true };
        var verify = new ProposalExecutor().Run(verifyHarness.Plan, verifyHarness.Approval, verifyHost, verifyHarness.RunId, Harness.FixedTime, verifyHarness.FailureDirectory);
        Assert.Equal("Failed", verify.Outcome);
        Assert.Equal("Verify", verify.FailedActionId);
        Assert.Contains(verify.FailedChecks, c => c.Code == ExecutionIssueCodes.VerificationFailed);
        Assert.False(Directory.Exists(verifyHarness.Plan.FinalRoot));
    }

    [Fact]
    public void CloseAfterVerificationReportsTheFailedCloseOperation()
    {
        using var harness = new Harness();
        var host = new RecordingProposalHost { FailCloseOnCount = 2 };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("CloseCreatedArtifacts", result.FailedActionId);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.ExecutionFailed);
        Assert.Contains("Verify", host.Calls);
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.True(File.Exists(result.ReceiptPath));
    }

    [Fact]
    public void ReceiptPreparationFailureRetainsForeignStagingEntryAndFailureReceipt()
    {
        using var harness = new Harness();
        var host = new RecordingProposalHost
        {
            OnVerify = stage => Directory.CreateDirectory(Path.Combine(stage, ProposalFiles.ReceiptRelativePath(harness.Plan)))
        };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("PrepareReceipt", result.FailedActionId);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.ExecutionFailed);
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.NotEmpty(result.CleanupFailures);
        Assert.True(File.Exists(result.ReceiptPath));
    }

    [Fact]
    public void PartialSuccessReceiptWriteFailureNeverPublishes()
    {
        using var harness = new Harness();
        var host = new RecordingProposalHost();
        var executor = new ProposalExecutor(stream =>
        {
            stream.WriteByte(255);
            throw new IOException("Injected failure after serializing success receipt, before flush.");
        });
        var result = executor.Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("PrepareReceipt", result.FailedActionId);
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.NotEmpty(result.CleanupFailures);
        Assert.True(File.Exists(ProposalFiles.Child(harness.StageRoot, ProposalFiles.ReceiptRelativePath(harness.Plan))));
        Assert.True(File.Exists(result.ReceiptPath));
        Assert.Equal("Failed", JsonSerializer.Deserialize<RunReceipt>(File.ReadAllText(result.ReceiptPath!))!.Outcome);
    }

    [Fact]
    public void RealAclDenialRefusesBeforeAnyHostOrStagingWork()
    {
        using var harness = new Harness();
        var directory = new DirectoryInfo(harness.BaseRoot);
        var original = directory.GetAccessControl(AccessControlSections.Access);
        var denied = directory.GetAccessControl(AccessControlSections.Access);
        var user = WindowsIdentity.GetCurrent().User!;
        denied.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.CreateFiles, AccessControlType.Deny));
        directory.SetAccessControl(denied);
        try
        {
            var host = new RecordingProposalHost();
            var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
            Assert.Equal("Refused", result.Outcome);
            Assert.Empty(host.Calls);
            Assert.False(Directory.Exists(harness.StageRoot));
            Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        }
        finally { directory.SetAccessControl(original); }
    }

    [Fact]
    public void DirectoryCreationAclDenialRefusesBeforeNativeInspect()
    {
        using var harness = new Harness();
        var directory = new DirectoryInfo(harness.BaseRoot);
        var original = directory.GetAccessControl(AccessControlSections.Access);
        var denied = directory.GetAccessControl(AccessControlSections.Access);
        denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateDirectories, AccessControlType.Deny));
        directory.SetAccessControl(denied);
        try
        {
            string file = Path.Combine(harness.BaseRoot, "file-creation-still-allowed");
            File.WriteAllText(file, "allowed");
            File.Delete(file);
            var host = new RecordingProposalHost();
            var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
            Assert.Equal("Refused", result.Outcome);
            Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.PreflightFailed);
            Assert.Empty(host.Calls);
            Assert.False(Directory.Exists(harness.StageRoot));
            Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        }
        finally { directory.SetAccessControl(original); }
    }

    [Fact]
    public void ParentEnumerationAclDenialIsPreflightFailure()
    {
        using var harness = new Harness();
        var directory = new DirectoryInfo(harness.BaseRoot);
        var denied = directory.GetAccessControl(AccessControlSections.Access);
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory, AccessControlType.Deny);
        denied.AddAccessRule(rule);
        directory.SetAccessControl(denied);
        try
        {
            var host = new RecordingProposalHost();
            var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
            Assert.Equal("Refused", result.Outcome);
            Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.PreflightFailed);
            Assert.Empty(host.Calls);
            Assert.False(Directory.Exists(harness.StageRoot));
            Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        }
        finally
        {
            var restored = directory.GetAccessControl(AccessControlSections.Access);
            restored.RemoveAccessRuleSpecific(rule);
            directory.SetAccessControl(restored);
        }
    }

    [Fact]
    public void MissingConfiguredBaseRootReportsPreflightFailure()
    {
        using var harness = new Harness();
        Directory.Delete(harness.BaseRoot);
        var host = new RecordingProposalHost();
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Refused", result.Outcome);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.PreflightFailed);
        Assert.Empty(host.Calls);
        Assert.False(Directory.Exists(harness.StageRoot));
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
    }

    [Fact]
    public void BaseRootRemovedDuringInspectIsPreflightFailure()
    {
        using var harness = new Harness();
        var host = new RecordingProposalHost { OnInspect = () => Directory.Delete(harness.BaseRoot) };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Refused", result.Outcome);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.PreflightFailed);
        Assert.Equal(["Inspect"], host.Calls);
        Assert.False(Directory.Exists(harness.StageRoot));
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
    }

    [Fact]
    public void ReparsePointIntroducedDuringInspectIsReportedAsUnsafe()
    {
        using var harness = new Harness();
        string parent = Path.GetDirectoryName(harness.BaseRoot)!;
        string physical = Path.Combine(parent, "physical-proposals");
        string probe = Path.Combine(parent, "symlink-probe");
        Directory.CreateDirectory(physical);
        try
        {
            Directory.CreateSymbolicLink(probe, physical);
            Directory.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            output.WriteLine("Reparse race test not exercised: local symlink creation is unavailable: " + ex.GetType().Name);
            return;
        }
        bool linked = false;
        var host = new RecordingProposalHost
        {
            OnInspect = () =>
            {
                Directory.Delete(harness.BaseRoot);
                Directory.CreateSymbolicLink(harness.BaseRoot, physical);
                linked = true;
            }
        };
        try
        {
            var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
            Assert.Equal("Refused", result.Outcome);
            Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.UnsafeExecutionPath);
            Assert.Equal(["Inspect"], host.Calls);
            Assert.Empty(Directory.GetFileSystemEntries(physical));
            Assert.False(Directory.Exists(harness.StageRoot));
        }
        finally
        {
            if (linked) Directory.Delete(harness.BaseRoot);
            Directory.CreateDirectory(harness.BaseRoot);
        }
    }

    [Fact]
    public void ReparsePointBaseRootIsRejectedWithoutTouchingItsTarget()
    {
        using var harness = new Harness();
        string physical = Path.Combine(Path.GetDirectoryName(harness.BaseRoot)!, "physical-proposals");
        Directory.CreateDirectory(physical);
        Directory.Delete(harness.BaseRoot);
        bool linked = false;
        try
        {
            try
            {
                Directory.CreateSymbolicLink(harness.BaseRoot, physical);
                linked = true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                output.WriteLine("Reparse test not exercised: local symlink creation is unavailable: " + ex.GetType().Name);
                return;
            }
            var host = new RecordingProposalHost();
            var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
            Assert.Equal("Refused", result.Outcome);
            Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.UnsafeExecutionPath);
            Assert.Empty(host.Calls);
            Assert.Empty(Directory.GetFileSystemEntries(physical));
        }
        finally
        {
            if (linked) Directory.Delete(harness.BaseRoot);
            Directory.CreateDirectory(harness.BaseRoot);
        }
    }

    [Fact]
    public void ReparsePointCreatedInStagingReportsUnsafeFailure()
    {
        using var harness = new Harness();
        var folder = harness.Plan.Actions.First(a => a.Operation == ProposalOperation.CreateFolder && a.RelativePath.Length > 0);
        string physical = Path.Combine(Path.GetDirectoryName(harness.BaseRoot)!, "physical-folder");
        string probe = Path.Combine(Path.GetDirectoryName(harness.BaseRoot)!, "symlink-probe");
        Directory.CreateDirectory(physical);
        File.WriteAllText(Path.Combine(physical, "marker.txt"), "untouched");
        try
        {
            Directory.CreateSymbolicLink(probe, physical);
            Directory.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            output.WriteLine("Staging reparse test not exercised: local symlink creation is unavailable: " + ex.GetType().Name);
            return;
        }

        var host = new RecordingProposalHost
        {
            OnApply = action =>
            {
                if (action.Id == "00:root")
                    Directory.CreateSymbolicLink(ProposalFiles.Child(harness.StageRoot, folder.RelativePath), physical);
            }
        };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host,
            harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal(folder.Id, result.FailedActionId);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.UnsafeExecutionPath);
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(physical, "marker.txt")));
    }

    [Fact]
    public void FinalRootReparseCreatedDuringApplyReportsUnsafeFailure()
    {
        using var harness = new Harness();
        var folder = harness.Plan.Actions.First(a => a.Operation == ProposalOperation.CreateFolder && a.RelativePath.Length > 0);
        string physical = Path.Combine(Path.GetDirectoryName(harness.BaseRoot)!, "physical-proposal");
        string probe = Path.Combine(Path.GetDirectoryName(harness.BaseRoot)!, "symlink-probe");
        Directory.CreateDirectory(physical);
        File.WriteAllText(Path.Combine(physical, "marker.txt"), "untouched");
        try
        {
            Directory.CreateSymbolicLink(probe, physical);
            Directory.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            output.WriteLine("Final-root reparse test not exercised: local symlink creation is unavailable: " + ex.GetType().Name);
            return;
        }

        bool linked = false;
        var host = new RecordingProposalHost
        {
            OnApply = action =>
            {
                if (action.Id != folder.Id) return;
                Directory.CreateSymbolicLink(harness.Plan.FinalRoot, physical);
                linked = true;
            }
        };
        try
        {
            var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host,
                harness.RunId, Harness.FixedTime, harness.FailureDirectory);
            Assert.Equal("Failed", result.Outcome);
            Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.UnsafeExecutionPath);
            Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
            Assert.True(Directory.Exists(ProposalFiles.Child(harness.StageRoot, folder.RelativePath)));
            Assert.True((File.GetAttributes(harness.Plan.FinalRoot) & FileAttributes.ReparsePoint) != 0);
            Assert.Equal("untouched", File.ReadAllText(Path.Combine(physical, "marker.txt")));
            Assert.True(File.Exists(result.ReceiptPath));
        }
        finally
        {
            if (linked) Directory.Delete(harness.Plan.FinalRoot);
        }
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
    }

    [Fact]
    public void PromotionRacePreservesForeignTargetAndReportsFailure()
    {
        using var harness = new Harness();
        byte[] bytes = [8, 9, 10];
        string foreign = Path.Combine(harness.Plan.FinalRoot, "foreign.bin");
        var host = new RecordingProposalHost
        {
            OnVerify = _ =>
            {
                Directory.CreateDirectory(harness.Plan.FinalRoot);
                File.WriteAllBytes(foreign, bytes);
            }
        };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("Promote", result.FailedActionId);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.TargetExists);
        Assert.Equal(bytes, File.ReadAllBytes(foreign));
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.NotEmpty(result.CleanupFailures);
        Assert.True(File.Exists(result.ReceiptPath));
    }

    [Fact]
    public void ForeignStagingEntryAndFailureReceiptStorageFailureStayVisible()
    {
        using var harness = new Harness();
        string obstruction = Path.Combine(harness.BaseRoot, "obstruction");
        File.WriteAllText(obstruction, "not a directory");
        var host = new RecordingProposalHost
        {
            FailActionId = "00:root",
            OnFailure = stage => File.WriteAllText(Path.Combine(stage, "foreign.txt"), "retain me")
        };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, obstruction);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.NotEmpty(result.CleanupFailures);
        Assert.NotNull(result.ReportingFailure);
        Assert.Null(result.ReceiptPath);
        Assert.Equal("retain me", File.ReadAllText(Path.Combine(harness.StageRoot, "foreign.txt")));
    }

    [Fact]
    public void InterruptedStageIsNeverAdopted()
    {
        using var harness = new Harness();
        Directory.CreateDirectory(harness.StageRoot);
        File.WriteAllText(Path.Combine(harness.StageRoot, "foreign.txt"), "retain me");
        var host = new RecordingProposalHost();
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Refused", result.Outcome);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.TargetExists);
        Assert.Empty(host.Calls);
        Assert.Equal("retain me", File.ReadAllText(Path.Combine(harness.StageRoot, "foreign.txt")));
    }

    [Fact]
    public void EarlierInterruptedRunBlocksNewRunIdForSameProposal()
    {
        using var harness = new Harness();
        string oldStage = ProposalFiles.StagePath(harness.Plan.FinalRoot, Guid.NewGuid());
        Directory.CreateDirectory(oldStage);
        File.WriteAllText(Path.Combine(oldStage, "interrupted.txt"), "evidence");
        var host = new RecordingProposalHost();
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Refused", result.Outcome);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.TargetExists);
        Assert.Empty(host.Calls);
        Assert.False(Directory.Exists(harness.StageRoot));
        Assert.Equal("evidence", File.ReadAllText(Path.Combine(oldStage, "interrupted.txt")));
    }

    [Fact]
    public void LockedGeneratedFileIsRetainedAndCleanupFailureIsReported()
    {
        using var harness = new Harness();
        FileStream? locked = null;
        var host = new RecordingProposalHost
        {
            FailActionId = "20:model/BaseModel",
            OnFailure = stage => locked = new FileStream(
                ProposalFiles.Child(stage, harness.Plan.Actions.Single(a => a.Id == "20:model/BaseModel").RelativePath),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)
        };
        try
        {
            var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
            Assert.Equal("Failed", result.Outcome);
            Assert.Equal("20:model/BaseModel", result.FailedActionId);
            Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
            Assert.NotEmpty(result.CleanupFailures);
            Assert.False(Directory.Exists(harness.Plan.FinalRoot));
            Assert.True(File.Exists(result.ReceiptPath));
        }
        finally { locked?.Dispose(); }
    }

    [Fact]
    public void ForeignPlannedLeafIsNotAdoptedOrDeleted()
    {
        using var harness = new Harness();
        var folder = harness.Plan.Actions.First(a => a.Operation == ProposalOperation.CreateFolder && a.RelativePath.Length > 0);
        var host = new RecordingProposalHost
        {
            OnApply = action =>
            {
                if (action.Id == "00:root")
                    Directory.CreateDirectory(ProposalFiles.Child(harness.StageRoot, folder.RelativePath));
            }
        };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal(folder.Id, result.FailedActionId);
        Assert.Contains(result.FailedChecks, issue => issue.Code == ExecutionIssueCodes.TargetExists);
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.True(Directory.Exists(ProposalFiles.Child(harness.StageRoot, folder.RelativePath)));
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
    }

    [Fact]
    public void ForeignFileRacingBeforeExclusiveCreateIsRetainedByteForByte()
    {
        using var harness = new Harness();
        byte[] foreignBytes = [21, 22, 23];
        string model = harness.Plan.Actions.Single(a => a.Id == "20:model/BaseModel").RelativePath;
        var host = new RecordingProposalHost
        {
            OnApply = action =>
            {
                if (action.Id == "20:model/BaseModel")
                    File.WriteAllBytes(ProposalFiles.Child(harness.StageRoot, model), foreignBytes);
            }
        };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("20:model/BaseModel", result.FailedActionId);
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.NotEmpty(result.CleanupFailures);
        Assert.Equal(foreignBytes, File.ReadAllBytes(ProposalFiles.Child(harness.StageRoot, model)));
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
    }

    [Fact]
    public void ReplacedEarlierArtifactIsRetainedWhenLaterActionFails()
    {
        using var harness = new Harness();
        byte[] foreignBytes = [31, 32, 33];
        string model = harness.Plan.Actions.Single(a => a.Id == "20:model/BaseModel").RelativePath;
        string stagedModel = ProposalFiles.Child(harness.StageRoot, model);
        var host = new RecordingProposalHost
        {
            FailActionId = "20:model/ExistingConditionsModel",
            OnApply = action =>
            {
                if (action.Id == "20:model/ExistingConditionsModel")
                {
                    File.Delete(stagedModel);
                    File.WriteAllBytes(stagedModel, foreignBytes);
                }
            }
        };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("20:model/ExistingConditionsModel", result.FailedActionId);
        Assert.Equal(harness.StageRoot, result.RetainedStagingRoot);
        Assert.NotEmpty(result.CleanupFailures);
        Assert.Equal(foreignBytes, File.ReadAllBytes(stagedModel));
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
        Assert.True(File.Exists(result.ReceiptPath));
    }

    [Fact]
    public void TemplateCannotBeSwappedInsideNativeApply()
    {
        using var harness = new Harness();
        byte[] approved = File.ReadAllBytes(harness.ModelTemplate);
        var host = new RecordingProposalHost
        {
            OnApply = action =>
            {
                if (action.Id == "20:model/BaseModel")
                    File.WriteAllText(harness.ModelTemplate, "swapped after fingerprint before native open");
            }
        };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Equal("20:model/BaseModel", result.FailedActionId);
        Assert.Equal(approved, File.ReadAllBytes(harness.ModelTemplate));
        Assert.False(Directory.Exists(harness.Plan.FinalRoot));
    }

    [Fact]
    public void EarlierFailureReceiptIsNeverOverwritten()
    {
        using var harness = new Harness();
        string earlier = Path.Combine(harness.FailureDirectory, $"NewProposal-{harness.RunId:N}.failed.json");
        byte[] evidence = [11, 12, 13];
        File.WriteAllBytes(earlier, evidence);
        var host = new RecordingProposalHost { FailActionId = "00:root" };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Failed", result.Outcome);
        Assert.Null(result.ReceiptPath);
        Assert.NotNull(result.ReportingFailure);
        Assert.Equal(evidence, File.ReadAllBytes(earlier));
    }

    [Fact]
    public void NativePreflightIssueRefusesBeforeStaging()
    {
        using var harness = new Harness();
        var host = new RecordingProposalHost { InspectIssues = [new(ExecutionIssueCodes.PreflightFailed, "Synthetic host is unavailable.")] };
        var result = new ProposalExecutor().Run(harness.Plan, harness.Approval, host, harness.RunId, Harness.FixedTime, harness.FailureDirectory);
        Assert.Equal("Refused", result.Outcome);
        Assert.Equal(["Inspect"], host.Calls);
        Assert.False(Directory.Exists(harness.StageRoot));
        Assert.Contains(result.FailedChecks, issue => issue.Message == "Synthetic host is unavailable.");
    }

    private sealed class Harness : IDisposable
    {
        public static readonly DateTimeOffset FixedTime = DateTimeOffset.Parse("2026-09-04T00:00:00Z");
        private readonly string root = Path.Combine(Path.GetTempPath(), "AutoGIS-executor-tests", Guid.NewGuid().ToString("N"));
        public Guid RunId { get; } = Guid.NewGuid();
        public string BaseRoot { get; }
        public string FailureDirectory { get; }
        public string ModelTemplate { get; }
        public string SheetTemplate { get; }
        public string ManifestPath { get; }
        public string StageRoot => ProposalFiles.StagePath(Plan.FinalRoot, RunId);
        public ProposalPlan Plan { get; }
        public ProposalApproval Approval { get; }

        public Harness()
        {
            Directory.CreateDirectory(root);
            BaseRoot = Path.Combine(root, "Proposals");
            FailureDirectory = Path.Combine(root, "Failures");
            Directory.CreateDirectory(BaseRoot);
            Directory.CreateDirectory(FailureDirectory);
            string model = Path.Combine(root, "Model.dwt");
            ModelTemplate = model;
            string sheet = Path.Combine(root, "Sheet.dwt");
            SheetTemplate = sheet;
            string manifest = Path.Combine(root, "standards.json");
            ManifestPath = manifest;
            File.WriteAllText(model, "synthetic model bytes");
            File.WriteAllText(sheet, "synthetic sheet bytes");
            var json = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-standards.json")))!;
            json["baseRoots"]![0]!["path"] = BaseRoot.Replace('\\', '/');
            json["modelTemplate"] = model.Replace('\\', '/');
            json["profiles"]![0]!["template"] = sheet.Replace('\\', '/');
            File.WriteAllText(manifest, json.ToJsonString());
            var preview = ProposalPreviewSession.Create(new("Synthetic Client", "Synthetic Site", 2026, "Landscape", "TEST-A1"), manifest);
            Plan = preview.Plan;
            Approval = Assert.IsType<ProposalApproval>(preview.TryApprove(Plan.Inputs, manifest));
        }

        public void Dispose() => Directory.Delete(root, true);
    }

    private sealed class RecordingProposalHost : IProposalHost
    {
        public List<string> Calls { get; } = [];
        public string? FailActionId { get; init; }
        public string? SkipActionId { get; init; }
        public bool FailClose { get; init; }
        public int FailCloseOnCount { get; init; }
        public bool FailVerify { get; init; }
        public string? OmitHashRelativePath { get; init; }
        public int CloseCount { get; private set; }
        public Action? OnInspect { get; init; }
        public ProposalIssue[] InspectIssues { get; init; } = [];
        public Action<PlannedAction>? OnApply { get; set; }
        public Action<string>? OnVerify { get; init; }
        public Action<string>? OnFailure { get; init; }
        public PreflightReport Inspect(ProposalPlan plan)
        {
            Calls.Add("Inspect");
            OnInspect?.Invoke();
            return new(InspectIssues);
        }
        public void Apply(PlannedAction action, string stagingRoot)
        {
            Calls.Add(action.Id);
            OnApply?.Invoke(action);
            if (action.Id == FailActionId)
            {
                OnFailure?.Invoke(stagingRoot);
                throw new IOException("Injected action failure.");
            }
            if (action.Id == SkipActionId) return;
            if (action.Operation == ProposalOperation.CreateFolder)
            {
                if (action.RelativePath.Length > 0) ProposalFiles.ReserveStage(ProposalFiles.Child(stagingRoot, action.RelativePath));
            }
            else if (ProposalFiles.IsArtifactCreate(action))
            {
                using var file = new FileStream(ProposalFiles.Child(stagingRoot, action.RelativePath), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                file.WriteByte(42);
            }
        }
        public void CloseCreatedArtifacts()
        {
            Calls.Add("CloseCreatedArtifacts");
            CloseCount++;
            if (FailClose || CloseCount == FailCloseOnCount) throw new IOException("Injected close failure.");
        }
        public VerificationReport Verify(ProposalPlan plan, string artifactRoot)
        {
            Calls.Add("Verify");
            OnVerify?.Invoke(artifactRoot);
            if (FailVerify)
                return new([], [new(ExecutionIssueCodes.VerificationFailed, "Injected verification failure.")], [], []);
            string[] paths = Directory.GetFiles(artifactRoot, "*", SearchOption.AllDirectories);
            string[] observed = paths.Select(path => Path.GetRelativePath(artifactRoot, path).Replace('\\', '/')).ToArray();
            var hashes = paths.Zip(observed, (path, relative) =>
                KeyValuePair.Create(relative, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
                .Where(hash => !string.Equals(hash.Key, OmitHashRelativePath, StringComparison.OrdinalIgnoreCase));
            return new(observed, [], ["Associate synthetic data shortcuts manually."], hashes);
        }
    }
}
