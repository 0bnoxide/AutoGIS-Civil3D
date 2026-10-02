using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AutoGIS.Civil3D.Proposal;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoGIS.Civil3D.Adapter;

internal static class DrawingWriter
{
    internal static string CreateModel(PlannedAction action, string stagingRoot, string localData)
    {
        if (action is not { Operation: ProposalOperation.CreateModelDrawing, Data: ModelDrawingData data })
            throw new InvalidDataException("Expected a model drawing action.");
        string output = ProposalFiles.Child(stagingRoot, action.RelativePath);
        ProposalFiles.RejectReparseAncestors(data.Template);
        if (!File.Exists(data.Template)) throw new FileNotFoundException("The approved model template is missing.", data.Template);
        if (ProposalFiles.EntryExists(output))
            throw new ProposalConditionException(ExecutionIssueCodes.TargetExists, "Model drawing output already exists.");
        return SaveSideDatabase(data.Template, output, null, replace: false, expectedSourceHash: null, localData);
    }

    internal static string AddModelOverlay(PlannedAction action, string stagingRoot,
        string expectedHostHash, string expectedTargetHash, string localData)
    {
        if (action is not { Operation: ProposalOperation.AddXref, Data: XrefData data } ||
            data.Reference is not { HostRole: "ProposedDesignModel", Mode: "Overlay" })
            throw new InvalidDataException("Expected a model overlay action.");
        string host = ProposalFiles.Child(stagingRoot, action.RelativePath);
        string target = NativeProposalHost.ResolveOverlayTarget(stagingRoot, action.RelativePath, data.RelativeReferencePath);
        ProposalFiles.RejectReparseAncestors(host);
        if (!File.Exists(host) || !File.Exists(target))
            throw new FileNotFoundException("A planned model overlay drawing is missing.");
        using var targetGuard = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        ProposalFiles.RequireCreatedHash(targetGuard, expectedTargetHash);
        return SaveSideDatabase(host, host, db =>
        {
            ObjectId definition = db.OverlayXref(data.RelativeReferencePath, data.Reference.ReferenceRole);
            if (definition.IsNull) throw new InvalidDataException("OverlayXref did not create a definition.");
            using var transaction = db.TransactionManager.StartTransaction();
            var table = (BlockTable)transaction.GetObject(db.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            var at = data.Reference.Insertion;
            using var reference = new BlockReference(new Point3d(at.X, at.Y, at.Z), definition)
            {
                ScaleFactors = new Scale3d(data.Reference.Scale),
                Rotation = data.Reference.Rotation
            };
            modelSpace.AppendEntity(reference);
            transaction.AddNewlyCreatedDBObject(reference, true);
            transaction.Commit();
        }, replace: true, expectedSourceHash: expectedHostHash, localData);
    }

    private static string SaveSideDatabase(string source, string destination, Action<Database>? change,
        bool replace, string? expectedSourceHash, string localData)
    {
        var active = AcApplication.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("A Civil 3D document must remain active on the host thread.");
        Database activeDatabase = active.Database;
        string activeName = active.Name;
        Database previous = HostApplicationServices.WorkingDatabase;
        Database? side = null;
        ProposalFiles.RejectReparseAncestors(source);
        ProposalFiles.RejectReparseAncestors(destination);
        using FileStream? original = replace
            ? new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)
            : null;
        if (replace) ProposalFiles.RequireCreatedHash(original!, expectedSourceHash!);
        string? createdHash = null;
        WithReservedScratchDrawing(localData, temporary =>
        {
            try
            {
                ProposalFiles.RejectReparseAncestors(destination);
                side = new Database(false, true);
                side.ReadDwgFile(source, FileOpenMode.OpenForReadAndAllShare, false, null);
                side.CloseInput(true);
                if (side.NeedsRecovery) throw new InvalidDataException("A source drawing requires recovery.");
                HostApplicationServices.WorkingDatabase = side;
                change?.Invoke(side);
                side.SaveAs(temporary, DwgVersion.AC1032);
            }
            finally
            {
                try { HostApplicationServices.WorkingDatabase = previous; }
                finally { side?.Dispose(); }
            }
            if (!activeDatabase.Equals(AcApplication.DocumentManager.MdiActiveDocument?.Database) ||
                !string.Equals(activeName, AcApplication.DocumentManager.MdiActiveDocument?.Name, StringComparison.Ordinal) ||
                !previous.Equals(HostApplicationServices.WorkingDatabase))
                throw new InvalidOperationException("Native drawing work changed the active document or working database.");
            ProposalFiles.RejectReparseAncestors(temporary);
            if (!replace)
            {
                createdHash = ProposalFiles.PublishScratch(temporary, destination);
            }
            else
            {
                ProposalFiles.RejectReparseAncestors(destination);
                using var saved = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read,
                    4096, FileOptions.DeleteOnClose);
                original!.Position = 0;
                saved.CopyTo(original);
                original.SetLength(saved.Length);
                original.Flush(true);
                createdHash = ProposalFiles.Hash(original);
            }
        });
        return createdHash ?? throw new InvalidDataException("Native save did not capture a model drawing digest.");
    }

    internal static string ScratchRoot(string localData)
    {
        if (localData.Length == 0) throw new IOException("The user's local application-data folder is unavailable.");
        string root = Path.Combine(localData, "AutoGIS", "scratch");
        ProposalFiles.RejectReparseAncestors(root);
        Directory.CreateDirectory(root);
        return root;
    }

    internal static void WithReservedScratchDrawing(string localData, Action<string> saveAndPublish)
    {
        string parent = ScratchRoot(localData);
        string scratch = Path.Combine(parent, $"save-{Guid.NewGuid():N}");
        string temporary = Path.Combine(scratch, "drawing.dwg");
        // ponytail: Managed SaveAs takes a path, so its leaf in this fresh user-private folder is the one
        // non-exclusive leaf; a same-user process is inside the trust boundary. Close it with a handle-based save.
        ProposalFiles.ReserveStage(scratch);
        bool committed = false;
        try
        {
            ProposalFiles.RejectReparseAncestors(temporary);
            if (ProposalFiles.EntryExists(temporary)) throw new IOException("Temporary drawing path already exists.");
            saveAndPublish(temporary);
            committed = true;
        }
        finally
        {
            string? cleanup = ProposalFiles.Cleanup(scratch);
            if (committed && cleanup is not null) throw new IOException(cleanup);
        }
    }
}
