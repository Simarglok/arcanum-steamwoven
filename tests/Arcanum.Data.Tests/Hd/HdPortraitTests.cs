using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Hd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Hd;

/// <summary>ARC-UI-2865: the portrait bitmap reader, the portrait key, the <c>portrait</c> pack entry (write, open, bind, refuse) and the portrait plan. Synthetic pixels only.</summary>
[TestClass]
public sealed class HdPortraitTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-hdp-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // A bottom-up uncompressed bitmap: 8 bits with a 256-entry palette, or 24 bits.
    private static byte[] Bmp(int width, int height, int bits, Func<int, int, (byte R, byte G, byte B)> pixel, bool topDown = false, int compression = 0)
    {
        var stride = (width * bits + 31) / 32 * 4;
        var palette = bits == 8 ? 256 * 4 : 0;
        var data = new byte[stride * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var row = (topDown ? y : height - 1 - y) * stride;
            var (r, g, b) = pixel(x, y);
            if (bits == 24) { data[row + x * 3] = b; data[row + x * 3 + 1] = g; data[row + x * 3 + 2] = r; }
            else data[row + x] = (byte)((r + g * 3 + b * 5) % 256);
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(54 + palette + data.Length); writer.Write(0); writer.Write(54 + palette);
        writer.Write(40); writer.Write(width); writer.Write(topDown ? -height : height); writer.Write((short)1); writer.Write((short)bits);
        writer.Write(compression); writer.Write(data.Length); writer.Write(0); writer.Write(0); writer.Write(bits == 8 ? 256 : 0); writer.Write(0);
        if (bits == 8)
            for (var i = 0; i < 256; i++) { writer.Write((byte)(255 - i)); writer.Write((byte)(i / 2)); writer.Write((byte)i); writer.Write((byte)0); } // blue, green, red
        writer.Write(data);
        return stream.ToArray();
    }

    private static (byte, byte, byte) Colour(int x, int y) => ((byte)(x * 30 + y), (byte)(y * 20 + x), (byte)(x ^ y));

    [TestMethod]
    public void BitmapReaderDecodesPalettedAndTrueColourWithTheRowOrderOfTheFile()
    {
        var truecolour = PortraitBitmap.Decode(Bmp(5, 4, 24, Colour))!;
        Assert.AreEqual((5, 4), (truecolour.Width, truecolour.Height));
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 5; x++)
        {
            var (r, g, b) = Colour(x, y); var at = (y * 5 + x) * 4;
            CollectionAssert.AreEqual(new[] { r, g, b, (byte)255 }, truecolour.Rgba[at..(at + 4)]);
        }
        var top = PortraitBitmap.Decode(Bmp(5, 4, 24, Colour, topDown: true))!;
        CollectionAssert.AreEqual(truecolour.Rgba, top.Rgba);
        var paletted = PortraitBitmap.Decode(Bmp(6, 3, 8, Colour))!;
        // index = (r + 3g + 5b) % 256; the palette maps an index to (red = i, green = i / 2, blue = 255 - i).
        var index = (Colour(2, 1).Item1 + Colour(2, 1).Item2 * 3 + Colour(2, 1).Item3 * 5) % 256;
        var at2 = (1 * 6 + 2) * 4;
        CollectionAssert.AreEqual(new[] { (byte)index, (byte)(index / 2), (byte)(255 - index), (byte)255 }, paletted.Rgba[at2..(at2 + 4)]);
    }

    [TestMethod]
    public void BitmapReaderRefusesWhatItDoesNotTake()
    {
        Assert.IsNull(PortraitBitmap.Decode(Bmp(4, 4, 24, Colour, compression: 1)));
        Assert.IsNull(PortraitBitmap.Decode([]));
        var good = Bmp(4, 4, 24, Colour);
        Assert.IsNull(PortraitBitmap.Decode(good[..^3]));
        var wrong = (byte[])good.Clone(); wrong[0] = (byte)'X';
        Assert.IsNull(PortraitBitmap.Decode(wrong));
        var sixteen = (byte[])good.Clone(); sixteen[28] = 16;
        Assert.IsNull(PortraitBitmap.Decode(sixteen));
    }

    [TestMethod]
    public void KeyPathsAreLowercaseBitmapNamesOfTheFolder()
    {
        Assert.AreEqual("portrait/hum1_b.bmp", HdPortraitKey.Path("Portrait\\HUM1_B.BMP"));
        Assert.AreEqual("portrait/hum1.bmp", HdPortraitKey.Path("hum1.bmp"));
        Assert.IsFalse(HdPortraitKey.TryPath("portrait/sub/hum1.bmp", out _));
        Assert.IsFalse(HdPortraitKey.TryPath("portrait/hum1.png", out _));
        Assert.IsFalse(HdPortraitKey.TryPath("portrait/hu m.bmp", out _));
        Assert.AreNotEqual(HdPortraitKey.Key("portrait/hum1.bmp"), HdPortraitKey.Key("portrait/hum1_b.bmp"));
        Assert.AreEqual(0, HdPortraitKey.Key("portrait/hum1.bmp").View);
    }

    private static byte[] Redraw(PortraitBitmapImage legacy, int scale, int tint = 9)
    {
        var rgba = new byte[legacy.Width * scale * legacy.Height * scale * 4];
        for (var y = 0; y < legacy.Height * scale; y++)
        for (var x = 0; x < legacy.Width * scale; x++)
        {
            var from = ((y / scale) * legacy.Width + x / scale) * 4; var to = (y * legacy.Width * scale + x) * 4;
            rgba[to] = (byte)(legacy.Rgba[from] + tint); rgba[to + 1] = legacy.Rgba[from + 1]; rgba[to + 2] = legacy.Rgba[from + 2]; rgba[to + 3] = 255;
        }
        return rgba;
    }

    private static (PortraitBitmapImage Large, PortraitBitmapImage Small) Bitmaps() =>
        (PortraitBitmap.Decode(Bmp(8, 8, 24, Colour))!, PortraitBitmap.Decode(Bmp(4, 4, 24, (x, y) => Colour(y, x)))!);

    private string WritePack(string name, int scale = 2, bool alsoSprite = false)
    {
        var directory = Path.Combine(_root, name);
        var (large, small) = Bitmaps();
        var result = HdInterfacePackWriter.Write(directory, "portraits-test", scale, [], DateTimeOffset.UnixEpoch,
            [new("portrait/hum1_b.bmp", 8, 8, large.Rgba, Redraw(large, scale)), new("portrait/hum1.bmp", 4, 4, small.Rgba, Redraw(small, scale))]);
        Assert.AreEqual(HdInterfaceWriteStatus.Written, result.Status, result.Detail);
        Assert.AreEqual(2, result.Entries);
        return directory;
    }

    [TestMethod]
    public void PortraitEntriesWriteOpenAndBindWithTheirLegacyBitmap()
    {
        var directory = WritePack("bind");
        var opened = HdPack.Open(directory);
        Assert.AreEqual(HdPackOpenStatus.Opened, opened.Status, opened.Detail);
        var pack = opened.Pack!;
        Assert.AreEqual(2, pack.Entries.Count(e => e.Kind == HdEntryKind.Portrait));
        Assert.AreEqual(2, pack.SpriteCount);
        Assert.AreEqual(2, pack.InterfaceSprites.Count());
        Assert.IsTrue(pack.VerifyIntegrity().Intact);

        var (large, small) = Bitmaps();
        var query = HdPortraitKey.Query("portrait/HUM1_B.bmp", large.Width, large.Height, large.Rgba);
        var bound = pack.ResolveSprite(query, large.Rgba);
        Assert.AreEqual(HdFrameStatus.Hd, bound.Status, bound.Invalid.ToString());
        Assert.AreEqual((16, 16), (bound.Width, bound.Height));
        CollectionAssert.AreEqual(Redraw(large, 2), bound.Rgba);
        Assert.AreEqual(HdFrameStatus.Hd, pack.ResolveSprite(HdPortraitKey.Query("portrait/hum1.bmp", 4, 4, small.Rgba), small.Rgba).Status);

        // A bitmap of the same name with other pixels (a mod, another version) is a changed source; a name the pack lacks is missing.
        var other = PortraitBitmap.Decode(Bmp(8, 8, 24, (x, y) => Colour(x + 1, y)))!;
        Assert.AreEqual(HdFrameStatus.SourceMismatch, pack.ResolveSprite(HdPortraitKey.Query("portrait/hum1_b.bmp", 8, 8, other.Rgba), other.Rgba).Status);
        Assert.AreEqual(HdFrameStatus.FrameMissing, pack.ResolveSprite(HdPortraitKey.Query("portrait/hum2_b.bmp", 8, 8, large.Rgba), large.Rgba).Status);
        // The small and the large bitmap are separate entries: the large key never binds the small image.
        Assert.AreEqual(HdFrameStatus.SourceMismatch, pack.ResolveSprite(HdPortraitKey.Query("portrait/hum1_b.bmp", 4, 4, small.Rgba), small.Rgba).Status);
    }

    [TestMethod]
    public void PortraitsCoexistWithInterfaceSpritesAndKeepTheirOwnReplacement()
    {
        var directory = WritePack("coexist");
        var (large, _) = Bitmaps();
        // A later portrait write replaces only the portraits.
        var replaced = HdInterfacePackWriter.Write(directory, "portraits-test", 2, [], DateTimeOffset.UnixEpoch,
            [new("portrait/hum1_b.bmp", 8, 8, large.Rgba, Redraw(large, 2, tint: 40))]);
        Assert.AreEqual(HdInterfaceWriteStatus.Written, replaced.Status, replaced.Detail);
        Assert.AreEqual(2, replaced.Replaced);
        var pack = HdPack.Open(directory).Pack!;
        Assert.AreEqual(1, pack.Entries.Count);
        // An interface write (no frames) leaves portraits in place only when it carries none: it replaces the sprites of the slice, which are none here.
        Assert.IsTrue(pack.VerifyIntegrity().Intact);
        Assert.AreEqual(HdFrameStatus.Hd, pack.ResolveSprite(HdPortraitKey.Query("portrait/hum1_b.bmp", 8, 8, large.Rgba), large.Rgba).Status);
    }

    [TestMethod]
    public void WriterRefusesAnImageThatIsNotOpaqueOrNotTheRightSize()
    {
        var (large, _) = Bitmaps();
        var translucent = Redraw(large, 2); translucent[3] = 200;
        var a = HdInterfacePackWriter.Write(Path.Combine(_root, "t"), "p", 2, [], DateTimeOffset.UnixEpoch, [new("portrait/hum1_b.bmp", 8, 8, large.Rgba, translucent)]);
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, a.Status);
        var wrong = HdInterfacePackWriter.Write(Path.Combine(_root, "w"), "p", 2, [], DateTimeOffset.UnixEpoch, [new("portrait/hum1_b.bmp", 8, 8, large.Rgba, new byte[16 * 8 * 4])]);
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, wrong.Status);
        var bad = HdInterfacePackWriter.Write(Path.Combine(_root, "b"), "p", 2, [], DateTimeOffset.UnixEpoch, [new("portrait/Hum 1.bmp", 8, 8, large.Rgba, Redraw(large, 2))]);
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, bad.Status);
    }

    [TestMethod]
    public void ManifestRefusesAPortraitWithAnotherKeyShape()
    {
        var directory = WritePack("refuse");
        var manifestPath = Path.Combine(directory, "manifest.json");
        var original = File.ReadAllText(manifestPath);
        foreach (var (from, to) in new[]
        {
            ("\"path\":\"portrait/hum1.bmp\"", "\"path\":\"portrait/Hum1.bmp\""),
            ("\"path\":\"portrait/hum1.bmp\"", "\"path\":\"art/interface/hum1.bmp\""),
            ("\"portrait\":{\"path\":\"portrait/hum1.bmp\"}", "\"portrait\":{\"path\":\"portrait/hum1.bmp\",\"x\":1}"),
        })
        {
            Assert.IsTrue(original.Contains(from, StringComparison.Ordinal), from);
            File.WriteAllText(manifestPath, original.Replace(from, to, StringComparison.Ordinal));
            Assert.AreEqual(HdPackOpenStatus.InvalidField, HdPack.Open(directory).Status, to);
        }
        // The same path twice is a duplicate key.
        File.WriteAllText(manifestPath, original.Replace("\"path\":\"portrait/hum1.bmp\"", "\"path\":\"portrait/hum1_b.bmp\"", StringComparison.Ordinal));
        Assert.AreEqual(HdPackOpenStatus.DuplicateKey, HdPack.Open(directory).Status);
    }

    [TestMethod]
    public void PlanParsesAndBuildNamesEveryProblem()
    {
        var plan = HdPortraitPlan.Parse(Encoding.UTF8.GetBytes("""{"kind":"arcanum-steamwoven-hd-portrait-plan","schema":1,"packId":"p","scale":2,"items":[{"bmp":"portrait/hum1_b.bmp","image":"a.png"},{"bmp":"portrait/none.bmp","image":"b.png"},{"bmp":"portrait/hum1.bmp","image":"c.png"}]}"""));
        Assert.AreEqual(3, plan.Items.Count);
        Assert.ThrowsExactly<FormatException>(() => HdPortraitPlan.Parse(Encoding.UTF8.GetBytes("""{"kind":"other","schema":1}""")));
        var (large, _) = Bitmaps();
        var bmp = Bmp(8, 8, 24, Colour);
        var build = HdPortraitImport.Build(plan, path => path == "portrait/hum1_b.bmp" ? bmp : path == "portrait/hum1.bmp" ? bmp : null,
            name => name == "a.png" ? new HdRgbImage(16, 16, new float[16 * 16 * 3]) : name == "c.png" ? new HdRgbImage(8, 8, new float[8 * 8 * 3]) : null);
        Assert.AreEqual(1, build.Portraits.Count);
        Assert.AreEqual(2, build.Problems.Count);
        StringAssert.Contains(build.Problems[0], "not in the game");
        StringAssert.Contains(build.Problems[1], "expected 16x16");
        Assert.IsFalse(build.Ok);
        Assert.AreEqual(255, build.Portraits[0].Rgba[3]);
        Assert.AreEqual(large.Rgba.Length, build.Portraits[0].LegacyRgba.Length);
    }
}
