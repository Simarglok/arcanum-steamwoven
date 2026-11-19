using System.Text;

namespace Arcanum.Data.Hd;

/// <summary>
/// ARC-UI-2865: the portable key of a portrait redraw in an HD pack (entry kind <c>portrait</c>). A portrait is one bitmap of the original's
/// <c>portrait/</c> folder (64 × 64 <c>&lt;stem&gt;.bmp</c>, 128 × 128 <c>&lt;stem&gt;_b.bmp</c>), so the key is its lowercase path; the Legacy frame
/// is that bitmap's decoded pixels (<see cref="Characters.PortraitBitmap"/>), checked like any frame's. The key is carried by an
/// <see cref="HdSpriteKey"/> (hash of the path, <c>View</c> 0, which no sprite (at least 132) or terrain chunk (−1) has, group, frame and palette 0), so the
/// pack's lookup, file checks and the optimized cache serve portraits unchanged. Godot-free.
/// </summary>
public static class HdPortraitKey
{
    public const string Folder = "portrait/";

    /// <summary>The key path of a portrait file name: <see cref="Folder"/> + the file name, lowercase, <c>/</c> separators, ending in <c>.bmp</c>.</summary>
    public static string Path(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var name = fileName.Trim().Replace('\\', '/').ToLowerInvariant();
        if (!name.StartsWith(Folder, StringComparison.Ordinal)) name = Folder + name;
        if (!name.EndsWith(".bmp", StringComparison.Ordinal) || name.Length <= Folder.Length + 4 || name.IndexOf('/', Folder.Length) >= 0)
            throw new ArgumentException("A portrait path is portrait/<name>.bmp.", nameof(fileName));
        return name;
    }

    /// <summary>The manifest text of <c>portrait.path</c> for an acceptable path: lowercase <c>portrait/[a-z0-9_-]{1,48}.bmp</c>; otherwise false (such a portrait has no HD entry).</summary>
    public static bool TryPath(string fileName, out string path)
    {
        path = "";
        try { path = Path(fileName); } catch (ArgumentException) { return false; }
        var stem = path[Folder.Length..^4];
        return stem.Length is >= 1 and <= 48 && stem.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
    }

    /// <summary>SHA-256 (lowercase hex) of the path as ASCII bytes: the entry's <see cref="HdPackEntry.NameSha256"/>.</summary>
    public static string NameSha256(string path) => HdPackPath.Sha256(Encoding.ASCII.GetBytes(path));

    public static HdSpriteKey Key(string path) => new(NameSha256(path), 0, 0, 0, 0);

    /// <summary>The question for one decoded portrait bitmap (<paramref name="rgba"/> is <c>width × height × 4</c> bytes).</summary>
    public static HdSpriteQuery Query(string fileName, int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length != (long)width * height * 4) throw new ArgumentException("Legacy RGBA must be width × height × 4 bytes.", nameof(rgba));
        var path = Path(fileName);
        return new(Key(path), NameSha256(path), new(width, height, HdPackPath.Sha256(rgba)));
    }
}
