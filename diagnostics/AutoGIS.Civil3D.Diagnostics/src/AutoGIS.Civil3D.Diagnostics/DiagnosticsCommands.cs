using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoGIS.Civil3D.Diagnostics;

public sealed class DiagnosticsCommands
{
    [CommandMethod("AUTOGISDIAGNOSTICS", CommandFlags.Modal)]
    public void RunDiagnostics()
    {
        Document? document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        Editor editor = document.Editor;

        WriteHeader(editor, "AutoGIS Civil 3D Diagnostics 0.1.1");
        WriteLine(editor, "LOAD", "PASS - the managed plug-in loaded and the command started.");
        WriteLine(editor, "SAFETY", "Read-only command: no drawing, file, registry, network, or subprocess changes.");
        WriteLine(editor, "PRIVACY", "Output can contain drawing, plug-in, and trusted-path locations. Redact paths before external sharing.");

        bool runtimeSucceeded = RunSection(editor, "Plug-in and runtime", () =>
        {
            Assembly plugInAssembly = Assembly.GetExecutingAssembly();
            WriteValue(editor, "Plug-in version", plugInAssembly.GetName().Version?.ToString() ?? "<unknown>");
            WriteValue(editor, "Plug-in path", plugInAssembly.Location);
            WriteValue(editor, ".NET runtime", RuntimeInformation.FrameworkDescription);
            WriteValue(editor, "Operating system", RuntimeInformation.OSDescription);
            WriteValue(editor, "Process architecture", RuntimeInformation.ProcessArchitecture.ToString());
            WriteValue(editor, "64-bit process", Environment.Is64BitProcess.ToString());
            WriteValue(editor, "AutoCAD API assembly", typeof(Document).Assembly.GetName().Version?.ToString() ?? "<unknown>");
            WriteValue(editor, "Civil 3D API assembly", typeof(CivilDocument).Assembly.GetName().Version?.ToString() ?? "<unknown>");
        });

        bool securitySucceeded = RunSection(editor, "Host and security", () =>
        {
            WriteValue(editor, "ACADVER", ReadSystemVariable("ACADVER"));
            WriteValue(editor, "SECURELOAD", ReadSystemVariable("SECURELOAD"));
            WriteValue(editor, "APPAUTOLOAD", ReadSystemVariable("APPAUTOLOAD"));
            WriteValue(editor, "TRUSTEDPATHS", ReadSystemVariable("TRUSTEDPATHS"));
        });

        bool drawingSucceeded = RunSection(editor, "Active drawing", () =>
        {
            Database database = document.Database;
            WriteValue(editor, "Drawing", document.Name);
            WriteValue(editor, "AutoCAD insertion units", database.Insunits.ToString());
        });

        bool civilSettingsSucceeded = RunSection(editor, "Civil 3D drawing settings", () =>
        {
            CivilDocument civilDocument = CivilApplication.ActiveDocument;
            var unitZone = civilDocument.Settings.DrawingSettings.UnitZoneSettings;
            string coordinateSystem = unitZone.CoordinateSystemCode;

            WriteValue(editor, "Drawing units", unitZone.DrawingUnits.ToString());
            WriteValue(editor, "Imperial conversion", unitZone.ImperialToMetricConversion.ToString());
            WriteValue(
                editor,
                "Coordinate-system code",
                string.IsNullOrWhiteSpace(coordinateSystem) || coordinateSystem == "."
                    ? "<not assigned>"
                    : coordinateSystem);
            WriteValue(editor, "Angular units", unitZone.AngularUnits.ToString());
            WriteValue(editor, "Drawing scale", unitZone.DrawingScale.ToString());
        });

        bool civilInventorySucceeded = RunSection(editor, "Civil 3D object inventory", () =>
        {
            CivilDocument civilDocument = CivilApplication.ActiveDocument;
            WriteValue(editor, "COGO points", civilDocument.GetAllPointIds().Count.ToString());
            WriteValue(editor, "Surfaces", civilDocument.GetSurfaceIds().Count.ToString());
            WriteValue(editor, "Alignments", civilDocument.GetAlignmentIds().Count.ToString());
            WriteValue(editor, "Sites", civilDocument.GetSiteIds().Count.ToString());
            WriteValue(editor, "Corridors", civilDocument.CorridorCollection.Count.ToString());
            WriteValue(editor, "Gravity pipe networks", civilDocument.GetPipeNetworkIds().Count.ToString());
        });

        bool allSectionsSucceeded =
            runtimeSucceeded &&
            securitySucceeded &&
            drawingSucceeded &&
            civilSettingsSucceeded &&
            civilInventorySucceeded;

        WriteHeader(editor, "Diagnostic complete");
        WriteLine(
            editor,
            "RESULT",
            allSectionsSucceeded
                ? "PASS - the plug-in can execute the tested managed Civil 3D API calls on this workstation."
                : "PARTIAL - the plug-in loaded, but one or more diagnostic sections failed; review each ERROR above.");
        WriteLine(editor, "NOTICE", "No drawing changes were made.");
    }

    private static string ReadSystemVariable(string name)
    {
        object? value = AcApplication.GetSystemVariable(name);
        return value?.ToString() ?? "<null>";
    }

    private static bool RunSection(Editor editor, string title, Action action)
    {
        editor.WriteMessage($"\n\n-- {title} --");
        try
        {
            action();
            WriteLine(editor, "SECTION", "PASS");
            return true;
        }
        catch (Exception exception)
        {
            WriteLine(editor, "ERROR", $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static void WriteHeader(Editor editor, string text)
    {
        editor.WriteMessage($"\n\n=== {text} ===");
    }

    private static void WriteValue(Editor editor, string name, string value)
    {
        editor.WriteMessage($"\n{name}: {value}");
    }

    private static void WriteLine(Editor editor, string label, string value)
    {
        editor.WriteMessage($"\n{label}: {value}");
    }
}
