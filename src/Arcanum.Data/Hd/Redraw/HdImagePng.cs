using System.Buffers.Binary;
using System.IO.Compression;

namespace Arcanum.Data.Hd;

/// <summary>
/// ARC-DEV-242: a PNG reader for the owner's redraw images (local input of the import, not a pack file): grey, grey+alpha, RGB and RGBA at
/// 8 or 16 bits, no interlace; palette and interlaced files are refused (<see cref="HdPngStatus.Unsupported"/>). The result is float RGB
/// 0..255 (alpha is dropped: a redraw is opaque, the alpha comes from the Legacy frame). Every chunk CRC and the zlib checksum are checked.
/// </summary>
public static class HdImagePng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static (HdPngStatus Status, HdRgbImage? Image, string? Detail) Decode(ReadOnlySpan<byte> png, long maxPixels)
    {
        if (png.Length < Signature.Length + 25 || !png[..Signature.Length].SequenceEqual(Signature)) return (HdPngStatus.Malformed, null, "signature");
        var error = HdPng.ReadChunk(png, Signature.Length, out var type, out var ihdr, out var at);
        if (error is not null || type != "IHDR" || ihdr.Length != 13) return (HdPngStatus.Malformed, null, error ?? "IHDR must be first");
        var width = BinaryPrimitives.ReadUInt32BigEndian(ihdr); var height = BinaryPrimitives.ReadUInt32BigEndian(ihdr[4..]);
        int depth = ihdr[8], colour = ihdr[9];
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue) return (HdPngStatus.Malformed, null, "IHDR size");
        if (ihdr[10] != 0 || ihdr[11] != 0) return (HdPngStatus.Malformed, null, "compression or filter method");
        if (ihdr[12] != 0) return (HdPngStatus.Unsupported, null, "interlaced");
        if (colour is not (0 or 2 or 4 or 6) || depth is not (8 or 16)) return (HdPngStatus.Unsupported, null, $"colour type {colour} at {depth} bits");
        if ((long)width * height > maxPixels) return (HdPngStatus.TooLarge, null, $"{width}x{height}");
        int channels = colour switch { 0 => 1, 2 => 3, 4 => 2, _ => 4 }, bytes = depth / 8, pixel = channels * bytes;
        var stride = checked((int)width * pixel);
        var filtered = checked((long)height * (stride + 1));
        if (filtered > int.MaxValue) return (HdPngStatus.TooLarge, null, $"{width}x{height}");

        using var compressed = new MemoryStream();
        var ended = false; var idat = 0;
        while (!ended)
        {
            error = HdPng.ReadChunk(png, at, out type, out var data, out var next);
            if (error is not null) return (HdPngStatus.Malformed, null, error);
            switch (type)
            {
                case "IHDR": return (HdPngStatus.Malformed, null, "second IHDR");
                case "PLTE": return (HdPngStatus.Unsupported, null, "palette");
                case "IDAT":
                    if (idat == 2) return (HdPngStatus.Malformed, null, "IDAT chunks not consecutive");
                    idat = 1; compressed.Write(data);
                    break;
                case "IEND": ended = true; break;
                default:
                    if (char.IsAsciiLetterUpper(type[0])) return (HdPngStatus.Unsupported, null, "critical chunk " + type);
                    break;
            }
            if (type != "IDAT" && idat == 1) idat = 2;
            at = next;
        }
        if (compressed.Length < 6) return (HdPngStatus.Malformed, null, "no image data");
        var z = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
        if ((z[0] & 0x0F) != 8 || (z[0] << 8 | z[1]) % 31 != 0 || (z[1] & 0x20) != 0) return (HdPngStatus.Malformed, null, "zlib header");
        var raw = new byte[(int)filtered];
        try
        {
            using var body = new MemoryStream(compressed.GetBuffer(), 2, z.Length - 6, writable: false);
            using var deflate = new DeflateStream(body, CompressionMode.Decompress);
            var read = 0;
            while (read < raw.Length)
            {
                var n = deflate.Read(raw, read, raw.Length - read);
                if (n == 0) return (HdPngStatus.Malformed, null, "image data too short");
                read += n;
            }
        }
        catch (InvalidDataException) { return (HdPngStatus.Malformed, null, "deflate stream"); }

        var pixels = new byte[stride * (int)height];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            var src = raw.AsSpan(y * (stride + 1) + 1, stride);
            var row = pixels.AsSpan(y * stride, stride);
            var up = y == 0 ? ReadOnlySpan<byte>.Empty : pixels.AsSpan((y - 1) * stride, stride);
            for (var x = 0; x < stride; x++)
            {
                int a = x >= pixel ? row[x - pixel] : 0, b = y > 0 ? up[x] : 0, c = x >= pixel && y > 0 ? up[x - pixel] : 0;
                int predictor = filter switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) >> 1, 4 => HdPng.Paeth(a, b, c), _ => -1 };
                if (predictor < 0) return (HdPngStatus.Malformed, null, "filter type " + filter);
                row[x] = (byte)(src[x] + predictor);
            }
        }
        var rgb = new float[(int)width * (int)height * 3];
        for (var i = 0; i < (int)width * (int)height; i++)
        {
            float Sample(int channel) => bytes == 1 ? pixels[i * pixel + channel] : (pixels[i * pixel + channel * 2] << 8 | pixels[i * pixel + channel * 2 + 1]) / 257f;
            if (channels <= 2) { var g = Sample(0); rgb[i * 3] = g; rgb[i * 3 + 1] = g; rgb[i * 3 + 2] = g; }
            else { rgb[i * 3] = Sample(0); rgb[i * 3 + 1] = Sample(1); rgb[i * 3 + 2] = Sample(2); }
        }
        return (HdPngStatus.Decoded, new((int)width, (int)height, rgb), null);
    }
}
