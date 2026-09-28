using System.Buffers.Binary;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace Arcanum.Data;

/// <summary>Reader for the evidence-bounded 1TAD DAT subset. It never normalizes entry names.</summary>
public sealed class DatArchive : IDisposable
{
    private const int FooterSize = 28;
    private const int EntryFixedSize = 20;
    private const uint PlainFlag = 0x001;
    private const uint ZlibFlag = 0x002;
    private const uint DirectoryFlag = 0x400;
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly DatReaderOptions _options;
    private readonly object _readGate = new();
    private bool _disposed;

    private DatArchive(Stream stream, bool ownsStream, DatReaderOptions options, DatFooter footer, List<DatEntry> entries)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _options = options;
        Footer = footer;
        Entries = entries.AsReadOnly();
    }

    public DatFooter Footer { get; }
    public IReadOnlyList<DatEntry> Entries { get; }

    public static DatArchive Open(string path, DatReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return OpenCore(stream, true, options ?? new DatReaderOptions());
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static DatArchive Open(Stream stream, DatReaderOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            if (!stream.CanRead || !stream.CanSeek)
            {
                throw new ArgumentException("The DAT stream must be readable and seekable.", nameof(stream));
            }
            return OpenCore(stream, !leaveOpen, options ?? new DatReaderOptions());
        }
        catch
        {
            if (!leaveOpen)
            {
                stream.Dispose();
            }
            throw;
        }
    }

    public byte[] ReadEntry(DatEntry entry)
    {
        lock (_readGate)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(entry);
            if (!ReferenceEquals(entry.Owner, this))
            {
                throw new ArgumentException("The entry belongs to a different archive.", nameof(entry));
            }
            if (entry.Flags == DirectoryFlag)
            {
                throw new DatUnsupportedEntryException("Directory metadata records have no supported payload read.");
            }
            if (entry.Flags != PlainFlag && entry.Flags != ZlibFlag)
            {
                throw new DatUnsupportedEntryException($"Unsupported DAT entry flags 0x{entry.Flags:x8}.");
            }
            ValidatePayloadBounds(entry);
            CheckLimit(entry.StoredSize, _options.MaxStoredEntryBytes, "Stored entry size");
            CheckLimit(entry.DeclaredOutputSize, _options.MaxDecodedEntryBytes, "Decoded entry size");
            if (entry.StoredSize > int.MaxValue || entry.DeclaredOutputSize > int.MaxValue)
            {
                throw new DatLimitExceededException("Entry size exceeds the supported in-memory array range.");
            }
            if (entry.Flags == PlainFlag && entry.StoredSize != entry.DeclaredOutputSize)
            {
                throw new DatFormatException("Plain entry stored and declared sizes differ.");
            }

            var stored = new byte[(int)entry.StoredSize];
            _stream.Seek(entry.Offset, SeekOrigin.Begin);
            ReadExactly(_stream, stored, "entry payload");
            return entry.Flags == PlainFlag ? stored : InflateExact(stored, checked((int)entry.DeclaredOutputSize));
        }
    }

    public void Dispose()
    {
        lock (_readGate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_ownsStream)
            {
                _stream.Dispose();
            }
        }
    }

    private static DatArchive OpenCore(Stream stream, bool ownsStream, DatReaderOptions options)
    {
        options.Validate();
        var fileLength = stream.Length;
        if (fileLength < FooterSize)
        {
            throw new DatFormatException("DAT footer is truncated.");
        }
        if (fileLength > options.MaxArchiveBytes)
        {
            throw new DatLimitExceededException("Archive exceeds MaxArchiveBytes.");
        }

        Span<byte> footerBytes = stackalloc byte[FooterSize];
        stream.Seek(-FooterSize, SeekOrigin.End);
        ReadExactly(stream, footerBytes, "footer");
        var opaque = footerBytes[..16].ToArray();
        var marker = footerBytes.Slice(16, 4).ToArray();
        if (!marker.AsSpan().SequenceEqual("1TAD"u8))
        {
            throw new DatUnsupportedFormatException("DAT marker is not the supported 1TAD variant.");
        }
        var nameBytesHint = BinaryPrimitives.ReadUInt32LittleEndian(footerBytes.Slice(20, 4));
        var tailBytes = BinaryPrimitives.ReadUInt32LittleEndian(footerBytes.Slice(24, 4));
        if (tailBytes < FooterSize || tailBytes > fileLength)
        {
            throw new DatFormatException("DAT tail-region size is out of bounds.");
        }
        var indexStart = checked(fileLength - tailBytes);
        var indexEnd = checked(fileLength - FooterSize);
        var indexBytes = checked(indexEnd - indexStart);
        if (indexBytes > options.MaxIndexBytes)
        {
            throw new DatLimitExceededException("Index exceeds MaxIndexBytes.");
        }
        if (indexBytes < sizeof(uint))
        {
            throw new DatFormatException("DAT index has no entry count.");
        }

        stream.Seek(indexStart, SeekOrigin.Begin);
        Span<byte> u32 = stackalloc byte[4];
        ReadExactly(stream, u32, "entry count");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(u32);
        if (count > int.MaxValue || count > options.MaxEntryCount)
        {
            throw new DatLimitExceededException("Entry count exceeds reader limits.");
        }
        var minimumEntriesBytes = checked((long)count * 25L);
        if (minimumEntriesBytes > indexEnd - stream.Position)
        {
            throw new DatFormatException("Entry count is physically unreachable from the index.");
        }

        var footer = new DatFooter(opaque, marker, nameBytesHint, tailBytes, indexStart);
        var entries = new List<DatEntry>();
        long totalNameBytes = 0;
        Span<byte> fixedBytes = stackalloc byte[EntryFixedSize];
        for (var ordinal = 0; ordinal < (int)count; ordinal++)
        {
            EnsureRemaining(stream, indexEnd, 4, "entry name length");
            ReadExactly(stream, u32, "entry name length");
            var nameLength = BinaryPrimitives.ReadUInt32LittleEndian(u32);
            if (nameLength == 0)
            {
                throw new DatFormatException("Entry name has zero length.");
            }
            CheckLimit(nameLength, options.MaxNameBytes, "Entry name size");
            totalNameBytes = checked(totalNameBytes + nameLength);
            if (totalNameBytes > options.MaxTotalNameBytes)
            {
                throw new DatLimitExceededException("Total entry-name bytes exceed MaxTotalNameBytes.");
            }
            if (nameLength > int.MaxValue)
            {
                throw new DatLimitExceededException("Entry name does not fit in memory.");
            }
            EnsureRemaining(stream, indexEnd, checked((long)nameLength + EntryFixedSize), "entry");
            var rawName = new byte[(int)nameLength];
            ReadExactly(stream, rawName, "entry name");
            if (rawName[^1] != 0)
            {
                throw new DatFormatException("Entry name has no terminal NUL byte.");
            }
            ReadExactly(stream, fixedBytes, "entry fields");
            var entryOpaque = fixedBytes[..4].ToArray();
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(fixedBytes.Slice(4, 4));
            var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(fixedBytes.Slice(8, 4));
            var storedSize = BinaryPrimitives.ReadUInt32LittleEndian(fixedBytes.Slice(12, 4));
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(fixedBytes.Slice(16, 4));
            if ((flags & DirectoryFlag) == 0)
            {
                var end = checked((long)offset + storedSize);
                if (end > indexStart)
                {
                    throw new DatFormatException("Entry payload intersects the index.");
                }
            }
            entries.Add(new DatEntry(null, ordinal, rawName, entryOpaque, flags, declaredSize, storedSize, offset));
        }
        if (stream.Position != indexEnd)
        {
            throw new DatFormatException("DAT index has trailing bytes.");
        }

        var archive = new DatArchive(stream, ownsStream, options, footer, entries);
        foreach (var entry in entries)
        {
            entry.Owner = archive;
        }
        return archive;
    }

    private static byte[] InflateExact(byte[] stored, int declaredSize)
    {
        try
        {
            var inflater = new Inflater(noHeader: false);
            inflater.SetInput(stored);
            var output = new byte[declaredSize];
            var scratch = new byte[1];
            var writtenTotal = 0;
            while (writtenTotal < output.Length)
            {
                var written = inflater.Inflate(output, writtenTotal, output.Length - writtenTotal);
                if (written > 0)
                {
                    writtenTotal += written;
                    continue;
                }
                if (inflater.IsFinished)
                {
                    break;
                }
                if (inflater.IsNeedingDictionary)
                {
                    throw new DatFormatException("Zlib stream requires an unsupported dictionary.");
                }
                if (inflater.IsNeedingInput)
                {
                    throw new DatFormatException("Zlib stream is truncated.");
                }
                throw new DatFormatException("Zlib stream made no progress.");
            }

            // A positive-capacity probe drains the trailer and detects output beyond the
            // declared bound without allocating a declaredSize + 1 destination buffer.
            if (writtenTotal == output.Length)
            {
                if (inflater.Inflate(scratch, 0, 1) > 0)
                {
                    throw new DatFormatException("Zlib output exceeds declared length.");
                }
                if (!inflater.IsFinished)
                {
                    if (inflater.IsNeedingDictionary)
                    {
                        throw new DatFormatException("Zlib stream requires an unsupported dictionary.");
                    }
                    if (inflater.IsNeedingInput)
                    {
                        throw new DatFormatException("Zlib stream is truncated.");
                    }
                    throw new DatFormatException("Zlib stream made no progress.");
                }
            }
            if (!inflater.IsFinished || inflater.TotalOut != declaredSize || writtenTotal != declaredSize)
            {
                throw new DatFormatException("Zlib output length differs from declared length.");
            }
            if (inflater.RemainingInput != 0)
            {
                throw new DatFormatException("Zlib stream has trailing data.");
            }
            return output;
        }
        catch (SharpZipBaseException exception)
        {
            throw new DatFormatException("Zlib stream is malformed.", exception);
        }
    }

    private void ValidatePayloadBounds(DatEntry entry)
    {
        var end = checked((long)entry.Offset + entry.StoredSize);
        if (end > Footer.IndexOffset)
        {
            throw new DatFormatException("Entry payload is outside the payload area.");
        }
    }

    private static void EnsureRemaining(Stream stream, long end, long needed, string label)
    {
        if (needed < 0 || stream.Position > end || needed > end - stream.Position)
        {
            throw new DatFormatException($"DAT {label} is truncated.");
        }
    }

    private static void CheckLimit(uint value, uint limit, string label)
    {
        if (value > limit)
        {
            throw new DatLimitExceededException($"{label} exceeds its configured limit.");
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> destination, string label)
    {
        while (!destination.IsEmpty)
        {
            var read = stream.Read(destination);
            if (read == 0)
            {
                throw new DatFormatException($"DAT {label} is truncated.");
            }
            destination = destination[read..];
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DatArchive));
        }
    }
}
