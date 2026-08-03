using AutoGIS.Civil3D.Handoff.Validation;
using ICSharpCode.SharpZipLib.Zip;

namespace AutoGIS.Civil3D.Handoff.Packaging;

internal sealed record BundleOpenResult(
    BundleArchive? Archive,
    IReadOnlyList<ValidationIssue> Issues);

internal sealed class BundleArchive : IDisposable
{
    private const string ManifestEntryName = "handoff.json";
    private const string SurfaceEntryName = "surface.landxml";
    private const int UnixFileTypeMask = 0xf000;
    private const int UnixRegularFileType = 0x8000;

    private readonly ZipFile zipFile;
    private readonly ZipEntry manifestEntry;
    private readonly ZipEntry surfaceEntry;
    private bool disposed;

    private BundleArchive(ZipFile zipFile, ZipEntry manifestEntry, ZipEntry surfaceEntry)
    {
        this.zipFile = zipFile;
        this.manifestEntry = manifestEntry;
        this.surfaceEntry = surfaceEntry;
    }

    internal static BundleOpenResult Open(string path)
    {
        ZipFile? zipFile = null;
        try
        {
            zipFile = new ZipFile(path);
            List<ZipEntry> entries = [];
            foreach (ZipEntry entry in zipFile)
            {
                entries.Add(entry);
            }

            ValidationIssue? issue = ValidateEntries(entries);
            if (issue is not null)
            {
                zipFile.Close();
                return Invalid(issue);
            }

            ZipEntry manifestEntry = FindEntry(entries, ManifestEntryName);
            ZipEntry surfaceEntry = FindEntry(entries, SurfaceEntryName);
            BundleArchive archive = new(zipFile, manifestEntry, surfaceEntry);
            zipFile = null;
            return new BundleOpenResult(archive, Array.Empty<ValidationIssue>());
        }
        catch (ZipException)
        {
            zipFile?.Close();
            return Invalid(IssueCodes.InvalidArchive, "The ZIP archive cannot be read.");
        }
        catch
        {
            zipFile?.Close();
            throw;
        }
    }

    internal byte[] ReadManifestBytes()
    {
        ThrowIfDisposed();
        using Stream manifestStream = OpenEntryStream(manifestEntry, BundleLimits.ManifestBytes);
        using MemoryStream buffer = new();
        manifestStream.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal Stream OpenSurfaceStream()
    {
        ThrowIfDisposed();
        return OpenEntryStream(surfaceEntry, BundleLimits.SurfaceBytes);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        zipFile.Close();
        disposed = true;
    }

    private static BundleOpenResult Invalid(ValidationIssue issue) =>
        new(null, [issue]);

    private static BundleOpenResult Invalid(string code, string message) =>
        Invalid(new ValidationIssue(code, IssueSeverity.Error, message));

    private static ValidationIssue? ValidateEntries(IReadOnlyList<ZipEntry> entries)
    {
        foreach (ZipEntry entry in entries)
        {
            if (!IsSafeEntryName(entry.Name))
            {
                return Error(IssueCodes.UnsafeEntryName, "The ZIP contains an unsafe entry name.");
            }
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (ZipEntry entry in entries)
        {
            if (!names.Add(entry.Name))
            {
                return Error(IssueCodes.DuplicateEntryName, "The ZIP contains duplicate entry names.");
            }
        }

        foreach (ZipEntry entry in entries)
        {
            if (!IsExpectedEntryName(entry.Name))
            {
                return Error(IssueCodes.UnexpectedEntry, "The ZIP contains an unexpected entry.");
            }
        }

        if (!ContainsEntry(entries, ManifestEntryName) || !ContainsEntry(entries, SurfaceEntryName))
        {
            return Error(IssueCodes.MissingRequiredEntry, "The ZIP is missing a required entry.");
        }

        if (entries.Count != BundleLimits.EntryCount)
        {
            return Error(IssueCodes.EntryCountMismatch, "The ZIP does not contain exactly two entries.");
        }

        foreach (ZipEntry entry in entries)
        {
            if (!IsRegularFile(entry))
            {
                return Error(IssueCodes.NonRegularEntry, "The ZIP contains a non-regular entry.");
            }
        }

        foreach (ZipEntry entry in entries)
        {
            if (entry.IsCrypted)
            {
                return Error(IssueCodes.EncryptedEntry, "The ZIP contains an encrypted entry.");
            }
        }

        foreach (ZipEntry entry in entries)
        {
            if (entry.CompressionMethod is not (CompressionMethod.Stored or CompressionMethod.Deflated))
            {
                return Error(IssueCodes.UnsupportedCompression, "The ZIP uses an unsupported compression method.");
            }
        }

        foreach (ZipEntry entry in entries)
        {
            if (entry.Size < 0 || entry.CompressedSize < 0)
            {
                return Error(IssueCodes.InvalidArchive, "The ZIP contains an entry with an invalid declared size.");
            }
        }

        ZipEntry manifestEntry = FindEntry(entries, ManifestEntryName);
        if (manifestEntry.Size > BundleLimits.ManifestBytes)
        {
            return Error(IssueCodes.ManifestTooLarge, "The manifest exceeds the declared size limit.");
        }

        ZipEntry surfaceEntry = FindEntry(entries, SurfaceEntryName);
        if (surfaceEntry.Size > BundleLimits.SurfaceBytes)
        {
            return Error(IssueCodes.SurfaceTooLarge, "The surface exceeds the declared size limit.");
        }

        if (ExceedsCompressionRatio(manifestEntry) || ExceedsCompressionRatio(surfaceEntry))
        {
            return Error(IssueCodes.CompressionRatioExceeded, "The ZIP contains an entry with an excessive compression ratio.");
        }

        return null;
    }

    private static ValidationIssue Error(string code, string message) =>
        new(code, IssueSeverity.Error, message);

    private static bool IsSafeEntryName(string? name)
    {
        if (string.IsNullOrEmpty(name)
            || Path.IsPathRooted(name)
            || name.StartsWith('\\')
            || name.StartsWith('/'))
        {
            return false;
        }

        if (name.Contains('\\'))
        {
            return false;
        }

        return !name.Split('/').Any(segment => segment is "." or "..");
    }

    private static bool IsExpectedEntryName(string name) =>
        string.Equals(name, ManifestEntryName, StringComparison.Ordinal)
        || string.Equals(name, SurfaceEntryName, StringComparison.Ordinal);

    private static bool ContainsEntry(IEnumerable<ZipEntry> entries, string expectedName) =>
        entries.Any(entry => string.Equals(entry.Name, expectedName, StringComparison.Ordinal));

    private static ZipEntry FindEntry(IEnumerable<ZipEntry> entries, string expectedName) =>
        entries.Single(entry => string.Equals(entry.Name, expectedName, StringComparison.Ordinal));

    private static bool IsRegularFile(ZipEntry entry)
    {
        if (entry.IsDirectory)
        {
            return false;
        }

        if (entry.HostSystem == (int)HostSystemID.Unix)
        {
            int fileType = (entry.ExternalFileAttributes >> 16) & UnixFileTypeMask;
            return fileType == UnixRegularFileType;
        }

        return entry.IsFile;
    }

    private static bool ExceedsCompressionRatio(ZipEntry entry) =>
        entry.Size > BundleLimits.MaximumCompressionRatio * entry.CompressedSize;

    private Stream OpenEntryStream(ZipEntry entry, long limit) =>
        new BoundedReadStream(zipFile.GetInputStream(entry), entry.Name, limit);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
