using System.Buffers.Binary;
using System.Text;

namespace AutoGIS.Civil3D.FixtureBuilder;

internal static class ZipHeaderMutator
{
    private const uint CentralDirectoryHeaderSignature = 0x02014B50;
    private const uint LocalFileHeaderSignature = 0x04034B50;
    private const ushort MsdosMadeBy = 0x0014;

    internal static void SetEncrypted(byte[] archive, string entryName) =>
        MutateEntry(archive, entryName, (centralOffset, localOffset) =>
        {
            SetFlag(archive, centralOffset + 8, 0x0001);
            SetFlag(archive, localOffset + 6, 0x0001);
        });

    internal static void SetCompressionMethod(byte[] archive, string entryName, ushort method) =>
        MutateEntry(archive, entryName, (centralOffset, localOffset) =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(centralOffset + 10), method);
            BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(localOffset + 8), method);
        });

    internal static void SetUncompressedSize(byte[] archive, string entryName, uint size) =>
        MutateEntry(archive, entryName, (centralOffset, localOffset) =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(centralOffset + 24), size);
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(localOffset + 22), size);
        });

    internal static void SetUnixMode(byte[] archive, string entryName, ushort mode) =>
        MutateEntry(archive, entryName, (centralOffset, _) =>
            BinaryPrimitives.WriteUInt32LittleEndian(
                archive.AsSpan(centralOffset + 38),
                (uint)mode << 16));

    internal static void SetDosAttributes(byte[] archive, string entryName, ushort attributes) =>
        MutateEntry(archive, entryName, (centralOffset, _) =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                archive.AsSpan(centralOffset + 4),
                MsdosMadeBy);
            BinaryPrimitives.WriteUInt32LittleEndian(
                archive.AsSpan(centralOffset + 38),
                attributes);
        });

    private static void SetFlag(byte[] archive, int offset, ushort flag)
    {
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(offset));
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(offset), (ushort)(flags | flag));
    }

    private static void MutateEntry(
        byte[] archive,
        string entryName,
        Action<int, int> mutation)
    {
        int offset = 0;
        while (offset <= archive.Length - 46)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(offset))
                != CentralDirectoryHeaderSignature)
            {
                offset++;
                continue;
            }

            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(offset + 28));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(offset + 30));
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(offset + 32));
            int recordLength = checked(46 + nameLength + extraLength + commentLength);
            if (offset + recordLength > archive.Length)
            {
                throw new InvalidDataException("The ZIP central directory is truncated.");
            }

            string currentName = Encoding.UTF8.GetString(archive, offset + 46, nameLength);
            if (!string.Equals(currentName, entryName, StringComparison.Ordinal))
            {
                offset += recordLength;
                continue;
            }

            uint localOffsetValue = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(offset + 42));
            if (localOffsetValue > int.MaxValue)
            {
                throw new InvalidDataException("The ZIP local header offset is unsupported.");
            }

            int localOffset = (int)localOffsetValue;
            if (localOffset > archive.Length - 30
                || BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(localOffset))
                    != LocalFileHeaderSignature)
            {
                throw new InvalidDataException("The ZIP local header is invalid.");
            }

            mutation(offset, localOffset);
            return;
        }

        throw new InvalidDataException($"The ZIP does not contain '{entryName}'.");
    }
}
