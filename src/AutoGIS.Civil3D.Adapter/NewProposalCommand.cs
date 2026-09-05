using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(AutoGIS.Civil3D.Adapter.NewProposalCommand))]

namespace AutoGIS.Civil3D.Adapter;

public sealed class NewProposalCommand
{
    [CommandMethod("AUTOGISNEWPROPOSAL", CommandFlags.Modal)]
    public void Run()
    {
        Document? document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            AcApplication.ShowAlertDialog("Open a drawing before starting New Proposal.");
            return;
        }
        try
        {
            if (!HostCompatibility.IsSupported(typeof(Document).Assembly.GetName().Version,
                CivilVersion(), Environment.Is64BitProcess))
            {
                document.Editor.WriteMessage("\nNew Proposal requires Civil 3D 2026 (AutoCAD 25.1 / Civil 13.8), x64.");
                return;
            }
            using var form = new NewProposalForm();
            Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
        }
        catch (System.Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException)
        {
            document.Editor.WriteMessage("\nNew Proposal could not load the Civil 3D 2026 API. Run it inside the supported Civil 3D host.");
        }
    }

    // Keep Civil binding inside the guarded call so plain AutoCAD can report a missing Civil API.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Version? CivilVersion() => typeof(CivilDocument).Assembly.GetName().Version;
}
