namespace AutoGIS.Civil3D.Handoff.LandXml;

internal sealed class ForbiddenSequenceException : IOException
{
    internal ForbiddenSequenceException()
        : base("The XML stream contains a forbidden document type declaration.")
    {
    }
}

internal sealed class ForbiddenSequenceReadStream : Stream
{
    private static ReadOnlySpan<byte> ForbiddenSequence => "<!DOCTYPE"u8;

    private readonly Stream source;
    private readonly byte[] sourceBuffer = new byte[4096];
    private readonly byte[] candidate = new byte[ForbiddenSequence.Length];
    private readonly Queue<byte> safeBytes = new();
    private int matchedByteCount;
    private bool forbiddenSequenceFound;
    private bool sourceEnded;

    internal ForbiddenSequenceReadStream(Stream source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        if (!source.CanRead)
        {
            throw new ArgumentException("The source stream must be readable.", nameof(source));
        }
    }

    public override bool CanRead => source.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    internal bool ForbiddenSequenceFound => forbiddenSequenceFound;

    public override long Length => source.Length;

    public override long Position
    {
        get => throw new NotSupportedException("The sequence-scanning stream is forward-only.");
        set => throw new NotSupportedException("The sequence-scanning stream is forward-only.");
    }

    public override void Flush() => throw new NotSupportedException("The sequence-scanning stream is read-only.");

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadCore(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer) => ReadCore(buffer);

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("The sequence-scanning stream is forward-only.");

    public override void SetLength(long value) =>
        throw new NotSupportedException("The sequence-scanning stream is read-only.");

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("The sequence-scanning stream is read-only.");

    private int ReadCore(Span<byte> buffer)
    {
        ThrowIfForbiddenSequenceFound();
        if (buffer.Length == 0)
        {
            return 0;
        }

        while (safeBytes.Count == 0 && !sourceEnded)
        {
            int countRead = source.Read(sourceBuffer);
            if (countRead == 0)
            {
                sourceEnded = true;
                ReleaseCandidate();
                break;
            }

            Scan(sourceBuffer.AsSpan(0, countRead));
        }

        int countToReturn = Math.Min(buffer.Length, safeBytes.Count);
        for (int index = 0; index < countToReturn; index++)
        {
            buffer[index] = safeBytes.Dequeue();
        }

        return countToReturn;
    }

    private void Scan(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> forbiddenSequence = ForbiddenSequence;
        foreach (byte value in bytes)
        {
            if (value == forbiddenSequence[matchedByteCount])
            {
                candidate[matchedByteCount] = value;
                matchedByteCount++;
            }
            else
            {
                ReleaseCandidate();
                if (value == forbiddenSequence[0])
                {
                    candidate[0] = value;
                    matchedByteCount = 1;
                }
                else
                {
                    safeBytes.Enqueue(value);
                }
            }

            if (matchedByteCount == forbiddenSequence.Length)
            {
                forbiddenSequenceFound = true;
                throw new ForbiddenSequenceException();
            }
        }
    }

    private void ReleaseCandidate()
    {
        for (int index = 0; index < matchedByteCount; index++)
        {
            safeBytes.Enqueue(candidate[index]);
        }

        matchedByteCount = 0;
    }

    private void ThrowIfForbiddenSequenceFound()
    {
        if (forbiddenSequenceFound)
        {
            throw new ForbiddenSequenceException();
        }
    }
}
