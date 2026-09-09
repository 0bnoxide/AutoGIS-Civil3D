using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(AutoGIS.Civil3D.Adapter.NewProposalCommand))]

namespace AutoGIS.Civil3D.Adapter;

public sealed class NewProposalCommand
{
    [CommandMethod("AUTOGISNEWPROPOSAL", CommandFlags.Modal)]
    public void Run()
    {
        if (!TryGetActiveDocument("starting New Proposal", out Document? document))
            return;
        try
        {
            (Version? autocad, Version? civil) = HostVersions();
            if (!HostCompatibility.IsSupported(autocad, civil, Environment.Is64BitProcess))
            {
                document!.Editor.WriteMessage("\nNew Proposal requires Civil 3D 2026 (AutoCAD 25.1 / Civil 13.8), x64.");
                return;
            }
            using var form = new NewProposalForm();
            Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
        }
        catch (System.Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException)
        {
            document!.Editor.WriteMessage("\nNew Proposal could not load the Civil 3D 2026 API. Run it inside the supported Civil 3D host.");
        }
    }

    [CommandMethod("AUTOGISPROPOSALSMOKE", CommandFlags.Modal)]
    public void Smoke()
    {
        if (!TryGetActiveDocument("running the New Proposal smoke check", out Document? document))
            return;
        try
        {
            (Version? autocad, Version? civil) = HostVersions();
            document!.Editor.WriteMessage($"\n{HostCompatibility.BindingReport(autocad, civil, Environment.Is64BitProcess)}");
        }
        catch (System.Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException)
        {
            document!.Editor.WriteMessage(
                $"\nAUTOGISPROPOSALSMOKE FAIL: could not load the Civil 3D 2026 API ({ex.GetType().Name}). " +
                "NETLOAD the preview in Civil 3D 2026 and confirm its installed API assemblies are available.");
        }
    }

    private static bool TryGetActiveDocument(string action, out Document? document)
    {
        document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
            return true;
        AcApplication.ShowAlertDialog($"Open a drawing before {action}.");
        return false;
    }

    private static (Version? AutoCAD, Version? Civil) HostVersions() =>
        (typeof(Document).Assembly.GetName().Version, CivilVersion());

    // Keep Civil binding inside the guarded call so plain AutoCAD can report a missing Civil API.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Version? CivilVersion() =>
        Type.GetType("Autodesk.Civil.ApplicationServices.CivilDocument, AeccDbMgd", throwOnError: true)!
            .Assembly.GetName().Version;
}
