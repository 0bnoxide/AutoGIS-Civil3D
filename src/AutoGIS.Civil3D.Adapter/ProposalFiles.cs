using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoGIS.Civil3D.Proposal;

namespace AutoGIS.Civil3D.Adapter;

internal sealed class ProposalConditionException : IOException
{
    internal string Code { get; }

    internal ProposalConditionException(string code, string message) : base(message) => Code = code;
}

internal static class ProposalFiles
{
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateDirectoryExclusive(string path, nint securityAttributes);

    internal static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static bool EntryExists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    internal static void RejectReparseAncestors(string path)
    {
        for (string? part = Full(path); part is not null; part = Path.GetDirectoryName(part))
        {
            try
            {
                if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                    throw new ProposalConditionException(ExecutionIssueCodes.UnsafeExecutionPath, $"Reparse point is not permitted: {part}");
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
        }
    }

    internal static string Child(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\\') || Path.IsPathRooted(relative) ||
            relative.Split('/').Any(part => part.Length == 0 || part is "." or ".." || part.Contains(':')))
            throw new ProposalConditionException(ExecutionIssueCodes.UnsafeExecutionPath, "The plan contains an unsafe relative artifact path.");
        string child = Full(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!Within(child, root)) throw new ProposalConditionException(ExecutionIssueCodes.UnsafeExecutionPath, "A planned artifact escapes the staging root.");
        return child;
    }

    internal static bool Within(string child, string root) =>
        Full(child).StartsWith(Full(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static string ValidateAndStagePath(ProposalPlan plan, Guid runId, string failureDirectory)
    {
        if (runId == Guid.Empty) throw new InvalidDataException("A nonempty run ID is required.");
        string baseRoot = Full(plan.FinalRootComponents[0]);
        string name = plan.FinalRootComponents[1];
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0 ||
            !Paths.Equals(Full(Path.Combine(baseRoot, name)), Full(plan.FinalRoot)))
            throw new ProposalConditionException(ExecutionIssueCodes.UnsafeExecutionPath, "The planned final root is not a direct child of its approved base root.");
        if (!Directory.Exists(baseRoot)) throw new DirectoryNotFoundException($"The approved base root does not exist: {baseRoot}");
        string finalRoot = Full(plan.FinalRoot);
        string stage = StagePath(finalRoot, runId);
        string failureRoot = Full(failureDirectory);
        if (Paths.Equals(failureRoot, finalRoot) || Within(failureRoot, finalRoot) || Paths.Equals(failureRoot, stage) || Within(failureRoot, stage))
            throw new ProposalConditionException(ExecutionIssueCodes.UnsafeExecutionPath, "Failure receipts must be outside the proposal and staging roots.");
        RejectReparseAncestors(baseRoot);
        RejectReparseAncestors(finalRoot);
        RejectReparseAncestors(stage);
        RejectReparseAncestors(failureRoot);
        foreach (var action in plan.Actions)
            if (action.RelativePath.Length != 0) _ = Child(stage, action.RelativePath);
        return stage;
    }

    internal static void RefuseExisting(string finalRoot, string stage)
    {
        RejectReparseAncestors(finalRoot);
        RejectReparseAncestors(stage);
        if (EntryExists(finalRoot)) throw new ProposalConditionException(ExecutionIssueCodes.TargetExists, "The proposal target already exists.");
        if (EntryExists(stage)) throw new ProposalConditionException(ExecutionIssueCodes.TargetExists, "An earlier staging root exists for this run ID; it will not be adopted.");
        string prefix = StagePrefix(finalRoot);
        string parent = Path.GetDirectoryName(Full(stage))!;
        if (Directory.EnumerateFileSystemEntries(parent)
            .Any(entry => Path.GetFileName(entry).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            throw new ProposalConditionException(ExecutionIssueCodes.TargetExists, "An unfinished staging root exists for this proposal; inspect it before rerunning.");
    }

    internal static string StagePath(string finalRoot, Guid runId) =>
        Path.Combine(Path.GetDirectoryName(Full(finalRoot))!, StagePrefix(finalRoot) + runId.ToString("N"));

    private static string StagePrefix(string finalRoot)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Full(finalRoot).ToUpperInvariant()));
        return ".autogis-stage-" + Convert.ToHexString(hash.AsSpan(0, 8)) + "-";
    }

    internal static void ProbeWritable(string directory, Guid runId)
    {
        RejectReparseAncestors(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Required writable directory does not exist: {directory}");
        string probe = Path.Combine(directory, $".autogis-write-probe-{runId:N}");
        using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
        {
            stream.WriteByte(0);
            stream.Flush(true);
        }
        string directoryProbe = Path.Combine(directory, $".autogis-directory-probe-{runId:N}");
        ReserveStage(directoryProbe);
        Directory.Delete(directoryProbe);
    }

    internal static void ReserveStage(string stage)
    {
        RejectReparseAncestors(stage);
        if (!CreateDirectoryExclusive(stage, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error is 80 or 183)
                throw new ProposalConditionException(ExecutionIssueCodes.TargetExists, $"Staging root already exists: {stage}");
            throw new IOException($"Could not exclusively reserve staging root: {stage}", new Win32Exception(error));
        }
    }

    internal static HashSet<string> ExpectedArtifacts(ProposalPlan plan) =>
        plan.Actions.Where(IsArtifactCreate).Select(a => a.RelativePath).ToHashSet(Paths);

    internal static bool IsArtifactCreate(PlannedAction action) => action.Operation switch
    {
        ProposalOperation.CreateModelDrawing or ProposalOperation.CreateSheetDrawing or ProposalOperation.CreateSheetSet => true,
        ProposalOperation.WriteSupportRecord when action.Data is SupportRecordData { Content: not SupportContent.ReceiptAfterVerification } => true,
        _ => false
    };

    internal static string ReceiptRelativePath(ProposalPlan plan) =>
        plan.Actions.Single(a => a.Data is SupportRecordData { Content: SupportContent.ReceiptAfterVerification }).RelativePath;

    internal static void ValidateObservedArtifacts(ProposalPlan plan, VerificationReport report, string stage)
    {
        var expected = ExpectedArtifacts(plan);
        var observed = report.VerifiedArtifacts.ToHashSet(Paths);
        if (observed.Count != report.VerifiedArtifacts.Length || !expected.SetEquals(observed))
            throw new InvalidDataException("Independent verification did not report exactly the planned artifacts.");
        foreach (string relative in observed)
        {
            string path = Child(stage, relative);
            RejectReparseAncestors(path);
            if (!File.Exists(path)) throw new FileNotFoundException("Verified artifact is missing on disk.", path);
        }
    }

    internal static void ValidateOwnedTree(string stage, HashSet<string> ownedFiles, HashSet<string> ownedDirectories)
    {
        RejectReparseAncestors(stage);
        if (!Directory.Exists(stage)) throw new IOException("The owned staging root disappeared.");
        foreach (string entry in EnumerateTree(stage))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new ProposalConditionException(ExecutionIssueCodes.UnsafeExecutionPath, $"A reparse point appeared in staging: {entry}");
            if ((attributes & FileAttributes.Directory) != 0 ? !ownedDirectories.Contains(entry) : !ownedFiles.Contains(entry))
                throw new IOException($"A foreign entry appeared in staging: {entry}");
        }
        foreach (string directory in ownedDirectories)
            if (!Directory.Exists(directory)) throw new IOException($"A planned directory is missing or changed type: {directory}");
        foreach (string file in ownedFiles)
            if (!File.Exists(file)) throw new IOException($"A planned file is missing or changed type: {file}");
    }

    internal static void TrackAction(string stage, PlannedAction action, HashSet<string> ownedFiles, HashSet<string> ownedDirectories)
    {
        if (action.RelativePath.Length == 0) return;
        string path = Child(stage, action.RelativePath);
        if (action.Operation == ProposalOperation.CreateFolder) ownedDirectories.Add(path);
        else if (IsArtifactCreate(action)) ownedFiles.Add(path);
    }

    internal static void RequireFreshActionTarget(string stage, PlannedAction action)
    {
        if (action.RelativePath.Length == 0) return;
        string path = Child(stage, action.RelativePath);
        RejectReparseAncestors(path);
        if ((action.Operation == ProposalOperation.CreateFolder || IsArtifactCreate(action)) && EntryExists(path))
            throw new ProposalConditionException(ExecutionIssueCodes.TargetExists, $"A planned output already exists before exclusive creation: {path}");
    }

    internal static void WriteReceipt(string path, RunReceipt receipt, Action onCreated, Action<FileStream>? beforeFlush = null)
    {
        RejectReparseAncestors(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        onCreated();
        JsonSerializer.Serialize(stream, receipt);
        beforeFlush?.Invoke(stream);
        stream.Flush(true);
    }

    internal static void Publish(string stage, string finalRoot)
    {
        RejectReparseAncestors(finalRoot);
        if (EntryExists(finalRoot)) throw new ProposalConditionException(ExecutionIssueCodes.TargetExists, "The proposal target appeared before publication.");
        RejectReparseAncestors(stage);
        try { Directory.Move(stage, finalRoot); }
        catch (IOException) when (EntryExists(finalRoot))
        {
            throw new ProposalConditionException(ExecutionIssueCodes.TargetExists, "The proposal target appeared during publication.");
        }
    }

    internal static string? Cleanup(string stage)
    {
        try
        {
            if (!EntryExists(stage)) return null;
            RejectReparseAncestors(stage);
            if (!Directory.Exists(stage) || Directory.EnumerateFileSystemEntries(stage).Any())
                return $"Staging retained for inspection because it is not an empty owned directory: {stage}";
            Directory.Delete(stage);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"Staging cleanup retained {stage}: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static IEnumerable<string> EnumerateTree(string root)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            yield return entry;
            if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0 &&
                (File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0)
                foreach (string child in EnumerateTree(entry)) yield return child;
        }
    }
}
