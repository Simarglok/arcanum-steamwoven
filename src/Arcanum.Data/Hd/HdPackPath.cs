using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Arcanum.Data.Hd;

/// <summary>Relative paths inside a pack, chosen to mean the same file on macOS, Linux
/// (SteamOS) and Windows, on case-sensitive and case-insensitive file systems: lowercase
/// ASCII segments joined by '/', 1..8 segments, at most 200 characters, no '.'/'..', no
/// leading or trailing dot, no Windows device name (con, prn, aux, nul, com0-9, lpt0-9).</summary>
internal static partial class HdPackPath
{
    public const int MaxLength = 200;
    public const int MaxSegments = 8;

    [GeneratedRegex("^[a-z0-9_-]+(\\.[a-z0-9_-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentPattern();
    [GeneratedRegex("^(con|prn|aux|nul|com[0-9]|lpt[0-9])$", RegexOptions.CultureInvariant)]
    private static partial Regex DevicePattern();

    public static string Validate(string value, string field, string extension)
    {
        if (!IsValid(value) || !value.EndsWith(extension, StringComparison.Ordinal))
            throw new HdFormatException(HdFormatError.InvalidPath, field + " must be a portable relative path ending in " + extension);
        return value;
    }

    public static bool IsValid(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength) return false;
        var segments = value.Split('/');
        return segments.Length <= MaxSegments && segments.All(s => s.Length is > 0 and <= 64 && SegmentPattern().IsMatch(s) && !DevicePattern().IsMatch(s.Split('.')[0]));
    }

    public static string Full(string root, string relative) => Path.Combine([root, .. relative.Split('/')]);

    /// <summary>True when the file or any directory between the pack root and it is a
    /// symbolic link (or a Windows junction). Other reparse points, such as cloud-sync
    /// placeholders, are ordinary files here. Missing path parts are not links.</summary>
    public static bool HasLink(string root, string relative)
    {
        var current = root;
        foreach (var segment in relative.Split('/'))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null) return true;
            if (!info.Exists) return false;
        }
        return false;
    }

    public static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
