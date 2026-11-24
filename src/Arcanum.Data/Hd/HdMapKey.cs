using System.Text;

namespace Arcanum.Data.Hd;

/// <summary>
/// ARC-RENDER-3079: the portable key of a world-map bitmap redraw in an HD pack (entry kinds <c>worldmap</c>, one of the 64 tiles of 250 × 250, and
/// <c>map-zoomed</c>, the continent picture of 365 × 365). Both are single bitmaps of the original's <c>worldmap/</c> folder, so the key is the lowercase path
/// (<c>worldmap/smallmapchunks001.bmp</c>, <c>worldmap/map_zoomed.bmp</c>); the Legacy frame is that bitmap's decoded pixels
/// (<see cref="Characters.PortraitBitmap"/>, the same reader a portrait uses), checked like any frame's. The key is carried by an <see cref="HdSpriteKey"/> (hash of the
/// path, <c>View</c> 0, which no sprite (at least 132) or terrain chunk (−1) has), so the pack's lookup, file checks and the optimized cache serve world-map
/// bitmaps unchanged. The HD file is exactly <c>Scale ×</c> the Legacy size (a multiple of 250 or 365; the integer ratio is the pack's recorded scale). Godot-free.
/// </summary>
public static class HdMapKey
{
    public const string Folder = "worldmap/";
    public const int TileSide = 250, ZoomedSide = 365;

    /// <summary>The Legacy side an entry of this kind must have (the tile or the continent picture).</summary>
    public static int LegacySide(HdEntryKind kind) => kind == HdEntryKind.MapZoomed ? ZoomedSide : TileSide;

    /// <summary>The key path of a world-map bitmap file name: <see cref="Folder"/> + the file name, lowercase, <c>/</c> separators, ending in <c>.bmp</c>.</summary>
    public static string Path(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var name = fileName.Trim().Replace('\\', '/').ToLowerInvariant();
        if (!name.StartsWith(Folder, StringComparison.Ordinal)) name = Folder + name;
        if (!name.EndsWith(".bmp", StringComparison.Ordinal) || name.Length <= Folder.Length + 4 || name.IndexOf('/', Folder.Length) >= 0)
            throw new ArgumentException("A world-map bitmap path is worldmap/<name>.bmp.", nameof(fileName));
        return name;
    }

    /// <summary>The manifest text of the entry's <c>path</c> for an acceptable path: lowercase <c>worldmap/[a-z0-9_-]{1,48}.bmp</c>; otherwise false (such a bitmap has no HD entry).</summary>
    public static bool TryPath(string fileName, out string path)
    {
        path = "";
        try { path = Path(fileName); } catch (ArgumentException) { return false; }
        var stem = path[Folder.Length..^4];
        return stem.Length is >= 1 and <= 48 && stem.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
    }

    /// <summary>The kind an acceptable path takes in a pack for a bitmap of <paramref name="width"/> × <paramref name="height"/>: a tile for 250 × 250, the continent picture for 365 × 365, else null.</summary>
    public static HdEntryKind? KindOf(int width, int height) =>
        width == TileSide && height == TileSide ? HdEntryKind.WorldMap : width == ZoomedSide && height == ZoomedSide ? HdEntryKind.MapZoomed : null;

    /// <summary>SHA-256 (lowercase hex) of the path as ASCII bytes: the entry's <see cref="HdPackEntry.NameSha256"/>.</summary>
    public static string NameSha256(string path) => HdPackPath.Sha256(Encoding.ASCII.GetBytes(path));

    public static HdSpriteKey Key(string path) => new(NameSha256(path), 0, 0, 0, 0);

    /// <summary>The question for one decoded world-map bitmap (<paramref name="rgba"/> is <c>width × height × 4</c> bytes).</summary>
    public static HdSpriteQuery Query(string fileName, int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length != (long)width * height * 4) throw new ArgumentException("Legacy RGBA must be width × height × 4 bytes.", nameof(rgba));
        var path = Path(fileName);
        return new(Key(path), NameSha256(path), new(width, height, HdPackPath.Sha256(rgba)));
    }
}
