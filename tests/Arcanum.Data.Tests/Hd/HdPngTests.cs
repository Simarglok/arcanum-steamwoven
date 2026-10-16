using System.Buffers.Binary;
using System.IO.Compression;
using Arcanum.Data.Hd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Hd;

[TestClass]
public sealed class HdPngTests
{
    private static byte[] Pattern(int w, int h)
    {
        var rgba = new byte[w * h * 4];
        for (var i = 0; i < rgba.Length; i++) rgba[i] = (byte)(i * 37 + i / 7);
        return rgba;
    }

    [TestMethod]
    public void Encoded_rgba_round_trips_exactly_and_is_deterministic()
    {
        var rgba = Pattern(13, 7);
        var png = HdPng.EncodeRgba(13, 7, rgba);
        CollectionAssert.AreEqual(png, HdPng.EncodeRgba(13, 7, rgba));
        var result = HdPng.Decode(png, 1000);
        Assert.AreEqual(HdPngStatus.Decoded, result.Status, result.Detail);
        Assert.AreEqual(13, result.Image!.Width); Assert.AreEqual(7, result.Image.Height);
        CollectionAssert.AreEqual(rgba, result.Image.Rgba);
        Assert.AreEqual(new HdPngHeader(13, 7), HdPng.ReadHeader(png).Header);
    }

    [TestMethod]
    public void Every_filter_type_and_split_idat_decode_to_the_same_pixels()
    {
        const int w = 9, h = 6;
        var rgba = Pattern(w, h);
        for (var filter = 0; filter <= 4; filter++)
        {
            var png = Build(w, h, Filtered(rgba, w, h, filter), splitIdat: true, ancillary: true);
            var result = HdPng.Decode(png, w * h);
            Assert.AreEqual(HdPngStatus.Decoded, result.Status, $"filter {filter}: {result.Detail}");
            CollectionAssert.AreEqual(rgba, result.Image!.Rgba, "filter " + filter);
        }
    }

    [TestMethod]
    public void Malformed_unsupported_and_oversized_files_are_typed_refusals()
    {
        var rgba = Pattern(4, 3);
        var good = HdPng.EncodeRgba(4, 3, rgba);

        var badSignature = (byte[])good.Clone(); badSignature[1] ^= 1;
        Assert.AreEqual(HdPngStatus.Malformed, HdPng.Decode(badSignature, 100).Status);
        var badCrc = (byte[])good.Clone(); badCrc[8 + 8 + 13] ^= 1; // IHDR CRC
        Assert.AreEqual(HdPngStatus.Malformed, HdPng.Decode(badCrc, 100).Status);
        Assert.AreEqual(HdPngStatus.Malformed, HdPng.Decode([.. good, 0], 100).Status, "data after IEND");
        Assert.AreEqual(HdPngStatus.Malformed, HdPng.Decode(good.AsSpan(0, good.Length - 12), 100).Status, "no IEND");
        Assert.AreEqual(HdPngStatus.Malformed, HdPng.Decode(Build(4, 3, Filtered(rgba, 4, 3, 0)[..^1]), 100).Status, "short image data");
        Assert.AreEqual(HdPngStatus.Malformed, HdPng.Decode(Build(4, 3, [.. Filtered(rgba, 4, 3, 0), 0]), 100).Status, "long image data");
        var badFilter = Filtered(rgba, 4, 3, 0); badFilter[0] = 5;
        Assert.AreEqual(HdPngStatus.Malformed, HdPng.Decode(Build(4, 3, badFilter), 100).Status, "filter 5");
        Assert.AreEqual(HdPngStatus.Malformed, HdPng.Decode(Build(4, 3, Filtered(rgba, 4, 3, 0), splitIdat: true, ancillaryBetweenIdat: true), 100).Status,
            "IDAT not consecutive");
        Assert.AreEqual(HdPngStatus.Malformed, HdPng.Decode(Build(4, 3, Filtered(rgba, 4, 3, 0), idat: false), 100).Status, "no IDAT");

        Assert.AreEqual(HdPngStatus.Unsupported, HdPng.Decode(Build(4, 3, Filtered(rgba, 4, 3, 0), interlace: 1), 100).Status);
        Assert.AreEqual(HdPngStatus.Unsupported, HdPng.Decode(Build(4, 3, Filtered(rgba, 4, 3, 0), colourType: 2), 100).Status);
        Assert.AreEqual(HdPngStatus.Unsupported, HdPng.Decode(Build(4, 3, Filtered(rgba, 4, 3, 0), plte: true), 100).Status);

        Assert.AreEqual(HdPngStatus.TooLarge, HdPng.Decode(good, 11).Status);
        Assert.AreEqual(HdPngStatus.Decoded, HdPng.Decode(good, 12).Status);
    }

    private static byte[] Filtered(byte[] rgba, int w, int h, int filter)
    {
        var stride = w * 4; var output = new byte[h * (stride + 1)];
        for (var y = 0; y < h; y++)
        {
            output[y * (stride + 1)] = (byte)filter;
            for (var x = 0; x < stride; x++)
            {
                int raw = rgba[y * stride + x], a = x >= 4 ? rgba[y * stride + x - 4] : 0, b = y > 0 ? rgba[(y - 1) * stride + x] : 0,
                    c = x >= 4 && y > 0 ? rgba[(y - 1) * stride + x - 4] : 0;
                var p = a + b - c; int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                var predictor = filter switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) >> 1, _ => pa <= pb && pa <= pc ? a : pb <= pc ? b : c };
                output[y * (stride + 1) + 1 + x] = (byte)(raw - predictor);
            }
        }
        return output;
    }

    private static byte[] Build(int w, int h, byte[] filtered, bool splitIdat = false, bool ancillary = false, bool ancillaryBetweenIdat = false,
        bool idat = true, int interlace = 0, int colourType = 6, bool plte = false)
    {
        using var deflated = new MemoryStream();
        using (var z = new ZLibStream(deflated, CompressionLevel.Fastest, leaveOpen: true)) z.Write(filtered);
        var data = deflated.ToArray();
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)w); BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)h);
        ihdr[8] = 8; ihdr[9] = (byte)colourType; ihdr[12] = (byte)interlace;
        HdPng.WriteChunk(output, "IHDR", ihdr);
        if (ancillary) HdPng.WriteChunk(output, "tEXt", "Comment\0synthetic"u8);
        if (plte) HdPng.WriteChunk(output, "PLTE", new byte[3]);
        if (idat)
        {
            if (!splitIdat) HdPng.WriteChunk(output, "IDAT", data);
            else
            {
                var half = data.Length / 2;
                HdPng.WriteChunk(output, "IDAT", data.AsSpan(0, half));
                if (ancillaryBetweenIdat) HdPng.WriteChunk(output, "tEXt", "x\0y"u8);
                HdPng.WriteChunk(output, "IDAT", data.AsSpan(half));
            }
        }
        HdPng.WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }
}
