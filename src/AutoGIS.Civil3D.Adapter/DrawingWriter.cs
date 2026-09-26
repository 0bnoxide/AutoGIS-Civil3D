using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AutoGIS.Civil3D.Proposal;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoGIS.Civil3D.Adapter;

internal static class DrawingWriter
{
    internal static void CreateModel(PlannedAction action, string stagingRoot)
    {
        if (action is not { Operation: ProposalOperation.CreateModelDrawing, Data: ModelDrawingData data })
            throw new InvalidDataException("Expected a model drawing action.");
        string output = ProposalFiles.Child(stagingRoot, action.RelativePath);
        ProposalFiles.RejectReparseAncestors(data.Template);
        if (!File.Exists(data.Template)) throw new FileNotFoundException("The approved model template is missing.", data.Template);
        if (ProposalFiles.EntryExists(output))
            throw new ProposalConditionException(ExecutionIssueCodes.TargetExists, "Model drawing output already exists.");
        SaveSideDatabase(data.Template, output, null, replace: false);
    }

    internal static void AddModelOverlay(PlannedAction action, string stagingRoot)
    {
        if (action is not { Operation: ProposalOperation.AddXref, Data: XrefData data } ||
            data.Reference is not { HostRole: "ProposedDesignModel", Mode: "Overlay" })
            throw new InvalidDataException("Expected a model overlay action.");
        string host = ProposalFiles.Child(stagingRoot, action.RelativePath);
        string target = NativeProposalHost.ResolveOverlayTarget(stagingRoot, action.RelativePath, data.RelativeReferencePath);
        ProposalFiles.RejectReparseAncestors(host);
        if (!File.Exists(host) || !File.Exists(target))
            throw new FileNotFoundException("A planned model overlay drawing is missing.");
        SaveSideDatabase(host, host, db =>
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
        }, replace: true);
    }

    private static void SaveSideDatabase(string source, string destination, Action<Database>? change, bool replace)
    {
        var active = AcApplication.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("A Civil 3D document must remain active on the host thread.");
        Database activeDatabase = active.Database;
        string activeName = active.Name;
        Database previous = HostApplicationServices.WorkingDatabase;
        Database? side = null;
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!,
            $".autogis-{Guid.NewGuid():N}.dwg");
        ProposalFiles.RejectReparseAncestors(source);
        ProposalFiles.RejectReparseAncestors(destination);
        using FileStream? original = replace
            ? new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)
            : null;
        try
        {
            ProposalFiles.RejectReparseAncestors(destination);
            if (ProposalFiles.EntryExists(temporary)) throw new IOException("Temporary drawing path already exists.");
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
        if (!replace)
        {
            File.Move(temporary, destination);
            return;
        }

        ProposalFiles.RejectReparseAncestors(destination);
        ProposalFiles.RejectReparseAncestors(temporary);
        using var saved = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.DeleteOnClose);
        original!.Position = 0;
        saved.CopyTo(original);
        original.SetLength(saved.Length);
        original.Flush(true);
    }
}
