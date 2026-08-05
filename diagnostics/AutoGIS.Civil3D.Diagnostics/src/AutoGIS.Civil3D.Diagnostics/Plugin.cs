using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(AutoGIS.Civil3D.Diagnostics.Plugin))]
[assembly: CommandClass(typeof(AutoGIS.Civil3D.Diagnostics.DiagnosticsCommands))]

namespace AutoGIS.Civil3D.Diagnostics;

/// <summary>
/// Minimal lifecycle implementation. It deliberately performs no work at load
/// time so the security pilot tests only Autodesk's managed plug-in loader.
/// </summary>
public sealed class Plugin : IExtensionApplication
{
    public void Initialize()
    {
        // Intentionally empty: load-time work would widen the pilot's scope.
    }

    public void Terminate()
    {
        // Intentionally empty: nothing was acquired in Initialize.
    }
}
