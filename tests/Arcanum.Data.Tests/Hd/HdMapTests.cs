using System.Text.Json.Nodes;
using Arcanum.Data.Characters;
using Arcanum.Data.Hd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Hd;

/// <summary>ARC-RENDER-3079: the world-map key, the <c>worldmap</c> and <c>map-zoomed</c> pack entries (write, open, bind, refuse, coexist with the other kinds) and the world-map plan.
/// Synthetic pixels only.</summary>
[TestClass]
public sealed class HdMapTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-hdm-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // An uncompressed bottom-up 24-bit bitmap of the given colours.
    internal static byte[] Bmp24(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var stride = (width * 24 + 31) / 32 * 4;
        var data = new byte[stride * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var row = (height - 1 - y) * stride;
            var (r, g, b) = pixel(x, y);
            data[row + x * 3] = b; data[row + x * 3 + 1] = g; data[row + x * 3 + 2] = r;
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(54 + data.Length); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((short)1); writer.Write((short)24);
        writer.Write(0); writer.Write(data.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        writer.Write(data);
        return stream.ToArray();
    }

    private static (byte, byte, byte) Colour(int x, int y) => ((byte)(x * 3 + y), (byte)(y * 2 + x), (byte)(x ^ y));

    private static PortraitBitmapImage Tile(int seed = 0) => PortraitBitmap.Decode(Bmp24(250, 250, (x, y) => Colour(x + seed, y)))!;
    private static PortraitBitmapImage Zoomed() => PortraitBitmap.Decode(Bmp24(365, 365, (x, y) => Colour(y, x)))!;

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

    private static HdBuiltMapBitmap Built(HdEntryKind kind, string path, PortraitBitmapImage bitmap, int scale, int tint = 9) =>
        new(kind, path, bitmap.Width, bitmap.Height, bitmap.Rgba, Redraw(bitmap, scale, tint));

    private string WritePack(string name, int scale = 2)
    {
        var directory = Path.Combine(_root, name);
        var result = HdInterfacePackWriter.Write(directory, "maps-test", scale, [], DateTimeOffset.UnixEpoch, maps:
            [Built(HdEntryKind.WorldMap, "worldmap/smallmapchunks001.bmp", Tile(), scale), Built(HdEntryKind.WorldMap, "worldmap/smallmapchunks002.bmp", Tile(5), scale),
             Built(HdEntryKind.MapZoomed, "worldmap/map_zoomed.bmp", Zoomed(), scale)]);
        Assert.AreEqual(HdInterfaceWriteStatus.Written, result.Status, result.Detail);
        Assert.AreEqual(3, result.Entries);
        return directory;
    }

    [TestMethod]
    public void KeyPathsAreLowercaseBitmapNamesOfTheWorldmapFolder()
    {
        Assert.AreEqual("worldmap/smallmapchunks001.bmp", HdMapKey.Path("WorldMap\\SmallMapChunks001.BMP"));
        Assert.AreEqual("worldmap/map_zoomed.bmp", HdMapKey.Path("Map_Zoomed.bmp"));
        Assert.IsFalse(HdMapKey.TryPath("worldmap/sub/a.bmp", out _));
        Assert.IsFalse(HdMapKey.TryPath("worldmap/a.png", out _));
        Assert.IsFalse(HdMapKey.TryPath("worldmap/a b.bmp", out _));
        Assert.IsFalse(HdMapKey.TryPath("worldmap/" + new string('a', 49) + ".bmp", out _));
        Assert.AreNotEqual(HdMapKey.Key("worldmap/smallmapchunks001.bmp"), HdMapKey.Key("worldmap/smallmapchunks002.bmp"));
        Assert.AreNotEqual(HdMapKey.Key("worldmap/hum1.bmp"), HdPortraitKey.Key("portrait/hum1.bmp"));
        Assert.AreEqual(0, HdMapKey.Key("worldmap/smallmapchunks001.bmp").View);
        Assert.AreEqual(HdEntryKind.WorldMap, HdMapKey.KindOf(250, 250));
        Assert.AreEqual(HdEntryKind.MapZoomed, HdMapKey.KindOf(365, 365));
        Assert.IsNull(HdMapKey.KindOf(250, 365));
    }

    [TestMethod]
    public void WorldMapEntriesWriteOpenAndBindWithTheirLegacyBitmapAtTheScaleTimesItsSize()
    {
        var directory = WritePack("bind");
        var opened = HdPack.Open(directory);
        Assert.AreEqual(HdPackOpenStatus.Opened, opened.Status, opened.Detail);
        var pack = opened.Pack!;
        Assert.AreEqual(2, pack.Entries.Count(e => e.Kind == HdEntryKind.WorldMap));
        Assert.AreEqual(1, pack.Entries.Count(e => e.Kind == HdEntryKind.MapZoomed));
        Assert.AreEqual(3, pack.SpriteCount);
        Assert.AreEqual(3, pack.InterfaceSprites.Count());
        Assert.IsTrue(pack.VerifyIntegrity().Intact);
        Assert.AreEqual("worldmap/map_zoomed.bmp", pack.Entries.Single(e => e.Kind == HdEntryKind.MapZoomed).MapPath);

        var tile = Tile(); var zoomed = Zoomed();
        var bound = pack.ResolveSprite(HdMapKey.Query("worldmap/SmallMapChunks001.bmp", 250, 250, tile.Rgba), tile.Rgba);
        Assert.AreEqual(HdFrameStatus.Hd, bound.Status, bound.Invalid.ToString());
        Assert.AreEqual((500, 500), (bound.Width, bound.Height));
        CollectionAssert.AreEqual(Redraw(tile, 2), bound.Rgba);
        var bigger = pack.ResolveSprite(HdMapKey.Query("worldmap/map_zoomed.bmp", 365, 365, zoomed.Rgba), zoomed.Rgba);
        Assert.AreEqual(HdFrameStatus.Hd, bigger.Status, bigger.Invalid.ToString());
        Assert.AreEqual((730, 730), (bigger.Width, bigger.Height), "a continent picture of another size is the pack's scale times 365");

        // A bitmap of the same name with other pixels (a mod, another version) is a changed source; a name the pack lacks is missing.
        var other = Tile(1);
        Assert.AreEqual(HdFrameStatus.SourceMismatch, pack.ResolveSprite(HdMapKey.Query("worldmap/smallmapchunks001.bmp", 250, 250, other.Rgba), other.Rgba).Status);
        Assert.AreEqual(HdFrameStatus.FrameMissing, pack.ResolveSprite(HdMapKey.Query("worldmap/smallmapchunks064.bmp", 250, 250, tile.Rgba), tile.Rgba).Status);
        // The tile of another name never binds the first one's image.
        Assert.AreEqual(HdFrameStatus.SourceMismatch, pack.ResolveSprite(HdMapKey.Query("worldmap/smallmapchunks002.bmp", 250, 250, tile.Rgba), tile.Rgba).Status);
    }

    [TestMethod]
    public void WorldMapEntriesBindAtScaleFourToo()
    {
        var pack = HdPack.Open(WritePack("four", scale: 4)).Pack!;
        var tile = Tile();
        var bound = pack.ResolveSprite(HdMapKey.Query("worldmap/smallmapchunks001.bmp", 250, 250, tile.Rgba), tile.Rgba);
        Assert.AreEqual(HdFrameStatus.Hd, bound.Status, bound.Invalid.ToString());
        Assert.AreEqual((1000, 1000), (bound.Width, bound.Height));
    }

    private HdPackOpenStatus OpenAfter(string name, Action<JsonObject> change)
    {
        var directory = WritePack(name);
        var path = Path.Combine(directory, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        change(manifest);
        File.WriteAllText(path, manifest.ToJsonString());
        return HdPack.Open(directory).Status;
    }

    private static JsonObject FirstOf(JsonObject manifest, string kind) => manifest["entries"]!.AsArray().Select(e => e!.AsObject()).First(e => (string?)e["kind"] == kind);

    [TestMethod]
    public void OpenRefusesAnEntryThatBreaksTheMapRules()
    {
        Assert.AreEqual(HdPackOpenStatus.Opened, OpenAfter("same", _ => { }));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, OpenAfter("size", m => FirstOf(m, "worldmap")["legacy"]!["width"] = 251), "a tile is 250 x 250");
        Assert.AreEqual(HdPackOpenStatus.InvalidField, OpenAfter("zoomed", m => FirstOf(m, "map-zoomed")["legacy"]!["height"] = 250), "the continent picture is 365 x 365");
        Assert.AreEqual(HdPackOpenStatus.InvalidField, OpenAfter("upper", m => FirstOf(m, "worldmap")["worldmap"]!["path"] = "worldmap/SmallMapChunks001.bmp"));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, OpenAfter("folder", m => FirstOf(m, "worldmap")["worldmap"]!["path"] = "portrait/smallmapchunks001.bmp"));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, OpenAfter("extra", m => FirstOf(m, "worldmap")["worldmap"]!["ratio"] = 4));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, OpenAfter("property", m => { var e = FirstOf(m, "map-zoomed"); e["worldmap"] = e["map-zoomed"]!.DeepClone(); e.Remove("map-zoomed"); }));
        Assert.AreEqual(HdPackOpenStatus.DuplicateKey, OpenAfter("duplicate", m =>
            FirstOf(m, "map-zoomed")["map-zoomed"]!["path"] = "worldmap/smallmapchunks001.bmp"), "one path, one entry");
    }

    [TestMethod]
    public void MapWritesAndOtherWritesLeaveEachOthersEntriesAlone()
    {
        const int Scale = 2;
        var directory = Path.Combine(_root, "coexist");
        var spriteRgba = new byte[4 * 4 * 4];
        for (var i = 0; i < 16; i++) { spriteRgba[i * 4] = (byte)(i * 9); spriteRgba[i * 4 + 1] = 40; spriteRgba[i * 4 + 2] = 90; spriteRgba[i * 4 + 3] = 255; }
        var art = new HdInterfaceArt("synthetic.art", HdInterfaceKey.Identity("synthetic.art", new byte[160]), [new HdLegacyRaster(4, 4, spriteRgba)]);
        HdBuiltInterfaceFrame Frame() => new(art, 0, 4, 4, Redraw(new PortraitBitmapImage(4, 4, spriteRgba), Scale), new(1, 1, 0, 0, 1));
        var portrait = PortraitBitmap.Decode(HdMapTests.Bmp24(4, 4, Colour))!;

        Assert.AreEqual(HdInterfaceWriteStatus.Written, HdInterfacePackWriter.Write(directory, "mixed", Scale, [Frame()], DateTimeOffset.UnixEpoch).Status);
        Assert.AreEqual(HdInterfaceWriteStatus.Written, HdInterfacePackWriter.Write(directory, "mixed", Scale, [], DateTimeOffset.UnixEpoch,
            [new("portrait/hum1.bmp", 4, 4, portrait.Rgba, Redraw(portrait, Scale))]).Status);
        var withMaps = HdInterfacePackWriter.Write(directory, "mixed", Scale, [], DateTimeOffset.UnixEpoch, maps:
            [Built(HdEntryKind.WorldMap, "worldmap/smallmapchunks001.bmp", Tile(), Scale), Built(HdEntryKind.MapZoomed, "worldmap/map_zoomed.bmp", Zoomed(), Scale)]);
        Assert.AreEqual(HdInterfaceWriteStatus.Written, withMaps.Status, withMaps.Detail);
        Assert.AreEqual(0, withMaps.Replaced, "the maps replaced nothing: the sprite and the portrait stay");
        var pack = HdPack.Open(directory).Pack!;
        Assert.AreEqual((1, 1, 1, 1), (pack.Entries.Count(e => e.Kind == HdEntryKind.Sprite), pack.Entries.Count(e => e.Kind == HdEntryKind.Portrait),
            pack.Entries.Count(e => e.Kind == HdEntryKind.WorldMap), pack.Entries.Count(e => e.Kind == HdEntryKind.MapZoomed)));
        var before = pack.Entries.ToDictionary(e => e.Kind, e => e.Sha256);

        // A map write that names one tile replaces that tile only.
        var replaced = HdInterfacePackWriter.Write(directory, "mixed", Scale, [], DateTimeOffset.UnixEpoch, maps: [Built(HdEntryKind.WorldMap, "worldmap/smallmapchunks001.bmp", Tile(), Scale, tint: 40)]);
        Assert.AreEqual(HdInterfaceWriteStatus.Written, replaced.Status, replaced.Detail);
        Assert.AreEqual(1, replaced.Replaced);
        pack = HdPack.Open(directory).Pack!;
        var after = pack.Entries.ToDictionary(e => e.Kind, e => e.Sha256);
        Assert.AreNotEqual(before[HdEntryKind.WorldMap], after[HdEntryKind.WorldMap]);
        foreach (var kind in new[] { HdEntryKind.Sprite, HdEntryKind.Portrait, HdEntryKind.MapZoomed }) Assert.AreEqual(before[kind], after[kind], kind.ToString());

        // A write of interface frames replaces the sprites and leaves the portrait and the maps.
        var frames = HdInterfacePackWriter.Write(directory, "mixed", Scale, [Frame()], DateTimeOffset.UnixEpoch);
        Assert.AreEqual(HdInterfaceWriteStatus.Written, frames.Status, frames.Detail);
        pack = HdPack.Open(directory).Pack!;
        Assert.AreEqual((1, 1, 1, 1), (pack.Entries.Count(e => e.Kind == HdEntryKind.Sprite), pack.Entries.Count(e => e.Kind == HdEntryKind.Portrait),
            pack.Entries.Count(e => e.Kind == HdEntryKind.WorldMap), pack.Entries.Count(e => e.Kind == HdEntryKind.MapZoomed)));
        Assert.IsTrue(pack.VerifyIntegrity().Intact);
    }

    [TestMethod]
    public void WriterRefusesABadMapItem()
    {
        var directory = Path.Combine(_root, "bad");
        var tile = Tile();
        HdInterfaceWriteResult Write(params HdBuiltMapBitmap[] maps) => HdInterfacePackWriter.Write(directory, "bad", 2, [], DateTimeOffset.UnixEpoch, maps: maps);
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, Write(Built(HdEntryKind.MapZoomed, "worldmap/a.bmp", tile, 2)).Status, "a tile is not a continent picture");
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, Write(Built(HdEntryKind.WorldMap, "worldmap/A.bmp", tile, 2)).Status, "an upper-case path");
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, Write(Built(HdEntryKind.WorldMap, "worldmap/a.bmp", tile, 2), Built(HdEntryKind.WorldMap, "worldmap/a.bmp", tile, 2)).Status, "twice");
        var translucent = Built(HdEntryKind.WorldMap, "worldmap/a.bmp", tile, 2); translucent.Rgba[3] = 10;
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, Write(translucent).Status, "a map is opaque");
        Assert.IsFalse(File.Exists(Path.Combine(directory, "manifest.json")), "nothing was written");
    }

    private static byte[] PlanJson(int scale, params (string Bmp, string Image)[] items) =>
        System.Text.Encoding.UTF8.GetBytes(new JsonObject
        {
            ["schema"] = 1, ["kind"] = HdMapPlan.Kind, ["packId"] = "maps-plan", ["scale"] = scale,
            ["items"] = new JsonArray([.. items.Select(i => (JsonNode)new JsonObject { ["bmp"] = i.Bmp, ["image"] = i.Image })]),
        }.ToJsonString());

    [TestMethod]
    public void PlanParsesAndBuildsOnlyWhatMatchesTheOriginalBitmapSize()
    {
        var tileBmp = Bmp24(250, 250, Colour); var zoomedBmp = Bmp24(365, 365, (x, y) => Colour(y, x));
        var plan = HdMapPlan.Parse(PlanJson(2, ("worldmap/smallmapchunks001.bmp", "a.png"), ("worldmap/map_zoomed.bmp", "z.png")));
        Assert.AreEqual((2, "maps-plan", 2), (plan.Scale, plan.PackId, plan.Items.Count));
        HdRgbImage Image(int side) => new(side, side, new float[side * side * 3]);
        byte[]? Legacy(string path) => path == "worldmap/smallmapchunks001.bmp" ? tileBmp : path == "worldmap/map_zoomed.bmp" ? zoomedBmp : null;
        HdRgbImage? Read(string name) => name == "a.png" ? Image(500) : name == "z.png" ? Image(730) : null;
        var good = HdMapImport.Build(plan, Legacy, Read);
        Assert.IsTrue(good.Ok, string.Join("; ", good.Problems));
        Assert.AreEqual(2, good.Maps.Count);
        Assert.AreEqual(HdEntryKind.WorldMap, good.Maps.Single(m => m.Path.EndsWith("001.bmp", StringComparison.Ordinal)).Kind);
        Assert.AreEqual(HdEntryKind.MapZoomed, good.Maps.Single(m => m.Path.EndsWith("zoomed.bmp", StringComparison.Ordinal)).Kind);

        // A redraw of the wrong size, an original that is not in the game, an odd-sized original, a bad name and a repeat are each reported, and nothing is built for them.
        var wrongSize = HdMapImport.Build(HdMapPlan.Parse(PlanJson(2, ("worldmap/smallmapchunks001.bmp", "z.png"))), Legacy, Read);
        Assert.IsFalse(wrongSize.Ok); Assert.AreEqual(0, wrongSize.Maps.Count);
        Assert.IsFalse(HdMapImport.Build(HdMapPlan.Parse(PlanJson(2, ("worldmap/absent.bmp", "a.png"))), Legacy, Read).Ok);
        Assert.IsFalse(HdMapImport.Build(HdMapPlan.Parse(PlanJson(2, ("worldmap/smallmapchunks001.bmp", "a.png"))), _ => Bmp24(100, 100, Colour), Read).Ok);
        Assert.IsFalse(HdMapImport.Build(HdMapPlan.Parse(PlanJson(2, ("portrait/hum1.bmp", "a.png"))), Legacy, Read).Ok);
        Assert.IsFalse(HdMapImport.Build(HdMapPlan.Parse(PlanJson(2, ("worldmap/smallmapchunks001.bmp", "a.png"), ("worldmap/smallmapchunks001.bmp", "a.png"))), Legacy, Read).Ok);
        Assert.IsFalse(HdMapImport.Build(HdMapPlan.Parse(PlanJson(2, ("worldmap/smallmapchunks001.bmp", "missing.png"))), Legacy, Read).Ok);
        Assert.ThrowsExactly<FormatException>(() => HdMapPlan.Parse("{\"schema\":1,\"kind\":\"other\",\"packId\":\"x\",\"scale\":2,\"items\":[]}"u8));
    }
}
