using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoGIS.Civil3D.Proposal;

public sealed record ProposalBaseRoot(int Year, string Path);
public sealed record ModelStandard(string Role, string Path);
public sealed record SheetStandard(string Role, string Path, string Number, string Title);
public sealed record SupportStandard(string Role, string Path);
public sealed record PropertyMapping(string Input, string DstProperty, string TitleBlockAttribute);
public sealed record Rectangle(double X, double Y, double Width, double Height);
public sealed record Point3(double X, double Y, double Z);
public sealed record PlaceholderStandard(string SheetRole, string Label, string Kind, Rectangle Rectangle);
public sealed record PlotStandard(string Device, string Media, string StyleSheet, string Units, double Scale, int Rotation);
public sealed record SheetProfile(string Orientation, string Size, string Template, string Layout,
    string TitleBlock, string PageSetup, PlotStandard Plot, Rectangle PrintableArea,
    ImmutableArray<PlaceholderStandard> Placeholders);
public sealed record XrefStandard(string HostRole, string ReferenceRole, string Mode,
    Point3 Insertion, double Scale, double Rotation);

/// <summary>Validated standards data. Construction is only through the fail-closed parser.</summary>
public sealed class StandardsManifest
{
    internal static readonly ImmutableArray<string> ModelRoles = ["BaseModel", "ExistingConditionsModel", "ProposedDesignModel"];
    internal static readonly ImmutableArray<string> SheetRoles = ["SiteOverviewSheet", "ExistingConditionsSheet", "ProposedSitePlanSheet"];
    internal static readonly ImmutableArray<string> SupportRoles = ["ProjectConfiguration", "SourceRegister", "AssumptionsLog", "DecisionLog", "CreationPlan", "CreationReceipt"];
    internal static readonly ImmutableArray<string> InputFields = ["ClientName", "SiteName", "ProposalYear", "Orientation", "SheetSize", "ClientNumber", "ProjectNumber", "ProposalNumber", "SiteAddress", "ProjectManager"];

    public int Version { get; } = 1;
    public ImmutableArray<ProposalBaseRoot> BaseRoots { get; }
    public ImmutableArray<string> Folders { get; }
    public string ModelTemplate { get; }
    public ImmutableArray<ModelStandard> Models { get; }
    public ImmutableArray<SheetStandard> Sheets { get; }
    public string SheetSetPath { get; }
    public ImmutableArray<SupportStandard> SupportRecords { get; }
    public string DataShortcutPath { get; }
    public ImmutableArray<SheetProfile> Profiles { get; }
    public ImmutableArray<PropertyMapping> PropertyMappings { get; }
    public ImmutableArray<XrefStandard> Xrefs { get; }

    private StandardsManifest(JsonElement root)
    {
        Object(root, "version", "baseRoots", "folders", "modelTemplate", "models", "sheets", "sheetSetPath",
            "supportRecords", "dataShortcutPath", "profiles", "propertyMappings", "xrefs");
        BaseRoots = Items(root, "baseRoots", item =>
        {
            Object(item, "year", "path");
            return new ProposalBaseRoot(Integer(item, "year"), Absolute(Text(item, "path")));
        }).OrderBy(r => r.Year).ToImmutableArray();
        Folders = Items(root, "folders", item => Relative(Text(item))).Order(StringComparer.Ordinal).ToImmutableArray();
        ModelTemplate = Absolute(Text(root, "modelTemplate"));
        Extension(ModelTemplate, ".dwt");
        Models = Items(root, "models", item =>
        {
            Object(item, "role", "path");
            return new ModelStandard(Text(item, "role"), Artifact(item, ".dwg"));
        }).OrderBy(m => m.Role, StringComparer.Ordinal).ToImmutableArray();
        Sheets = Items(root, "sheets", item =>
        {
            Object(item, "role", "path", "number", "title");
            return new SheetStandard(Text(item, "role"), Artifact(item, ".dwg"), Text(item, "number"), Text(item, "title"));
        }).OrderBy(s => s.Role, StringComparer.Ordinal).ToImmutableArray();
        SheetSetPath = Relative(Text(root, "sheetSetPath"));
        Extension(SheetSetPath, ".dst");
        SupportRecords = Items(root, "supportRecords", item =>
        {
            Object(item, "role", "path");
            return new SupportStandard(Text(item, "role"), Artifact(item, ".json"));
        }).OrderBy(s => s.Role, StringComparer.Ordinal).ToImmutableArray();
        DataShortcutPath = Relative(Text(root, "dataShortcutPath"));
        Profiles = Items(root, "profiles", Profile).OrderBy(p => p.Orientation, StringComparer.Ordinal)
            .ThenBy(p => p.Size, StringComparer.Ordinal).ToImmutableArray();
        PropertyMappings = Items(root, "propertyMappings", item =>
        {
            Object(item, "input", "dstProperty", "titleBlockAttribute");
            return new PropertyMapping(Text(item, "input"), Text(item, "dstProperty"), Text(item, "titleBlockAttribute"));
        }).OrderBy(p => p.Input, StringComparer.Ordinal).ToImmutableArray();
        Xrefs = Items(root, "xrefs", item =>
        {
            Object(item, "hostRole", "referenceRole", "mode", "insertion", "scale", "rotation");
            var point = item.GetProperty("insertion");
            Object(point, "x", "y", "z");
            return new XrefStandard(Text(item, "hostRole"), Text(item, "referenceRole"), Text(item, "mode"),
                new Point3(Number(point, "x"), Number(point, "y"), Number(point, "z")), Number(item, "scale"), Number(item, "rotation"));
        }).OrderBy(x => x.HostRole, StringComparer.Ordinal).ThenBy(x => x.ReferenceRole, StringComparer.Ordinal).ToImmutableArray();
        Validate();
    }

    public string ToJson() => JsonSerializer.Serialize(this, PlanJson.Options);

    public static ManifestResult Parse(ReadOnlySpan<byte> utf8)
    {
        try
        {
            // JsonDocument otherwise permits replacement decoding of malformed string bytes.
            string json = new UTF8Encoding(false, true).GetString(utf8);
            using var document = JsonDocument.Parse(json);
            CheckDuplicates(document.RootElement);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw Invalid("Manifest must be an object.");
            if (Integer(document.RootElement, "version") != 1)
                throw new ManifestException(ProposalIssueCodes.UnsupportedVersion, "Only manifest version 1 is supported.");
            return new(new StandardsManifest(document.RootElement), []);
        }
        catch (ManifestException ex)
        {
            return new(null, [new(ex.Code, ex.Message, ex.Location)]);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        {
            return new(null, [new(ProposalIssueCodes.InvalidJson, "Manifest must be strict UTF-8 JSON.")]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            return new(null, [new(ProposalIssueCodes.InvalidManifest, "A required manifest value is missing or has the wrong type.")]);
        }
    }

    private void Validate()
    {
        Require(BaseRoots.Length > 0 && BaseRoots.All(r => r.Year is >= 1 and <= 9999), "Declare valid proposal years and roots.");
        Unique(BaseRoots.Select(r => r.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        ExactRoles(Models.Select(m => m.Role), ModelRoles);
        ExactRoles(Sheets.Select(s => s.Role), SheetRoles);
        ExactRoles(SupportRecords.Select(s => s.Role), SupportRoles);
        Unique(Sheets.Select(s => s.Number));
        ExactRoles(PropertyMappings.Select(p => p.Input), InputFields);
        Unique(PropertyMappings.Select(p => p.DstProperty));
        Unique(PropertyMappings.Select(p => p.TitleBlockAttribute));
        Require(Profiles.Length > 0, "Declare at least one sheet profile.");
        Unique(Profiles.Select(p => p.Orientation + "/" + p.Size));
        foreach (var profile in Profiles)
        {
            Require(profile.Orientation is "Landscape" or "Portrait", "Orientation must be Landscape or Portrait.");
            foreach (var role in SheetRoles)
                Require(profile.Placeholders.Count(p => p.SheetRole == role) == (role == "SiteOverviewSheet" ? 2 : 1), "Declare the required starter placeholders.");
            Require(profile.Placeholders.Length == 4, "Unexpected sheet placeholder.");
            var labels = profile.Placeholders.Where(p => p.SheetRole == "SiteOverviewSheet").Select(p => p.Label).ToHashSet(StringComparer.Ordinal);
            Require(labels.SetEquals(["SITE LOCATION", "SITE OVERVIEW"]), "The overview sheet requires SITE LOCATION and SITE OVERVIEW labels.");
            foreach (var placeholder in profile.Placeholders)
            {
                Require(placeholder.Kind is "Boundary" or "DisabledViewport", "Declare a supported placeholder kind explicitly.");
                var a = profile.PrintableArea;
                var b = placeholder.Rectangle;
                Require(b.X >= a.X && b.Y >= a.Y && b.X + b.Width <= a.X + a.Width && b.Y + b.Height <= a.Y + a.Height,
                    "Placeholder must fit within the printable area.");
            }
        }

        var outputs = Folders.Select(p => (Path: p, Folder: true)).Concat(
            Models.Select(m => m.Path).Concat(Sheets.Select(s => s.Path)).Concat(SupportRecords.Select(s => s.Path))
                .Append(SheetSetPath).Select(p => (Path: p, Folder: false))).ToArray();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var output in outputs)
            if (!paths.Add(output.Path))
                throw new ManifestException(ProposalIssueCodes.OutputCollision, "Output paths collide on Windows.", output.Path);
        foreach (var file in outputs.Where(o => !o.Folder))
            if (outputs.Any(o => o.Path.StartsWith(file.Path + "/", StringComparison.OrdinalIgnoreCase)))
                throw new ManifestException(ProposalIssueCodes.OutputCollision, "A file cannot be an output directory.", file.Path);
        var folders = Folders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var output in outputs)
        {
            string parent = WindowsPaths.Parent(output.Path);
            Require(parent.Length == 0 || folders.Contains(parent), "Every output parent folder must be declared.", output.Path);
        }
        Require(folders.Contains(DataShortcutPath), "Data shortcut folder must be declared.");

        var expected = ModelRoles.Take(2).Select(r => (HostRole: "ProposedDesignModel", ReferenceRole: r))
            .Concat(SheetRoles.SelectMany(s => ModelRoles.Select(m => (HostRole: s, ReferenceRole: m)))).ToHashSet();
        // The approved closed graph is acyclic; exact equality rejects cycles, omissions and extra edges.
        if (Xrefs.Length != expected.Count || !expected.SetEquals(Xrefs.Select(x => (x.HostRole, x.ReferenceRole))) ||
            Xrefs.Any(x => x.Mode != "Overlay" || x.Scale <= 0))
            throw new ManifestException(ProposalIssueCodes.InvalidReferences, "Declare exactly the approved direct overlay reference graph with positive scales.");
    }

    private static SheetProfile Profile(JsonElement item)
    {
        Object(item, "orientation", "size", "template", "layout", "titleBlock", "pageSetup", "plot", "printableArea", "placeholders");
        string template = Absolute(Text(item, "template"));
        Extension(template, ".dwt");
        var plot = item.GetProperty("plot");
        Object(plot, "device", "media", "styleSheet", "units", "scale", "rotation");
        var settings = new PlotStandard(Text(plot, "device"), Text(plot, "media"), Text(plot, "styleSheet"), Text(plot, "units"), Number(plot, "scale"), Integer(plot, "rotation"));
        Require(settings.Scale > 0 && settings.Rotation is 0 or 90 or 180 or 270 && settings.Units is "Millimeters" or "Inches", "Invalid plot units, scale, or rotation.");
        var placeholders = Items(item, "placeholders", p =>
        {
            Object(p, "sheetRole", "label", "kind", "rectangle");
            return new PlaceholderStandard(Text(p, "sheetRole"), Text(p, "label"), Text(p, "kind"), Rect(p.GetProperty("rectangle")));
        }).OrderBy(p => p.SheetRole, StringComparer.Ordinal).ThenBy(p => p.Label, StringComparer.Ordinal).ToImmutableArray();
        return new(Text(item, "orientation"), Text(item, "size"), template, Text(item, "layout"), Text(item, "titleBlock"), Text(item, "pageSetup"), settings, Rect(item.GetProperty("printableArea")), placeholders);
    }

    private static Rectangle Rect(JsonElement item)
    {
        Object(item, "x", "y", "width", "height");
        var rectangle = new Rectangle(Number(item, "x"), Number(item, "y"), Number(item, "width"), Number(item, "height"));
        Require(rectangle.Width > 0 && rectangle.Height > 0 && double.IsFinite(rectangle.X + rectangle.Width) && double.IsFinite(rectangle.Y + rectangle.Height), "Rectangle must have finite positive dimensions.");
        return rectangle;
    }

    private static void CheckDuplicates(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in item.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new ManifestException(ProposalIssueCodes.DuplicateProperty, "Duplicate JSON property.", property.Name);
                CheckDuplicates(property.Value);
            }
        }
        else if (item.ValueKind == JsonValueKind.Array)
            foreach (var child in item.EnumerateArray()) CheckDuplicates(child);
    }

    private static void Object(JsonElement item, params string[] fields)
    {
        Require(item.ValueKind == JsonValueKind.Object, "Expected an object.");
        var names = item.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Require(names.SetEquals(fields), "Object contains missing or unknown fields.", string.Join(",", names.Except(fields).Concat(fields.Except(names))));
    }
    private static ImmutableArray<T> Items<T>(JsonElement item, string name, Func<JsonElement, T> parse) =>
        item.GetProperty(name).EnumerateArray().Select(parse).ToImmutableArray();
    private static string Text(JsonElement item, string name) => Text(item.GetProperty(name));
    private static string Text(JsonElement item)
    {
        string? value = item.GetString();
        Require(TextValues.IsValid(value), "Text must be well-formed, nonblank, with no surrounding whitespace or control characters.");
        return value!;
    }
    private static int Integer(JsonElement item, string name) => item.GetProperty(name).GetInt32();
    private static double Number(JsonElement item, string name)
    {
        double value = item.GetProperty(name).GetDouble();
        Require(double.IsFinite(value), "Numbers must be finite.", name);
        return value;
    }
    private static string Artifact(JsonElement item, string extension)
    {
        string path = Relative(item.GetProperty("path").GetString() ?? "");
        Extension(path, extension);
        return path;
    }
    private static string Relative(string path)
    {
        if (!WindowsPaths.IsRelative(path)) throw new ManifestException(ProposalIssueCodes.UnsafePath, "Expected a safe relative Windows path.", path);
        return path.Replace('\\', '/');
    }
    private static string Absolute(string path)
    {
        if (!WindowsPaths.IsAbsolute(path)) throw new ManifestException(ProposalIssueCodes.UnsafePath, "Expected an absolute Windows drive or UNC path.", path);
        return path.Replace('\\', '/');
    }
    private static void Extension(string path, string extension) => Require(path.EndsWith(extension, StringComparison.OrdinalIgnoreCase), "Unexpected artifact or template extension.", path);
    private static void ExactRoles(IEnumerable<string> actual, IEnumerable<string> expected)
    {
        var values = actual.ToArray();
        Require(values.Length == expected.Count() && values.ToHashSet(StringComparer.Ordinal).SetEquals(expected), "Roles or input mappings are missing, duplicated, or unknown.");
    }
    private static void Unique(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Require(values.All(seen.Add), "Values must be unique, ignoring Windows casing.");
    }
    private static void Require(bool condition, string message, string? location = null)
    {
        if (!condition) throw Invalid(message, location);
    }
    private static ManifestException Invalid(string message, string? location = null) => new(ProposalIssueCodes.InvalidManifest, message, location);
    private sealed class ManifestException(string code, string message, string? location = null) : Exception(message)
    {
        public string Code { get; } = code;
        public string? Location { get; } = location;
    }
}

internal static class PlanJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>Lexical Windows rules, independent of the host OS and filesystem.</summary>
internal static class WindowsPaths
{
    internal static bool IsComponent(string? value)
    {
        if (!TextValues.IsValid(value) || value!.Length > 255 || value.EndsWith('.') ||
            value.Any(c => "<>:\"/\\|?*".Contains(c))) return false;
        string stem = value.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") &&
            !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3]));
    }
    internal static bool IsRelative(string path) => path.Length < 32767 && path.Replace('\\', '/').Split('/').All(IsComponent);
    internal static bool IsAbsolute(string path)
    {
        string p = path.Replace('\\', '/');
        if (p.Length >= 32767) return false;
        if (p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] == '/')
            return p.Length == 3 || IsRelative(p[3..]);
        return p.StartsWith("//", StringComparison.Ordinal) && p[2..].Split('/').Length >= 2 && IsRelative(p[2..]);
    }
    internal static string Parent(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
}

internal static class TextValues
{
    internal static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim()) return false;
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsControl(value[i])) return false;
            if (!char.IsSurrogate(value[i])) continue;
            if (!char.IsSurrogatePair(value, i)) return false;
            i++;
        }
        return true;
    }
}
