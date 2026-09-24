using System.ComponentModel;
using System.Text.Json;
using AutoGIS.Civil3D.Handoff;
using AutoGIS.Civil3D.Handoff.Validation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AutoGIS.Civil3D.Mcp;

[McpServerToolType]
public sealed class HandoffTools
{
    private const int MaxOutputBytes = 64 * 1024;
    private const int MaxIssues = 128;
    private const string FileUnreadable = "FILE_UNREADABLE";
    private readonly string? _configuredRoot;
    private readonly Func<string, ValidationReport> _validate;
    private int _busy;

    public HandoffTools(string? configuredRoot)
        : this(configuredRoot, new BundleValidator().ValidateBundle)
    {
    }

    internal HandoffTools(string? configuredRoot, Func<string, ValidationReport> validate)
    {
        _configuredRoot = configuredRoot;
        _validate = validate;
    }

    [McpServerTool(Name = "validate_handoff_bundle", ReadOnly = true,
        UseStructuredContent = true, OutputSchemaType = typeof(ValidationOutput))]
    [Description("Validate one handoff ZIP beneath the configured local bundle root without modifying it.")]
    public async Task<CallToolResult> ValidateHandoffBundle(
        [Description("ZIP path relative to AUTOGIS_MCP_BUNDLE_ROOT.")] string bundle_relative_path,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        if (context.Params.Arguments is not { Count: 1 } arguments ||
            !arguments.ContainsKey("bundle_relative_path") ||
            string.IsNullOrWhiteSpace(bundle_relative_path))
        {
            return Error("INVALID_ARGUMENTS");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Error("CANCELLED");
        }

        string? root = ResolveRoot(out string rootError);
        if (root is null)
        {
            return Error(rootError);
        }

        string? path = ResolveBundlePath(root, bundle_relative_path, out string pathError);
        if (path is null)
        {
            return Error(pathError);
        }

        return await RunValidationAsync(path, cancellationToken);
    }

    internal async Task<CallToolResult> RunValidationAsync(string path, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return Error("BUSY");
        }

        // ponytail: one process-wide read; add a bounded queue only if concurrent validation becomes necessary.
        Task<ValidationReport> work = Task.Run(() =>
        {
            try
            {
                return _validate(path);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }, CancellationToken.None);

        try
        {
            return FormatReport(await work.WaitAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // The synchronous validator keeps reading until it finishes; _busy stays set meanwhile.
            return Error("CANCELLED");
        }
        catch (UnauthorizedAccessException)
        {
            return Error(FileUnreadable);
        }
        catch (IOException)
        {
            return Error(FileUnreadable);
        }
        catch (Exception)
        {
            return Error("VALIDATION_FAILED");
        }
    }

    private string? ResolveRoot(out string error)
    {
        error = "ROOT_NOT_CONFIGURED";
        if (string.IsNullOrWhiteSpace(_configuredRoot) ||
            !Path.IsPathFullyQualified(_configuredRoot) || IsUncOrDevice(_configuredRoot))
        {
            return null;
        }

        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_configuredRoot));
            if (string.Equals(root, Path.GetPathRoot(root), PathComparison) ||
                !Directory.Exists(root))
            {
                return null;
            }

            for (DirectoryInfo? directory = new(root); directory is not null; directory = directory.Parent)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    error = "PATH_NOT_ALLOWED";
                    return null;
                }
            }

            return root;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ResolveBundlePath(string root, string relative, out string error)
    {
        error = "PATH_NOT_ALLOWED";
        if (relative.Length > 1024 || Path.IsPathRooted(relative) ||
            IsUncOrDevice(relative) || relative.Contains(':') ||
            relative.Any(c => char.IsControl(c) || "<>\"|?*".Contains(c)))
        {
            return null;
        }

        string[] segments = relative.Replace('\\', '/').Split('/');
        if (segments.Any(IsUnsafeSegment) ||
            !string.Equals(Path.GetExtension(segments[^1]), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            string path = Path.GetFullPath(Path.Combine(root,
                string.Join(Path.DirectorySeparatorChar, segments)));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
            {
                return null;
            }

            string current = root;
            for (int i = 0; i < segments.Length; i++)
            {
                current = Path.Combine(current, segments[i]);
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return null;
                }

                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                if (isDirectory != (i < segments.Length - 1))
                {
                    error = FileUnreadable;
                    return null;
                }
            }

            return path;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            error = "FILE_NOT_FOUND";
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            error = FileUnreadable;
            return null;
        }
    }

    private static bool IsUncOrDevice(string path) =>
        path.StartsWith("\\\\", StringComparison.Ordinal) ||
        path.StartsWith("//", StringComparison.Ordinal);

    private static bool IsUnsafeSegment(string segment) =>
        segment.Length == 0 || segment is "." or ".." ||
        segment.EndsWith(' ') || segment.EndsWith('.') || IsDeviceName(segment);

    private static bool IsDeviceName(string segment)
    {
        string stem = segment.Split('.')[0].TrimEnd(' ');
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4 &&
            (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
             stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            (stem[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3');
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static CallToolResult FormatReport(ValidationReport report)
    {
        List<ValidationIssueOutput> issues = report.Issues.Take(MaxIssues)
            .Select(issue => new ValidationIssueOutput(
                SafeCode(issue.Code), issue.Severity.ToString(),
                "Validation issue; see code.", SafeLabel(issue.Location)))
            .ToList();
        ValidationMetadataOutput? metadata = report.Metadata is { } verified
            ? new ValidationMetadataOutput(verified.PackageId, SafeLabel(verified.SurfaceName) ?? "[redacted]",
                verified.PointCount, verified.FaceCount, verified.EpsgCode)
            : null;

        ValidationOutput output;
        JsonElement structured;
        do
        {
            output = new ValidationOutput(report.Status.ToString(), report.Issues.Count,
                issues.Count < report.Issues.Count, issues, metadata);
            structured = JsonSerializer.SerializeToElement(output, OutputJsonOptions);
            if (JsonSerializer.SerializeToUtf8Bytes(structured).Length <= MaxOutputBytes)
            {
                break;
            }

            if (issues.Count == 0)
            {
                return Error("VALIDATION_FAILED");
            }

            issues.RemoveAt(issues.Count - 1);
        } while (true);

        return new CallToolResult { Content = [], StructuredContent = structured };
    }

    private static string SafeCode(string code) =>
        code.Length is > 0 and <= 32 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? code : "VALIDATION_ISSUE";

    private static string? SafeLabel(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(c => !char.IsControl(c) && c is not ('/' or '\\' or ':'))
            ? value : null;

    private static readonly JsonSerializerOptions OutputJsonOptions = new(JsonSerializerDefaults.Web);

    private static CallToolResult Error(string code) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = code }] };
}

public sealed record ValidationOutput(
    string Status,
    int IssueCount,
    bool Truncated,
    IReadOnlyList<ValidationIssueOutput> Issues,
    ValidationMetadataOutput? Metadata);

public sealed record ValidationIssueOutput(string Code, string Severity, string Message, string? Location);

public sealed record ValidationMetadataOutput(
    Guid PackageId,
    string SurfaceName,
    long PointCount,
    long FaceCount,
    int EpsgCode);
