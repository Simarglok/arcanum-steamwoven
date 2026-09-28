using Arcanum.Data.Terrain;

namespace Arcanum.TerrainViewer.Core;

public readonly record struct TerrainTextureKey(string SourceId, int DatOrdinal, uint Flags, uint DeclaredDecodedSize,
    string LogicalEntryName, int GroupIndex, int FrameIndex, int RequestedPaletteSlot)
{
    public static TerrainTextureKey FromRecord(TerrainRecord record) => new(record.ResourceIdentity.SourceId, record.ResourceIdentity.DatOrdinal,
        record.ResourceIdentity.Flags, record.ResourceIdentity.DeclaredDecodedSize, record.ResourceIdentity.LogicalEntryName,
        record.Selection.GroupIndex, record.Selection.FrameIndex, record.Selection.PaletteSlot);
}

public static class TerrainCachePolicy
{
    public const int MaxDistinctKeys = 4096;
    public const long MaxRgbaBytes = 256L * 1024 * 1024;

    public static long CheckedRgbaBytes(int width, int height) => checked((long)width * height * 4);

    public static void Validate(IReadOnlyDictionary<TerrainTextureKey, (int Width, int Height)> frames)
    {
        if (frames.Count > MaxDistinctKeys) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ResourceLimit));
        long total = 0;
        foreach (var frame in frames.Values)
        {
            if (frame.Width <= 0 || frame.Height <= 0) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.DecodeFailure));
            total = checked(total + CheckedRgbaBytes(frame.Width, frame.Height));
            if (total > MaxRgbaBytes) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ResourceLimit));
        }
    }

    /// <summary>Checks a frame before its bytes are retained, preventing unbounded failed loads.</summary>
    public static long AddCheckedFrame(long currentBytes, int currentCount, int width, int height)
    {
        if (currentCount >= MaxDistinctKeys) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ResourceLimit));
        if (width <= 0 || height <= 0) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.DecodeFailure));
        var total = checked(currentBytes + CheckedRgbaBytes(width, height));
        if (total > MaxRgbaBytes) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.ResourceLimit));
        return total;
    }
}

public sealed class TerrainViewerException(TerrainViewerError error) : Exception(error.Code.ToString())
{
    public TerrainViewerError Error { get; } = error;
}
