using System.Buffers.Binary;
using System.IO.Compression;

namespace Arcanum.Data.Hd;

public enum HdPngStatus { Decoded, Malformed, Unsupported, TooLarge }
public sealed record HdPngHeader(int Width, int Height);
public sealed record HdPngImage(int Width, int Height, byte[] Rgba);
/// <summary>A decoded image or the typed reason it was refused. <see cref="Detail"/> never
/// carries pixel bytes.</summary>
public sealed record HdPngResult(HdPngStatus Status, HdPngImage? Image, string? Detail = null);
public sealed record HdPngHeaderResult(HdPngStatus Status, HdPngHeader? Header, string? Detail = null);

/// <summary>The one PNG profile an HD pack stores: 8-bit RGBA (colour type 6), deflate,
/// filter method 0, no interlace, IDAT chunks consecutive, IEND last with nothing after it.
/// Every CRC is checked; ancillary chunks are skipped; any other critical chunk (PLTE
/// included) is refused. This is a strict reader for pack files written by
/// <see cref="EncodeRgba"/> or an ordinary RGBA encoder, not a general PNG decoder. The
/// zlib header and Adler-32 are checked. The output buffer (h·(1 + 4w)
/// bytes) is allocated once the header passes the caller's pixel limit.</summary>
public static class HdPng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Reads and checks the signature and IHDR only (no inflate).</summary>
    public static HdPngHeaderResult ReadHeader(ReadOnlySpan<byte> png)
    {
        if (png.Length < Signature.Length + 25 || !png[..Signature.Length].SequenceEqual(Signature))
            return new(HdPngStatus.Malformed, null, "signature");
        var chunk = ReadChunk(png, Signature.Length, out var type, out var data, out _);
        if (chunk is not null) return new(HdPngStatus.Malformed, null, chunk);
        if (type != "IHDR" || data.Length != 13) return new(HdPngStatus.Malformed, null, "IHDR must be first");
        var width = BinaryPrimitives.ReadUInt32BigEndian(data);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue) return new(HdPngStatus.Malformed, null, "IHDR size");
        if (data[8] != 8 || data[9] != 6 || data[10] != 0 || data[11] != 0 || data[12] != 0)
            return new(HdPngStatus.Unsupported, null, "only 8-bit RGBA, deflate, filter 0, no interlace");
        return new(HdPngStatus.Decoded, new((int)width, (int)height));
    }

    /// <param name="maxPixels">Refuses (<see cref="HdPngStatus.TooLarge"/>) an image whose
    /// width × height exceeds this before anything is inflated.</param>
    public static HdPngResult Decode(ReadOnlySpan<byte> png, long maxPixels)
    {
        if (maxPixels <= 0) throw new ArgumentOutOfRangeException(nameof(maxPixels));
        var header = ReadHeader(png);
        if (header.Status != HdPngStatus.Decoded) return new(header.Status, null, header.Detail);
        var (width, height) = (header.Header!.Width, header.Header.Height);
        if ((long)width * height > maxPixels) return new(HdPngStatus.TooLarge, null, $"{width}x{height}");
        var stride = checked((long)width * 4);
        var filtered = height * (stride + 1);
        if (filtered > int.MaxValue || stride * height > int.MaxValue) return new(HdPngStatus.TooLarge, null, $"{width}x{height}");

        using var compressed = new MemoryStream();
        var at = Signature.Length + 25; var idatState = 0; // 0 before, 1 inside, 2 after the IDAT run
        var ended = false;
        while (!ended)
        {
            var error = ReadChunk(png, at, out var type, out var data, out var next);
            if (error is not null) return new(HdPngStatus.Malformed, null, error);
            switch (type)
            {
                case "IHDR": return new(HdPngStatus.Malformed, null, "second IHDR");
                case "IDAT":
                    if (idatState == 2) return new(HdPngStatus.Malformed, null, "IDAT chunks not consecutive");
                    idatState = 1; compressed.Write(data);
                    break;
                case "IEND":
                    if (data.Length != 0) return new(HdPngStatus.Malformed, null, "IEND length");
                    if (next != png.Length) return new(HdPngStatus.Malformed, null, "data after IEND");
                    ended = true;
                    break;
                default:
                    if (char.IsAsciiLetterUpper(type[0])) return new(HdPngStatus.Unsupported, null, "critical chunk " + type);
                    break;
            }
            if (type != "IDAT" && idatState == 1) idatState = 2;
            at = next;
        }
        if (idatState == 0) return new(HdPngStatus.Malformed, null, "no IDAT");

        // zlib (RFC 1950) checked by hand: the header, one deflate stream giving exactly the
        // image data, and the trailing Adler-32 of that data.
        var z = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
        if (z.Length < 6 || (z[0] & 0x0F) != 8 || z[0] >> 4 > 7 || (z[0] << 8 | z[1]) % 31 != 0 || (z[1] & 0x20) != 0)
            return new(HdPngStatus.Malformed, null, "zlib header");
        var raw = new byte[(int)filtered];
        try
        {
            using var body = new MemoryStream(compressed.GetBuffer(), 2, z.Length - 6, writable: false);
            using var deflate = new DeflateStream(body, CompressionMode.Decompress);
            var read = 0;
            while (read < raw.Length)
            {
                var n = deflate.Read(raw, read, raw.Length - read);
                if (n == 0) return new(HdPngStatus.Malformed, null, "image data too short");
                read += n;
            }
            if (deflate.ReadByte() != -1) return new(HdPngStatus.Malformed, null, "image data too long");
        }
        catch (InvalidDataException) { return new(HdPngStatus.Malformed, null, "deflate stream"); }
        if (BinaryPrimitives.ReadUInt32BigEndian(z[^4..]) != Adler32(raw)) return new(HdPngStatus.Malformed, null, "zlib checksum");

        var rgba = new byte[(int)(stride * height)];
        var s = (int)stride;
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (s + 1)];
            var src = raw.AsSpan(y * (s + 1) + 1, s);
            var row = rgba.AsSpan(y * s, s);
            var up = y == 0 ? ReadOnlySpan<byte>.Empty : rgba.AsSpan((y - 1) * s, s);
            for (var x = 0; x < s; x++)
            {
                int a = x >= 4 ? row[x - 4] : 0, b = y > 0 ? up[x] : 0, c = x >= 4 && y > 0 ? up[x - 4] : 0;
                int predictor;
                switch (filter)
                {
                    case 0: predictor = 0; break;
                    case 1: predictor = a; break;
                    case 2: predictor = b; break;
                    case 3: predictor = (a + b) >> 1; break;
                    case 4: predictor = Paeth(a, b, c); break;
                    default: return new(HdPngStatus.Malformed, null, "filter type " + filter);
                }
                row[x] = (byte)(src[x] + predictor);
            }
        }
        return new(HdPngStatus.Decoded, new(width, height, rgba));
    }

    /// <summary>Writes the profile <see cref="Decode"/> reads: one IHDR, one IDAT (filter 0
    /// on every row), IEND. Deterministic for equal input.</summary>
    public static byte[] EncodeRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var stride = checked(width * 4);
        if (rgba.Length != checked(stride * height)) throw new ArgumentException("RGBA length must be width × height × 4.", nameof(rgba));
        using var deflated = new MemoryStream();
        using (var zlib = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            for (var y = 0; y < height; y++) { zlib.WriteByte(0); zlib.Write(rgba.Slice(y * stride, stride)); }
        using var output = new MemoryStream();
        output.Write(Signature);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(output, "IHDR", ihdr);
        WriteChunk(output, "IDAT", deflated.GetBuffer().AsSpan(0, (int)deflated.Length));
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    internal static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(word, (uint)data.Length); output.Write(word);
        Span<byte> name = stackalloc byte[4];
        for (var i = 0; i < 4; i++) name[i] = (byte)type[i];
        output.Write(name); output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc(name, data)); output.Write(word);
    }

    private static string? ReadChunk(ReadOnlySpan<byte> png, int at, out string type, out ReadOnlySpan<byte> data, out int next)
    {
        type = string.Empty; data = default; next = at;
        if ((long)at + 12 > png.Length) return "truncated chunk";
        var length = BinaryPrimitives.ReadUInt32BigEndian(png[at..]);
        if (length > int.MaxValue || at + 12L + length > png.Length) return "chunk length";
        var name = png.Slice(at + 4, 4);
        foreach (var letter in name) if (!char.IsAsciiLetter((char)letter)) return "chunk type";
        data = png.Slice(at + 8, (int)length);
        if (BinaryPrimitives.ReadUInt32BigEndian(png[(at + 8 + (int)length)..]) != Crc(name, data)) return "chunk CRC";
        type = string.Create(4, name.ToArray(), static (span, bytes) => { for (var i = 0; i < 4; i++) span[i] = (char)bytes[i]; });
        next = at + 12 + (int)length;
        return null;
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        while (data.Length > 0)
        {
            var block = data[..Math.Min(data.Length, 5552)];
            foreach (var x in block) { a += x; b += a; }
            a %= 65521; b %= 65521;
            data = data[block.Length..];
        }
        return b << 16 | a;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c; int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
