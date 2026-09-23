namespace Arcanum.Data;

public readonly record struct ArtFrameSelection(int GroupIndex, int FrameIndex, int PaletteSlot);

public sealed class ArtDecoderOptions
{
    public int MaxInputBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxFrameCount { get; init; } = 65_536;
    public int MaxFrameTableBytes { get; init; } = 16 * 1024 * 1024;
    public int MaxDimension { get; init; } = 16_384;
    public int MaxFramePixels { get; init; } = 16 * 1024 * 1024;
    public int MaxRgbaBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxEncodedFrameBytes { get; init; } = 64 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxInputBytes <= 0 || MaxFrameCount <= 0 || MaxFrameTableBytes <= 0 ||
            MaxDimension <= 0 || MaxFramePixels <= 0 || MaxRgbaBytes <= 0 ||
            MaxEncodedFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ArtDecoderOptions), "All decoder limits must be positive.");
        }
    }
}

public class ArtException : Exception
{
    public ArtException(string message) : base(message) { }
    public ArtException(string message, Exception innerException) : base(message, innerException) { }
}

public class ArtFormatException : ArtException
{
    public ArtFormatException(string message) : base(message) { }
    public ArtFormatException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class ArtUnsupportedFormatException : ArtFormatException
{
    public ArtUnsupportedFormatException(string message) : base(message) { }
}

public sealed class ArtLimitExceededException : ArtException
{
    public ArtLimitExceededException(string message) : base(message) { }
}

public sealed class ArtHeader
{
    internal ArtHeader(byte[] rawBytes, uint flags, int framesPerSecond, int bitsPerPixel, uint[] palettePresenceWords,
        int actionFrame, int framesPerGroup, uint[] rawWords36To67, uint[] rawWords68To99, uint[] rawWords100To131,
        int physicalGroupCount)
    {
        RawBytes = Array.AsReadOnly(rawBytes);
        Flags = flags;
        FramesPerSecond = framesPerSecond;
        BitsPerPixel = bitsPerPixel;
        PalettePresenceWords = Array.AsReadOnly(palettePresenceWords);
        ActionFrame = actionFrame;
        FramesPerGroup = framesPerGroup;
        RawWords36To67 = Array.AsReadOnly(rawWords36To67);
        RawWords68To99 = Array.AsReadOnly(rawWords68To99);
        RawWords100To131 = Array.AsReadOnly(rawWords100To131);
        PhysicalGroupCount = physicalGroupCount;
    }

    public IReadOnlyList<byte> RawBytes { get; }
    public uint Flags { get; }
    public int FramesPerSecond { get; }
    public int BitsPerPixel { get; }
    public IReadOnlyList<uint> PalettePresenceWords { get; }
    public int ActionFrame { get; }
    public int FramesPerGroup { get; }
    public IReadOnlyList<uint> RawWords36To67 { get; }
    public IReadOnlyList<uint> RawWords68To99 { get; }
    public IReadOnlyList<uint> RawWords100To131 { get; }
    public int PhysicalGroupCount { get; }
}

public sealed class ArtPalette
{
    private readonly byte[] _rawTable;

    internal ArtPalette(int slot, uint presenceWord, byte[] rawTable)
    {
        Slot = slot;
        PresenceWord = presenceWord;
        _rawTable = rawTable;
        RawTable = Array.AsReadOnly(rawTable);
    }

    public int Slot { get; }
    public uint PresenceWord { get; }
    public IReadOnlyList<byte> RawTable { get; }

    internal ReadOnlySpan<byte> TableSpan => _rawTable;
}

public sealed class ArtFrameMetadata
{
    internal ArtFrameMetadata(int width, int height, int encodedPayloadLength, int hotX, int hotY, int offsetX, int offsetY, byte[] rawHeader)
    {
        Width = width;
        Height = height;
        EncodedPayloadLength = encodedPayloadLength;
        HotX = hotX;
        HotY = hotY;
        OffsetX = offsetX;
        OffsetY = offsetY;
        RawHeader = Array.AsReadOnly(rawHeader);
    }

    public int Width { get; }
    public int Height { get; }
    public int EncodedPayloadLength { get; }
    public int HotX { get; }
    public int HotY { get; }
    public int OffsetX { get; }
    public int OffsetY { get; }
    public IReadOnlyList<byte> RawHeader { get; }
}

public sealed class ArtFrame
{
    internal ArtFrame(ArtHeader header, ArtPalette?[] palettesBySlot, ArtFrameMetadata frame, int groupIndex, int frameIndex,
        int requestedPaletteSlot, int effectivePaletteSlot, byte[] rgba)
    {
        Header = header;
        PalettesBySlot = Array.AsReadOnly(palettesBySlot);
        Frame = frame;
        GroupIndex = groupIndex;
        FrameIndex = frameIndex;
        RequestedPaletteSlot = requestedPaletteSlot;
        EffectivePaletteSlot = effectivePaletteSlot;
        Width = frame.Width;
        Height = frame.Height;
        Rgba = Array.AsReadOnly(rgba);
    }

    public ArtHeader Header { get; }
    public IReadOnlyList<ArtPalette?> PalettesBySlot { get; }
    public ArtFrameMetadata Frame { get; }
    public int GroupIndex { get; }
    public int FrameIndex { get; }
    public int RequestedPaletteSlot { get; }
    public int EffectivePaletteSlot { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<byte> Rgba { get; }
}
