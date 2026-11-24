using System.Globalization;
using System.Text;

namespace Arcanum.Data.Hd;

/// <summary>
/// ARC-RENDER-3106: the portable key of a town-map tile redraw in an HD pack (entry kind <c>townmap</c>). A town map is drawn from tiles of the original's
/// <c>townmap/&lt;name&gt;/&lt;name&gt;NNNNNN.bmp</c> (64 × 32, 8-bit; <c>.tmi</c> layout of ARC-UI-084), so the key is that lowercase path, exactly as
/// <c>AreaTownMapLayout.TilePath</c> builds it; the Legacy frame is the tile's decoded pixels (<see cref="Characters.PortraitBitmap"/>, the reader a portrait and a world-map tile
/// use), checked like any frame's. The path names the town and the tile index; another module's tile with the same path has other pixels and so fails the Legacy check (the
/// pack has one key space like every other kind). The key is carried by an <see cref="HdSpriteKey"/> (hash of the path, <c>View</c> 0), so the pack's lookup, file checks and
/// the device cache serve town tiles unchanged. The HD file is exactly <c>Scale ×</c> the Legacy size (256 × 128 at a 4× pack: render scale 1.0, a tile of the picture at the
/// <c>.tmi</c>'s own pixels), and the layout of the window, the scroll, the party marker, the fog and the clicks stay in the original's coordinates. Godot-free.
/// </summary>
public static class HdTownMapKey
{
    public const string Folder = "townmap/";
    public const int TileWidth = 64, TileHeight = 32, MaxTownLength = 40, IndexDigits = 6;

    /// <summary>The town-name characters an HD entry can name: lowercase letters, digits, space, apostrophe, dot, underscore, hyphen (the shipped names use these; a name with another
    /// character, an edge space or dot, or more than <see cref="MaxTownLength"/> characters has no HD entry and draws the BMP).</summary>
    public static bool IsTown(string town) =>
        town.Length is >= 1 and <= MaxTownLength && town[0] is not (' ' or '.') && town[^1] is not (' ' or '.')
        && town.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or ' ' or '\'' or '.' or '_' or '-');

    /// <summary>The key path of a town tile: <c>townmap/&lt;town&gt;/&lt;town&gt;NNNNNN.bmp</c>, lowercase.</summary>
    public static string Path(string town, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(town);
        if (index is < 0 or > 999_999) throw new ArgumentOutOfRangeException(nameof(index));
        var name = town.Trim().ToLowerInvariant();
        return $"{Folder}{name}/{name}{index.ToString("D6", CultureInfo.InvariantCulture)}.bmp";
    }

    /// <summary>Splits an acceptable key path into its town and tile index (lowercase, the file name is the folder name plus six digits and <c>.bmp</c>); otherwise false.</summary>
    public static bool TryParse(string fileName, out string town, out int index)
    {
        town = ""; index = 0;
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var name = fileName.Trim().Replace('\\', '/').ToLowerInvariant();
        if (!name.StartsWith(Folder, StringComparison.Ordinal) || !name.EndsWith(".bmp", StringComparison.Ordinal)) return false;
        var rest = name[Folder.Length..^4];
        var slash = rest.IndexOf('/');
        if (slash <= 0 || rest.IndexOf('/', slash + 1) >= 0) return false;
        var folder = rest[..slash]; var file = rest[(slash + 1)..];
        if (file.Length != folder.Length + IndexDigits || !file.StartsWith(folder, StringComparison.Ordinal) || !IsTown(folder)) return false;
        var digits = file[folder.Length..];
        if (!digits.All(char.IsAsciiDigit)) return false;
        town = folder; index = int.Parse(digits, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>The manifest text of the entry's <c>path</c> for an acceptable path (<see cref="TryParse"/>); otherwise false (such a tile has no HD entry).</summary>
    public static bool TryPath(string fileName, out string path)
    {
        path = "";
        if (!TryParse(fileName, out var town, out var index)) return false;
        path = Path(town, index);
        return true;
    }

    /// <summary>The town part of a pack file name for a tile: the town with every character outside <c>[a-z0-9_-]</c> turned into <c>-</c> (the pack's path segments).</summary>
    public static string FileTown(string town) => new([.. town.Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' ? c : '-')]);

    /// <summary>SHA-256 (lowercase hex) of the path as ASCII bytes: the entry's <see cref="HdPackEntry.NameSha256"/> (a path with a non-ASCII character is not acceptable, so ASCII is exact).</summary>
    public static string NameSha256(string path) => HdPackPath.Sha256(Encoding.ASCII.GetBytes(path));

    public static HdSpriteKey Key(string path) => new(NameSha256(path), 0, 0, 0, 0);

    /// <summary>The question for one decoded town tile (<paramref name="rgba"/> is <c>width × height × 4</c> bytes); the tile is 64 × 32.</summary>
    public static HdSpriteQuery Query(string fileName, int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width != TileWidth || height != TileHeight || rgba.Length != (long)width * height * 4) throw new ArgumentException("A town-map tile is 64 × 32 and its RGBA is width × height × 4 bytes.", nameof(rgba));
        if (!TryPath(fileName, out var path)) throw new ArgumentException("A town-map tile path is townmap/<town>/<town>NNNNNN.bmp.", nameof(fileName));
        return new(Key(path), NameSha256(path), new(width, height, HdPackPath.Sha256(rgba)));
    }
}
