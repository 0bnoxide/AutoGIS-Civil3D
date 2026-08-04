using System.Buffers.Binary;
using System.Text;
using AutoGIS.Civil3D.Handoff.Validation;
using ICSharpCode.SharpZipLib;
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
    private const int MsdosHostSystem = 0;
    private const int DosNonRegularAttributeMask = 0x0458;
    private const uint LocalFileHeaderSignature = 0x04034b50;
    private const uint CentralDirectoryHeaderSignature = 0x02014b50;
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
    private const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064b50;
    private const int LocalFileHeaderLength = 30;
    private const int EndOfCentralDirectoryLength = 22;
    private const int MaximumZipCommentLength = ushort.MaxValue;
    private const int Zip64EndOfCentralDirectoryLocatorLength = 20;
    private const int MinimumZip64EndOfCentralDirectoryLength = 56;
    private const ushort DataDescriptorFlag = 0x0008;

    private readonly ZipFile zipFile;
    private readonly FileStream archiveStream;
    private readonly ZipEntry manifestEntry;
    private readonly ZipEntry surfaceEntry;
    private bool disposed;

    private BundleArchive(
        ZipFile zipFile,
        FileStream archiveStream,
        ZipEntry manifestEntry,
        ZipEntry surfaceEntry)
    {
        this.zipFile = zipFile;
        this.archiveStream = archiveStream;
        this.manifestEntry = manifestEntry;
        this.surfaceEntry = surfaceEntry;
    }

    internal static BundleOpenResult Open(string path)
    {
        FileStream? archiveStream = null;
        ZipFile? zipFile = null;
        try
        {
            archiveStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            StringCodec stringCodec = StringCodec.Default;
            zipFile = new ZipFile(archiveStream, leaveOpen: true, stringCodec);
            List<ZipEntry> entries = [];
            foreach (ZipEntry entry in zipFile)
            {
                entries.Add(entry);
            }

            PreflightLocalHeaders(archiveStream, stringCodec, entries);

            ValidationIssue? issue = ValidateEntries(entries);
            if (issue is not null)
            {
                zipFile.Close();
                archiveStream.Dispose();
                return Invalid(issue);
            }

            ZipEntry manifestEntry = FindEntry(entries, ManifestEntryName);
            ZipEntry surfaceEntry = FindEntry(entries, SurfaceEntryName);
            BundleArchive archive = new(zipFile, archiveStream, manifestEntry, surfaceEntry);
            zipFile = null;
            archiveStream = null;
            return new BundleOpenResult(archive, Array.Empty<ValidationIssue>());
        }
        catch (ZipException)
        {
            zipFile?.Close();
            archiveStream?.Dispose();
            return Invalid(IssueCodes.InvalidArchive, "The ZIP archive cannot be read.");
        }
        catch
        {
            zipFile?.Close();
            archiveStream?.Dispose();
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

        try
        {
            zipFile.Close();
        }
        finally
        {
            archiveStream.Dispose();
            disposed = true;
        }
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

    private static void PreflightLocalHeaders(
        FileStream archiveStream,
        StringCodec stringCodec,
        IEnumerable<ZipEntry> entries)
    {
        List<LocalEntryLayout> layouts = [];
        foreach (ZipEntry entry in entries)
        {
            layouts.Add(ValidateLocalHeader(archiveStream, stringCodec, entry));
        }

        ValidateCompressedSpans(archiveStream, layouts);
    }

    private static LocalEntryLayout ValidateLocalHeader(
        FileStream archiveStream,
        StringCodec stringCodec,
        ZipEntry entry)
    {
        if (entry.Offset < 0 || entry.Offset > archiveStream.Length - LocalFileHeaderLength)
        {
            throw new ZipException("The ZIP entry has an invalid local-header offset.");
        }

        archiveStream.Position = entry.Offset;
        Span<byte> header = stackalloc byte[LocalFileHeaderLength];
        ReadExactly(archiveStream, header);

        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != LocalFileHeaderSignature)
        {
            throw new ZipException("The ZIP entry local-header signature is invalid.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
        ushort compressionMethod = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
        if (version != entry.Version
            || flags != (ushort)entry.Flags
            || compressionMethod != (ushort)entry.CompressionMethod)
        {
            throw new ZipException("The ZIP entry local header does not match the central directory.");
        }

        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[26..]);
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
        if (archiveStream.Position > archiveStream.Length - nameLength - extraLength)
        {
            throw new ZipException("The ZIP entry local-header name is invalid.");
        }

        byte[] localName = new byte[nameLength];
        ReadExactly(archiveStream, localName);
        string decodedLocalName = stringCodec.ZipInputEncoding(entry.Flags).GetString(localName);
        if (!string.Equals(decodedLocalName, entry.Name, StringComparison.Ordinal))
        {
            throw new ZipException("The ZIP entry local-header name does not match the central directory.");
        }

        if (extraLength > archiveStream.Length - archiveStream.Position)
        {
            throw new ZipException("The ZIP entry local extra field is truncated.");
        }

        archiveStream.Position += extraLength;

        if ((flags & DataDescriptorFlag) != 0)
        {
            return new LocalEntryLayout(entry, entry.Offset, archiveStream.Position, HasDataDescriptor: true);
        }

        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(header[14..]);
        uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[18..]);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(header[22..]);
        if (entry.Crc is < 0 or > uint.MaxValue
            || crc != (uint)entry.Crc
            || !MatchesLocalSize(compressedSize, entry.CompressedSize)
            || !MatchesLocalSize(size, entry.Size))
        {
            throw new ZipException("The ZIP entry local-header size does not match the central directory.");
        }

        return new LocalEntryLayout(entry, entry.Offset, archiveStream.Position, HasDataDescriptor: false);
    }

    private static void ValidateCompressedSpans(
        FileStream archiveStream,
        IReadOnlyList<LocalEntryLayout> layouts)
    {
        long centralDirectoryStart = FindCentralDirectoryStart(archiveStream);
        LocalEntryLayout[] orderedLayouts = layouts
            .OrderBy(layout => layout.HeaderOffset)
            .ToArray();

        for (int index = 0; index < orderedLayouts.Length; index++)
        {
            LocalEntryLayout layout = orderedLayouts[index];
            long boundary = index + 1 < orderedLayouts.Length
                ? orderedLayouts[index + 1].HeaderOffset
                : centralDirectoryStart;
            if (boundary <= layout.DataOffset)
            {
                throw new ZipException("The ZIP entry has an invalid physical data span.");
            }

            long actualCompressedSpan = layout.HasDataDescriptor
                ? ValidateDataDescriptor(archiveStream, layout, boundary)
                : boundary - layout.DataOffset;
            if (actualCompressedSpan != layout.Entry.CompressedSize)
            {
                throw new ZipException("The ZIP entry compressed size does not match its physical data span.");
            }
        }
    }

    private static long ValidateDataDescriptor(
        FileStream archiveStream,
        LocalEntryLayout layout,
        long boundary)
    {
        bool isZip64 = layout.Entry.LocalHeaderRequiresZip64;
        int[] descriptorLengths = isZip64 ? [24, 20] : [16, 12];
        foreach (int descriptorLength in descriptorLengths)
        {
            long descriptorOffset = boundary - descriptorLength;
            long compressedSpan = descriptorOffset - layout.DataOffset;
            if (compressedSpan < 0)
            {
                continue;
            }

            byte[] descriptor = new byte[descriptorLength];
            archiveStream.Position = descriptorOffset;
            ReadExactly(archiveStream, descriptor);

            int valueOffset = descriptorLength is 16 or 24 ? 4 : 0;
            if (valueOffset != 0 &&
                BinaryPrimitives.ReadUInt32LittleEndian(descriptor) != 0x08074b50)
            {
                continue;
            }

            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(valueOffset));
            valueOffset += sizeof(uint);
            ulong compressedSize = isZip64
                ? BinaryPrimitives.ReadUInt64LittleEndian(descriptor.AsSpan(valueOffset))
                : BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(valueOffset));
            valueOffset += isZip64 ? sizeof(ulong) : sizeof(uint);
            ulong size = isZip64
                ? BinaryPrimitives.ReadUInt64LittleEndian(descriptor.AsSpan(valueOffset))
                : BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(valueOffset));

            if (layout.Entry.Crc is >= 0 and <= uint.MaxValue &&
                crc == (uint)layout.Entry.Crc &&
                compressedSize == (ulong)layout.Entry.CompressedSize &&
                size == (ulong)layout.Entry.Size &&
                compressedSize == (ulong)compressedSpan)
            {
                return compressedSpan;
            }
        }

        throw new ZipException("The ZIP entry data descriptor does not match its physical data span.");
    }

    private static long FindCentralDirectoryStart(FileStream archiveStream)
    {
        int tailLength = checked((int)Math.Min(
            archiveStream.Length,
            EndOfCentralDirectoryLength + (long)MaximumZipCommentLength));
        if (tailLength < EndOfCentralDirectoryLength)
        {
            throw new ZipException("The ZIP end-of-central-directory record is missing.");
        }

        byte[] tail = new byte[tailLength];
        long tailOffset = archiveStream.Length - tailLength;
        archiveStream.Position = tailOffset;
        ReadExactly(archiveStream, tail);

        for (int index = tail.Length - EndOfCentralDirectoryLength; index >= 0; index--)
        {
            ReadOnlySpan<byte> candidate = tail.AsSpan(index);
            if (BinaryPrimitives.ReadUInt32LittleEndian(candidate) != EndOfCentralDirectorySignature)
            {
                continue;
            }

            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(candidate[20..]);
            if (index + EndOfCentralDirectoryLength + commentLength != tail.Length)
            {
                continue;
            }

            uint centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(candidate[16..]);
            long result = centralDirectoryOffset == uint.MaxValue
                ? ReadZip64CentralDirectoryStart(archiveStream, tailOffset + index)
                : centralDirectoryOffset;
            ValidateCentralDirectoryStart(archiveStream, result, tailOffset + index);
            return result;
        }

        throw new ZipException("The ZIP end-of-central-directory record is invalid.");
    }

    private static long ReadZip64CentralDirectoryStart(
        FileStream archiveStream,
        long endOfCentralDirectoryOffset)
    {
        long locatorOffset = endOfCentralDirectoryOffset - Zip64EndOfCentralDirectoryLocatorLength;
        if (locatorOffset < 0)
        {
            throw new ZipException("The ZIP64 end-of-central-directory locator is missing.");
        }

        Span<byte> locator = stackalloc byte[Zip64EndOfCentralDirectoryLocatorLength];
        archiveStream.Position = locatorOffset;
        ReadExactly(archiveStream, locator);
        if (BinaryPrimitives.ReadUInt32LittleEndian(locator) !=
            Zip64EndOfCentralDirectoryLocatorSignature)
        {
            throw new ZipException("The ZIP64 end-of-central-directory locator is invalid.");
        }

        ulong recordOffsetValue = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
        if (recordOffsetValue > long.MaxValue)
        {
            throw new ZipException("The ZIP64 end-of-central-directory offset is invalid.");
        }

        long recordOffset = (long)recordOffsetValue;
        if (recordOffset < 0 || recordOffset > archiveStream.Length - MinimumZip64EndOfCentralDirectoryLength)
        {
            throw new ZipException("The ZIP64 end-of-central-directory record is truncated.");
        }

        Span<byte> record = stackalloc byte[MinimumZip64EndOfCentralDirectoryLength];
        archiveStream.Position = recordOffset;
        ReadExactly(archiveStream, record);
        if (BinaryPrimitives.ReadUInt32LittleEndian(record) != Zip64EndOfCentralDirectorySignature)
        {
            throw new ZipException("The ZIP64 end-of-central-directory record is invalid.");
        }

        ulong recordSize = BinaryPrimitives.ReadUInt64LittleEndian(record[4..]);
        if (recordSize > long.MaxValue - 12 || recordOffset + 12 + (long)recordSize != locatorOffset)
        {
            throw new ZipException("The ZIP64 end-of-central-directory length is invalid.");
        }

        ulong centralDirectoryOffset = BinaryPrimitives.ReadUInt64LittleEndian(record[48..]);
        if (centralDirectoryOffset > long.MaxValue)
        {
            throw new ZipException("The ZIP64 central-directory offset is invalid.");
        }

        return (long)centralDirectoryOffset;
    }

    private static void ValidateCentralDirectoryStart(
        FileStream archiveStream,
        long centralDirectoryStart,
        long endOfCentralDirectoryOffset)
    {
        if (centralDirectoryStart < 0 ||
            centralDirectoryStart > endOfCentralDirectoryOffset - sizeof(uint))
        {
            throw new ZipException("The ZIP central-directory offset is invalid.");
        }

        Span<byte> signature = stackalloc byte[sizeof(uint)];
        archiveStream.Position = centralDirectoryStart;
        ReadExactly(archiveStream, signature);
        if (BinaryPrimitives.ReadUInt32LittleEndian(signature) != CentralDirectoryHeaderSignature)
        {
            throw new ZipException("The ZIP central-directory signature is invalid.");
        }
    }

    private static bool MatchesLocalSize(uint localSize, long centralSize) =>
        centralSize is >= 0 and <= uint.MaxValue
            ? localSize == (uint)centralSize
            : localSize == uint.MaxValue;

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int countRead = stream.Read(buffer[offset..]);
            if (countRead == 0)
            {
                throw new ZipException("The ZIP local header is truncated.");
            }

            offset += countRead;
        }
    }

    private static bool IsSafeEntryName(string? name)
    {
        if (string.IsNullOrEmpty(name)
            || IsRootedZipName(name))
        {
            return false;
        }

        if (name.Contains('\\'))
        {
            return false;
        }

        return !name.Split('/').Any(segment => segment is "." or "..");
    }

    private static bool IsRootedZipName(string name) =>
        name.StartsWith('\\')
        || name.StartsWith('/')
        || (name.Length >= 2 && name[1] == ':' && IsAsciiLetter(name[0]));

    private static bool IsAsciiLetter(char value) =>
        (value is >= 'A' and <= 'Z') || (value is >= 'a' and <= 'z');

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

        if (entry.HostSystem == MsdosHostSystem &&
            (entry.ExternalFileAttributes & DosNonRegularAttributeMask) != 0)
        {
            return false;
        }

        int fileType = (entry.ExternalFileAttributes >> 16) & UnixFileTypeMask;
        if (fileType != 0)
        {
            return fileType == UnixRegularFileType;
        }

        return entry.IsFile;
    }

    private static bool ExceedsCompressionRatio(ZipEntry entry) =>
        entry.Size > BundleLimits.MaximumCompressionRatio * entry.CompressedSize;

    private Stream OpenEntryStream(ZipEntry entry, long limit)
    {
        try
        {
            return new BoundedReadStream(
                zipFile.GetInputStream(entry),
                entry.Name,
                limit,
                entry.Size,
                entry.Crc,
                entry.CompressedSize);
        }
        catch (SharpZipBaseException exception)
        {
            throw new BundleEntryDataException(
                IssueCodes.InvalidArchive,
                $"The ZIP entry data for {entry.Name} is corrupt.",
                exception);
        }
        catch (EndOfStreamException exception)
        {
            throw new BundleEntryDataException(
                IssueCodes.InvalidArchive,
                $"The ZIP entry data for {entry.Name} is truncated.",
                exception);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private sealed record LocalEntryLayout(
        ZipEntry Entry,
        long HeaderOffset,
        long DataOffset,
        bool HasDataDescriptor);
}
