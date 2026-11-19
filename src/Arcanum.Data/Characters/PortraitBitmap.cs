namespace Arcanum.Data.Characters;

/// <summary>The decoded pixels of a portrait BMP: <see cref="Rgba"/> is <c>Width × Height × 4</c> bytes, top row first, alpha 255.</summary>
public sealed record PortraitBitmapImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// ARC-UI-2865: a Godot-free reader of the original's portrait images (<c>portrait/&lt;stem&gt;.bmp</c> 64 × 64 and
/// <c>portrait/&lt;stem&gt;_b.bmp</c> 128 × 128: uncompressed Windows bitmaps of 1, 4, 8 or 24 bits, no alpha). The HD pack binds a portrait to the
/// SHA-256 of these decoded pixels (<see cref="Hd.HdPortraitKey"/>), so the importer that writes a pack and the viewer that reads it must hash
/// the same pixels; both take them from this reader (the viewer still draws the Legacy texture it always drew). A file this reader does not
/// take (compression, 16 or 32 bits, a truncated or oversized image) is <c>null</c>: the portrait then draws Legacy.
/// </summary>
public static class PortraitBitmap
{
    public const int MaxSide = 1024;

    public static PortraitBitmapImage? Decode(ReadOnlySpan<byte> file)
    {
        if (file.Length < 54 || file[0] != (byte)'B' || file[1] != (byte)'M') return null;
        var dataOffset = ReadInt(file, 10);
        var headerSize = ReadInt(file, 14);
        if (headerSize < 40 || 14L + headerSize > file.Length) return null;
        var width = ReadInt(file, 18);
        var heightRaw = ReadInt(file, 22);
        var planes = ReadShort(file, 26);
        var bits = ReadShort(file, 28);
        var compression = ReadInt(file, 30);
        var colours = ReadInt(file, 46);
        var height = Math.Abs((long)heightRaw);
        if (planes != 1 || compression != 0 || width <= 0 || width > MaxSide || height <= 0 || height > MaxSide) return null;
        if (bits is not (1 or 4 or 8 or 24)) return null;
        var topDown = heightRaw < 0;

        var palette = new byte[256 * 3];
        if (bits <= 8)
        {
            var count = colours == 0 ? 1 << bits : colours;
            if (count < 1 || count > 1 << bits) return null;
            var tableOffset = 14L + headerSize;
            if (tableOffset + 4L * count > file.Length) return null;
            for (var i = 0; i < count; i++)
            {
                var at = (int)tableOffset + i * 4; // stored blue, green, red, reserved
                palette[i * 3] = file[at + 2]; palette[i * 3 + 1] = file[at + 1]; palette[i * 3 + 2] = file[at];
            }
        }
        var stride = ((long)width * bits + 31) / 32 * 4;
        if (dataOffset < 14 + headerSize || dataOffset + stride * height > file.Length) return null;
        var rgba = new byte[(long)width * height * 4];
        for (var row = 0; row < height; row++)
        {
            var source = (int)(dataOffset + stride * (topDown ? row : height - 1 - row));
            var target = row * width * 4;
            for (var x = 0; x < width; x++, target += 4)
            {
                if (bits == 24)
                {
                    var at = source + x * 3;
                    rgba[target] = file[at + 2]; rgba[target + 1] = file[at + 1]; rgba[target + 2] = file[at];
                }
                else
                {
                    var bit = x * bits;
                    var index = (file[source + bit / 8] >> (8 - bits - bit % 8)) & ((1 << bits) - 1);
                    rgba[target] = palette[index * 3]; rgba[target + 1] = palette[index * 3 + 1]; rgba[target + 2] = palette[index * 3 + 2];
                }
                rgba[target + 3] = byte.MaxValue;
            }
        }
        return new((int)width, (int)height, rgba);
    }

    private static int ReadInt(ReadOnlySpan<byte> file, int at) => file[at] | file[at + 1] << 8 | file[at + 2] << 16 | file[at + 3] << 24;
    private static int ReadShort(ReadOnlySpan<byte> file, int at) => file[at] | file[at + 1] << 8;
}
