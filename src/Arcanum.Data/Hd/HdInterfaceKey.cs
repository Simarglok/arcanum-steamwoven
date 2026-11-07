using System.Text;

namespace Arcanum.Data.Hd;

/// <summary>
/// ARC-DEV-242: the portable key of interface ART (<c>art/interface/*.art</c>) in an HD pack. Interface art is
/// drawn from palette slot 0, group 0, whole-file frames (<see cref="ArtDecoderOptions.AllowTrailingBytes"/>), so the
/// key is <see cref="HdSpriteKey"/> with <c>Group = 0</c>, <c>Palette = 0</c> and the frame number, the SHA-256 of the whole
/// file the content stack's winner holds, <c>View</c> = that file's length, and the name hash is the SHA-256 of the lowercase
/// ASCII path (<c>art/interface/&lt;file&gt;</c>), so the builder and the viewer make the same key without a DAT entry's
/// raw name bytes. Godot-free.
/// </summary>
public static class HdInterfaceKey
{
    public const string Folder = "art/interface/";

    /// <summary>The path hashed as the name: <see cref="Folder"/> + the file name, lowercase, with <c>/</c> separators.</summary>
    public static string Path(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var name = fileName.Trim().Replace('\\', '/').ToLowerInvariant();
        return name.StartsWith(Folder, StringComparison.Ordinal) ? name : Folder + name;
    }

    /// <summary>SHA-256 (lowercase hex) of <see cref="Path"/> as ASCII bytes.</summary>
    public static string NameSha256(string fileName) => HdPackPath.Sha256(Encoding.ASCII.GetBytes(Path(fileName)));

    /// <summary>The identity of one interface ART file: the hash and the byte length of its whole payload, and its name hash.</summary>
    public static HdInterfaceArtIdentity Identity(string fileName, ReadOnlySpan<byte> payload)
        => new(HdPackPath.Sha256(payload), payload.Length, NameSha256(fileName));

    /// <summary>The question for one decoded interface frame (<paramref name="rgba"/> is <c>width × height × 4</c> bytes).</summary>
    public static HdSpriteQuery Query(HdInterfaceArtIdentity art, int frame, int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentNullException.ThrowIfNull(art);
        if (frame < 0 || width <= 0 || height <= 0 || rgba.Length != (long)width * height * 4) throw new ArgumentException("Legacy RGBA must be width × height × 4 bytes.", nameof(rgba));
        return new(new(art.PayloadSha256, art.ViewLength, 0, frame, 0), art.NameSha256, new(width, height, HdPackPath.Sha256(rgba)));
    }
}

/// <summary>The install-independent identity of an interface ART file (see <see cref="HdInterfaceKey"/>).</summary>
public sealed record HdInterfaceArtIdentity(string PayloadSha256, int ViewLength, string NameSha256);
