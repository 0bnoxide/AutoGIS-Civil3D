using System.Text;

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
    private const string ForbiddenText = "<!DOCTYPE";

    private static readonly byte[][] ForbiddenSequences =
    [
        Encoding.ASCII.GetBytes(ForbiddenText),
        Encoding.Unicode.GetBytes(ForbiddenText),
        Encoding.BigEndianUnicode.GetBytes(ForbiddenText),
        Encoding.UTF32.GetBytes(ForbiddenText),
        new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetBytes(ForbiddenText)
    ];

    private readonly Stream source;
    private readonly byte[] sourceBuffer = new byte[4096];
    private readonly List<byte> candidate = [];
    private readonly Queue<byte> safeBytes = new();
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
        foreach (byte value in bytes)
        {
            candidate.Add(value);
            while (candidate.Count > 0)
            {
                if (MatchesForbiddenSequence())
                {
                    forbiddenSequenceFound = true;
                    throw new ForbiddenSequenceException();
                }

                if (IsForbiddenSequencePrefix())
                {
                    break;
                }

                safeBytes.Enqueue(candidate[0]);
                candidate.RemoveAt(0);
            }
        }
    }

    private void ReleaseCandidate()
    {
        foreach (byte value in candidate)
        {
            safeBytes.Enqueue(value);
        }

        candidate.Clear();
    }

    private bool MatchesForbiddenSequence()
    {
        foreach (byte[] forbiddenSequence in ForbiddenSequences)
        {
            if (candidate.Count == forbiddenSequence.Length &&
                CandidateMatches(forbiddenSequence))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsForbiddenSequencePrefix()
    {
        foreach (byte[] forbiddenSequence in ForbiddenSequences)
        {
            if (candidate.Count < forbiddenSequence.Length &&
                CandidateMatches(forbiddenSequence))
            {
                return true;
            }
        }

        return false;
    }

    private bool CandidateMatches(ReadOnlySpan<byte> forbiddenSequence)
    {
        for (int index = 0; index < candidate.Count; index++)
        {
            if (candidate[index] != forbiddenSequence[index])
            {
                return false;
            }
        }

        return true;
    }

    private void ThrowIfForbiddenSequenceFound()
    {
        if (forbiddenSequenceFound)
        {
            throw new ForbiddenSequenceException();
        }
    }
}
