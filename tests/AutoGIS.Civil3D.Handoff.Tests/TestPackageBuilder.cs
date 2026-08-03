using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace AutoGIS.Civil3D.Handoff.Tests;

public enum PackageFault
{
    Valid,
    MissingSurface,
    ExtraEntry,
    UnsafePath,
    WindowsRootedPath,
    CaseCollision,
    DirectoryEntry,
    SymlinkEntry,
    NonUnixHostSymlink,
    EncryptedSurface,
    UnsupportedCompression,
    ManifestTooLarge,
    SurfaceTooLarge,
    CompressionRatioExceeded,
    LocalHeaderFlagsMismatch,
    LocalHeaderCompressionMismatch,
    LocalHeaderNameMismatch,
    LocalHeaderSizeMismatch,
    LocalHeaderMismatchBeforeUnsafeName,
    Malformed
}

internal static class TestPackageBuilder
{
    private const uint CentralDirectoryHeaderSignature = 0x02014b50;
    private const uint LocalFileHeaderSignature = 0x04034b50;
    private const ushort UnixMadeBy = 0x0314;
    private const ushort MsdosMadeBy = 0x0014;
    private const uint RegularFileAttributes = 0x81a40000;
    private const uint DirectoryAttributes = 0x41ed0000;
    private const uint SymbolicLinkAttributes = 0xa1ff0000;
    private const ushort BZip2CompressionMethod = 12;
    private const long ManifestLimit = 1L * 1024 * 1024;
    private const long SurfaceLimit = 2L * 1024 * 1024 * 1024;

    internal static string Create(PackageFault fault)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "AutoGIS.Civil3D.Handoff.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "package.zip");
        if (fault == PackageFault.Malformed)
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("not a ZIP archive"));
            return path;
        }

        List<EntrySpec> entries =
        [
            new("handoff.json", "{}"u8.ToArray()),
            new("surface.landxml", "<LandXML/>"u8.ToArray())
        ];

        switch (fault)
        {
            case PackageFault.Valid:
                break;
            case PackageFault.MissingSurface:
                entries.RemoveAt(1);
                break;
            case PackageFault.ExtraEntry:
                entries.Add(new EntrySpec("unexpected.txt", "unexpected"u8.ToArray()));
                break;
            case PackageFault.UnsafePath:
                entries[1] = new EntrySpec("../surface.landxml", "<LandXML/>"u8.ToArray());
                break;
            case PackageFault.WindowsRootedPath:
                entries[1] = new EntrySpec("C:/surface.landxml", "<LandXML/>"u8.ToArray());
                break;
            case PackageFault.CaseCollision:
                entries.Add(new EntrySpec("HANDOFF.JSON", "{}"u8.ToArray()));
                break;
            case PackageFault.DirectoryEntry:
            case PackageFault.SymlinkEntry:
            case PackageFault.NonUnixHostSymlink:
            case PackageFault.EncryptedSurface:
            case PackageFault.UnsupportedCompression:
            case PackageFault.ManifestTooLarge:
            case PackageFault.SurfaceTooLarge:
            case PackageFault.CompressionRatioExceeded:
            case PackageFault.LocalHeaderFlagsMismatch:
            case PackageFault.LocalHeaderCompressionMismatch:
            case PackageFault.LocalHeaderNameMismatch:
            case PackageFault.LocalHeaderSizeMismatch:
                break;
            case PackageFault.LocalHeaderMismatchBeforeUnsafeName:
                entries[1] = new EntrySpec("../surface.landxml", "<LandXML/>"u8.ToArray());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
        }

        CreateArchive(path, entries);
        ApplyFault(path, fault);
        return path;
    }

    internal static void Delete(string packagePath)
    {
        string directory = Path.GetDirectoryName(packagePath)
            ?? throw new ArgumentException("The package path must have a directory.", nameof(packagePath));
        Directory.Delete(directory, recursive: true);
    }

    private static void CreateArchive(string path, IReadOnlyList<EntrySpec> entries)
    {
        using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(file, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);

        foreach (EntrySpec spec in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(spec.Name, CompressionLevel.NoCompression);
            entry.LastWriteTime = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
            using Stream entryStream = entry.Open();
            entryStream.Write(spec.Contents);
        }

        archive.Dispose();

        byte[] bytes = File.ReadAllBytes(path);
        foreach (EntrySpec spec in entries)
        {
            PatchEntry(bytes, spec.Name, (centralHeaderOffset, _) =>
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bytes.AsSpan(centralHeaderOffset + 4),
                    UnixMadeBy);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bytes.AsSpan(centralHeaderOffset + 38),
                    RegularFileAttributes);
            });
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void ApplyFault(string path, PackageFault fault)
    {
        if (fault is PackageFault.Valid or PackageFault.MissingSurface or PackageFault.ExtraEntry
            or PackageFault.UnsafePath or PackageFault.WindowsRootedPath or PackageFault.CaseCollision)
        {
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        switch (fault)
        {
            case PackageFault.DirectoryEntry:
                PatchEntry(bytes, "handoff.json", (centralHeaderOffset, _) =>
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 38),
                        DirectoryAttributes));
                break;
            case PackageFault.SymlinkEntry:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, _) =>
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 38),
                        SymbolicLinkAttributes));
                break;
            case PackageFault.NonUnixHostSymlink:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, _) =>
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 4),
                        MsdosMadeBy);
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 38),
                        SymbolicLinkAttributes);
                });
                break;
            case PackageFault.EncryptedSurface:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, localHeaderOffset) =>
                {
                    SetEncryptedFlag(bytes, centralHeaderOffset + 8);
                    SetEncryptedFlag(bytes, localHeaderOffset + 6);
                });
                break;
            case PackageFault.UnsupportedCompression:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, localHeaderOffset) =>
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 10),
                        BZip2CompressionMethod);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 8),
                        BZip2CompressionMethod);
                });
                break;
            case PackageFault.ManifestTooLarge:
                PatchDeclaredSize(bytes, "handoff.json", checked((uint)(ManifestLimit + 1)));
                break;
            case PackageFault.SurfaceTooLarge:
                PatchDeclaredSize(bytes, "surface.landxml", checked((uint)(SurfaceLimit + 1)));
                break;
            case PackageFault.CompressionRatioExceeded:
                PatchCompressionRatio(bytes, "surface.landxml");
                break;
            case PackageFault.LocalHeaderFlagsMismatch:
                PatchEntry(bytes, "surface.landxml", (_, localHeaderOffset) =>
                {
                    ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 6));
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 6),
                        (ushort)(flags ^ 0x0001));
                });
                break;
            case PackageFault.LocalHeaderCompressionMismatch:
                PatchEntry(bytes, "surface.landxml", (_, localHeaderOffset) =>
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 8),
                        BZip2CompressionMethod));
                break;
            case PackageFault.LocalHeaderNameMismatch:
                PatchEntry(bytes, "surface.landxml", (_, localHeaderOffset) =>
                {
                    byte[] replacement = "syrface.landxml"u8.ToArray();
                    ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 26));
                    if (replacement.Length != nameLength)
                    {
                        throw new InvalidDataException("The replacement local entry name must preserve its length.");
                    }

                    replacement.CopyTo(bytes, localHeaderOffset + 30);
                });
                break;
            case PackageFault.LocalHeaderSizeMismatch:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, localHeaderOffset) =>
                {
                    ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 8));
                    flags = (ushort)(flags & ~0x0008);
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(centralHeaderOffset + 8), flags);
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(localHeaderOffset + 6), flags);

                    uint crc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(centralHeaderOffset + 16));
                    uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(centralHeaderOffset + 20));
                    uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(centralHeaderOffset + 24));
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(localHeaderOffset + 14), crc);
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(localHeaderOffset + 18), compressedSize);
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 22),
                        checked(uncompressedSize + 1));
                });
                break;
            case PackageFault.LocalHeaderMismatchBeforeUnsafeName:
                PatchEntry(bytes, "../surface.landxml", (_, localHeaderOffset) =>
                {
                    ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 6));
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 6),
                        (ushort)(flags ^ 0x0001));
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void PatchDeclaredSize(byte[] bytes, string entryName, uint uncompressedSize)
    {
        PatchEntry(bytes, entryName, (centralHeaderOffset, localHeaderOffset) =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(centralHeaderOffset + 24),
                uncompressedSize);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(localHeaderOffset + 22),
                uncompressedSize);
        });
    }

    private static void PatchCompressionRatio(byte[] bytes, string entryName)
    {
        PatchEntry(bytes, entryName, (centralHeaderOffset, localHeaderOffset) =>
        {
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(centralHeaderOffset + 20));
            uint uncompressedSize = checked((compressedSize * 100) + 1);

            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(centralHeaderOffset + 24),
                uncompressedSize);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(localHeaderOffset + 22),
                uncompressedSize);
        });
    }

    private static void SetEncryptedFlag(byte[] bytes, int offset)
    {
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), (ushort)(flags | 1));
    }

    private static void PatchEntry(
        byte[] bytes,
        string entryName,
        Action<int, int> patch)
    {
        for (int offset = 0; offset <= bytes.Length - 46; offset++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) != CentralDirectoryHeaderSignature)
            {
                continue;
            }

            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 30));
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 32));
            int headerLength = checked(46 + nameLength + extraLength + commentLength);
            if (offset + headerLength > bytes.Length)
            {
                throw new InvalidDataException("The test ZIP central directory is truncated.");
            }

            string currentName = Encoding.UTF8.GetString(bytes, offset + 46, nameLength);
            if (!string.Equals(currentName, entryName, StringComparison.Ordinal))
            {
                offset += headerLength - 1;
                continue;
            }

            int localHeaderOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(offset + 42)));
            if (localHeaderOffset < 0
                || localHeaderOffset > bytes.Length - 30
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(localHeaderOffset)) != LocalFileHeaderSignature)
            {
                throw new InvalidDataException("The test ZIP local header is invalid.");
            }

            patch(offset, localHeaderOffset);
            return;
        }

        throw new InvalidDataException($"The test ZIP does not contain '{entryName}'.");
    }

    private sealed record EntrySpec(string Name, byte[] Contents);
}
