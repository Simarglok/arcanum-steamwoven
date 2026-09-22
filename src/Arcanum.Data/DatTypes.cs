namespace Arcanum.Data;

public sealed class DatReaderOptions
{
    public uint MaxArchiveBytes { get; init; } = uint.MaxValue;
    public uint MaxIndexBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxEntryCount { get; init; } = 1_000_000;
    public uint MaxNameBytes { get; init; } = 64 * 1024;
    public uint MaxTotalNameBytes { get; init; } = 64 * 1024 * 1024;
    public uint MaxStoredEntryBytes { get; init; } = 256 * 1024 * 1024;
    public uint MaxDecodedEntryBytes { get; init; } = 512 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxArchiveBytes == 0 || MaxIndexBytes == 0 || MaxEntryCount <= 0 ||
            MaxNameBytes == 0 || MaxTotalNameBytes == 0 ||
            MaxStoredEntryBytes == 0 || MaxDecodedEntryBytes == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DatReaderOptions), "All reader limits must be positive.");
        }
    }
}

public class DatException : Exception
{
    public DatException(string message) : base(message) { }
    public DatException(string message, Exception innerException) : base(message, innerException) { }
}

public class DatFormatException : DatException
{
    public DatFormatException(string message) : base(message) { }
    public DatFormatException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class DatUnsupportedFormatException : DatFormatException
{
    public DatUnsupportedFormatException(string message) : base(message) { }
}

public sealed class DatLimitExceededException : DatException
{
    public DatLimitExceededException(string message) : base(message) { }
}

public sealed class DatUnsupportedEntryException : DatException
{
    public DatUnsupportedEntryException(string message) : base(message) { }
}

public sealed class DatFooter
{
    internal DatFooter(byte[] opaqueBytes, byte[] markerBytes, uint nameBytesHint, uint tailRegionBytes, long indexOffset)
    {
        OpaqueBytes = Array.AsReadOnly(opaqueBytes);
        MarkerBytes = Array.AsReadOnly(markerBytes);
        NameBytesHint = nameBytesHint;
        TailRegionBytes = tailRegionBytes;
        IndexOffset = indexOffset;
    }

    public IReadOnlyList<byte> OpaqueBytes { get; }
    public IReadOnlyList<byte> MarkerBytes { get; }
    public uint NameBytesHint { get; }
    public uint TailRegionBytes { get; }
    public long IndexOffset { get; }
}

public sealed class DatEntry
{
    internal DatEntry(DatArchive? owner, int ordinal, byte[] rawNameBytes, byte[] opaqueBytes, uint flags, uint declaredOutputSize, uint storedSize, uint offset)
    {
        Owner = owner;
        Ordinal = ordinal;
        RawNameBytes = Array.AsReadOnly(rawNameBytes);
        OpaqueBytes = Array.AsReadOnly(opaqueBytes);
        Flags = flags;
        DeclaredOutputSize = declaredOutputSize;
        StoredSize = storedSize;
        Offset = offset;
    }

    internal DatArchive? Owner { get; set; }
    public int Ordinal { get; }
    public IReadOnlyList<byte> RawNameBytes { get; }
    public IReadOnlyList<byte> OpaqueBytes { get; }
    public uint Flags { get; }
    public uint DeclaredOutputSize { get; }
    public uint StoredSize { get; }
    public uint Offset { get; }
}
