using System.Security.Cryptography;
using Autodesk.AutoCAD.DatabaseServices;
using AutoGIS.Civil3D.Proposal;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoGIS.Civil3D.Adapter;

internal static class NativeProposalVerifier
{
    internal static VerificationReport VerifyModels(ProposalPlan plan, string artifactRoot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var failures = new List<ProposalIssue>();
        var verified = new List<string>();
        var hashes = new List<KeyValuePair<string, string>>();
        var locked = new List<(string RelativePath, string Path, FileStream File, string Hash)>();
        try
        {
            foreach (var action in plan.Actions.Where(a => a.Operation == ProposalOperation.CreateModelDrawing))
            {
                string path = ProposalFiles.Child(artifactRoot, action.RelativePath);
                FileStream? file = null;
                try
                {
                    ProposalFiles.RejectReparseAncestors(path);
                    // Allow Autodesk readers while denying ordinary writes and replacement until all resolution ends.
                    file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    string before = Hash(file);
                    int oldFailures = failures.Count;
                    WithReadDatabase(path, db => CheckReferences(db, plan, action.RelativePath, artifactRoot, failures));
                    if (Hash(file) != before)
                        Fail(failures, action.RelativePath, "Readback changed the model drawing bytes.");
                    if (failures.Count == oldFailures)
                    {
                        locked.Add((action.RelativePath, path, file, before));
                        file = null;
                    }
                }
                catch (System.Exception ex)
                {
                    Fail(failures, action.RelativePath, $"Could not independently read model drawing: {ex.GetType().Name}: {ex.Message}");
                }
                finally { file?.Dispose(); }
            }

            if (failures.Count == 0)
                foreach (var model in locked)
                {
                    try
                    {
                        ProposalFiles.RejectReparseAncestors(model.Path);
                        int oldFailures = failures.Count;
                        WithReadDatabase(model.Path, db =>
                        {
                            db.ResolveXrefs(false, false);
                            CheckLoadedReferences(db, plan, model.RelativePath, artifactRoot, failures);
                        });
                        string after = Hash(model.File);
                        if (after != model.Hash)
                            Fail(failures, model.RelativePath, "Resolution changed the model drawing bytes.");
                        if (failures.Count == oldFailures)
                        {
                            verified.Add(model.RelativePath);
                            hashes.Add(KeyValuePair.Create(model.RelativePath, after));
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Fail(failures, model.RelativePath, $"Could not independently resolve model drawing: {ex.GetType().Name}: {ex.Message}");
                    }
                    if (failures.Count != 0) break;
                }
        }
        finally { foreach (var model in locked) model.File.Dispose(); }
        return new(verified, failures, [], hashes);
    }

    private static void WithReadDatabase(string path, Action<Database> inspect)
    {
        var active = AcApplication.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("A Civil 3D document must remain active on the host thread.");
        Database activeDatabase = active.Database;
        string activeName = active.Name;
        Database previous = HostApplicationServices.WorkingDatabase;
        Database? db = null;
        try
        {
            db = new Database(false, true);
            db.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, false, null);
            db.CloseInput(true);
            if (db.NeedsRecovery) throw new InvalidDataException("Model drawing requires recovery.");
            inspect(db);
        }
        finally
        {
            try { HostApplicationServices.WorkingDatabase = previous; }
            finally { db?.Dispose(); }
        }
        if (!activeDatabase.Equals(AcApplication.DocumentManager.MdiActiveDocument?.Database) ||
            !string.Equals(activeName, AcApplication.DocumentManager.MdiActiveDocument?.Name, StringComparison.Ordinal) ||
            !previous.Equals(HostApplicationServices.WorkingDatabase))
            throw new InvalidOperationException("Readback changed the active document or working database.");
    }

    private static void CheckReferences(Database db, ProposalPlan plan, string hostRelativePath,
        string artifactRoot, List<ProposalIssue> failures)
    {
        var expected = plan.Actions.Where(a => a.Operation == ProposalOperation.AddXref &&
            string.Equals(a.RelativePath, hostRelativePath, StringComparison.OrdinalIgnoreCase))
            .Select(a => (XrefData)a.Data).ToDictionary(x => x.Reference.ReferenceRole, StringComparer.Ordinal);
        foreach (var (role, intent) in expected)
        {
            var model = plan.Configuration.Standards.Models.SingleOrDefault(m => m.Role == role);
            string resolved = NativeProposalHost.ResolveOverlayTarget(artifactRoot, hostRelativePath, intent.RelativeReferencePath);
            if (model is null || !string.Equals(resolved, ProposalFiles.Child(artifactRoot, model.Path), StringComparison.OrdinalIgnoreCase))
                Fail(failures, hostRelativePath, $"Planned Xref target does not match its model role: {role}.");
        }
        using var transaction = db.TransactionManager.StartTransaction();
        var table = (BlockTable)transaction.GetObject(db.BlockTableId, OpenMode.ForRead);
        var external = new HashSet<string>(StringComparer.Ordinal);
        foreach (ObjectId id in table)
        {
            var block = (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead);
            if (!block.IsFromExternalReference) continue;
            if (!external.Add(block.Name))
                Fail(failures, hostRelativePath, $"Duplicate Xref definition: {block.Name}.");
            string? storedTarget = CheckStoredPath(block, hostRelativePath, artifactRoot, failures);
            if (!expected.TryGetValue(block.Name, out var intent))
            {
                Fail(failures, hostRelativePath, $"Unexpected Xref definition: {block.Name}.");
                continue;
            }
            CheckDefinition(block, storedTarget, intent, hostRelativePath, artifactRoot, failures);
        }
        foreach (string missing in expected.Keys.Except(external, StringComparer.Ordinal))
            Fail(failures, hostRelativePath, $"Missing model Xref definition: {missing}.");

        var placements = new Dictionary<string, int>(StringComparer.Ordinal);
        var modelPlacements = new Dictionary<string, int>(StringComparer.Ordinal);
        ObjectId modelSpaceId = table[BlockTableRecord.ModelSpace];
        foreach (ObjectId spaceId in table)
        {
            var space = (BlockTableRecord)transaction.GetObject(spaceId, OpenMode.ForRead);
            if (space.IsFromExternalReference) continue;
            foreach (ObjectId entityId in space)
            {
                if (transaction.GetObject(entityId, OpenMode.ForRead) is not BlockReference reference) continue;
                var definition = (BlockTableRecord)transaction.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                if (!definition.IsFromExternalReference || !expected.TryGetValue(definition.Name, out var intent)) continue;
                string role = definition.Name;
                placements[role] = placements.GetValueOrDefault(role) + 1;
                if (spaceId != modelSpaceId) continue;
                modelPlacements[role] = modelPlacements.GetValueOrDefault(role) + 1;
                if (!TransformMatches(reference, intent.Reference))
                    Fail(failures, hostRelativePath, $"Model Xref transform differs from plan: {role}.");
            }
        }
        foreach (string role in expected.Keys)
        {
            if (placements.GetValueOrDefault(role) != 1 || modelPlacements.GetValueOrDefault(role) != 1)
                Fail(failures, hostRelativePath, $"Model Xref has a missing, duplicate, or non-model insertion: {role}.");
        }
    }

    private static string? CheckStoredPath(BlockTableRecord block, string hostRelativePath,
        string artifactRoot, List<ProposalIssue> failures)
    {
        string stored = block.PathName;
        if (string.IsNullOrWhiteSpace(stored) || Path.IsPathRooted(stored) || stored.Contains(':'))
        {
            Fail(failures, hostRelativePath, $"Xref path is empty or absolute: {block.Name}.");
            return null;
        }
        try { return NativeProposalHost.ResolveOverlayTarget(artifactRoot, hostRelativePath, stored); }
        catch (ProposalConditionException ex)
        {
            Fail(failures, hostRelativePath, $"Xref path escapes owned staging: {block.Name}: {ex.Message}");
            return null;
        }
    }

    private static void CheckDefinition(BlockTableRecord block, string? storedTarget, XrefData intent, string hostRelativePath,
        string artifactRoot, List<ProposalIssue> failures)
    {
        string name = block.Name;
        if (!block.IsFromOverlayReference)
            Fail(failures, hostRelativePath, $"Xref is attached rather than overlaid: {name}.");
        if (storedTarget is null) return;
        string stored = block.PathName;
        if (!string.Equals(Normalize(stored), Normalize(intent.RelativeReferencePath), StringComparison.OrdinalIgnoreCase))
            Fail(failures, hostRelativePath, $"Xref path differs from plan: {name}.");
        string intended = NativeProposalHost.ResolveOverlayTarget(artifactRoot, hostRelativePath, intent.RelativeReferencePath);
        if (!string.Equals(storedTarget, intended, StringComparison.OrdinalIgnoreCase) || !File.Exists(storedTarget))
            Fail(failures, hostRelativePath, $"Xref target is missing or differs from plan: {name}.");
    }

    private static void CheckLoadedReferences(Database db, ProposalPlan plan, string hostRelativePath,
        string artifactRoot, List<ProposalIssue> failures)
    {
        var expected = plan.Actions.Where(a => a.Operation == ProposalOperation.AddXref &&
            string.Equals(a.RelativePath, hostRelativePath, StringComparison.OrdinalIgnoreCase))
            .Select(a => (XrefData)a.Data).ToDictionary(x => x.Reference.ReferenceRole, StringComparer.Ordinal);
        using var transaction = db.TransactionManager.StartTransaction();
        var table = (BlockTable)transaction.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId id in table)
        {
            var block = (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead);
            if (!block.IsFromExternalReference) continue;
            if (!expected.TryGetValue(block.Name, out var intent))
            {
                Fail(failures, hostRelativePath, $"Unexpected loaded Xref definition: {block.Name}.");
                continue;
            }
            string intended = NativeProposalHost.ResolveOverlayTarget(artifactRoot, hostRelativePath, intent.RelativeReferencePath);
            CheckLoadedDefinition(block, intended, hostRelativePath, failures);
        }
    }

    private static void CheckLoadedDefinition(BlockTableRecord block, string intended, string hostRelativePath,
        List<ProposalIssue> failures)
    {
        string name = block.Name;
        if (block.XrefStatus != XrefStatus.Resolved)
        {
            Fail(failures, hostRelativePath, $"Xref is unresolved: {name} ({block.XrefStatus}).");
            return;
        }
        // GetXrefDatabase does not transfer ownership of the loaded reference database.
        Database? loaded = block.GetXrefDatabase(false);
        if (loaded is null || string.IsNullOrWhiteSpace(loaded.Filename) ||
            !Path.IsPathFullyQualified(loaded.Filename) ||
            !string.Equals(ProposalFiles.Full(loaded.Filename), intended, StringComparison.OrdinalIgnoreCase))
            Fail(failures, hostRelativePath, $"Loaded Xref resolves to the wrong drawing: {name}.");
    }

    private static bool TransformMatches(BlockReference reference, XrefStandard expected) =>
        reference.Position.X == expected.Insertion.X && reference.Position.Y == expected.Insertion.Y &&
        reference.Position.Z == expected.Insertion.Z &&
        reference.ScaleFactors.X == expected.Scale && reference.ScaleFactors.Y == expected.Scale &&
        reference.ScaleFactors.Z == expected.Scale && reference.Rotation == expected.Rotation &&
        HasWorldNormal(reference.Normal.X, reference.Normal.Y, reference.Normal.Z);

    internal static bool HasWorldNormal(double x, double y, double z) =>
        Math.Abs(x) <= 1e-9 && Math.Abs(y) <= 1e-9 && Math.Abs(z - 1) <= 1e-9;

    private static string Normalize(string path) => path.Replace('/', '\\');

    private static string Hash(FileStream file)
    {
        file.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(file));
        file.Position = 0;
        return hash;
    }

    private static void Fail(List<ProposalIssue> failures, string path, string message) =>
        failures.Add(new(ExecutionIssueCodes.VerificationFailed, message, path));
}
