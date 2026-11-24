using System.Text.Json.Nodes;
using Arcanum.Data.Characters;
using Arcanum.Data.Hd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Hd;

/// <summary>ARC-RENDER-3106: the town-map tile key, the <c>townmap</c> pack entries (write, open, bind, refuse, coexist with the other kinds, left out of the device cache's list) and the town-map plan.
/// Synthetic pixels only.</summary>
[TestClass]
public sealed class HdTownMapTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-hdt-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static (byte, byte, byte) Colour(int x, int y) => ((byte)(x * 3 + y), (byte)(y * 2 + x), (byte)(x ^ y));

    private static PortraitBitmapImage Tile(int seed = 0) => PortraitBitmap.Decode(HdMapTests.Bmp24(64, 32, (x, y) => Colour(x + seed, y)))!;

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

    private static HdBuiltTownTile Built(string path, PortraitBitmapImage tile, int scale, int tint = 9) => new(path, tile.Width, tile.Height, tile.Rgba, () => Redraw(tile, scale, tint));

    private string WritePack(string name, int scale = 2)
    {
        var directory = Path.Combine(_root, name);
        var result = HdInterfacePackWriter.Write(directory, "town-test", scale, [], DateTimeOffset.UnixEpoch, townTiles:
            [Built("townmap/ashbury/ashbury000000.bmp", Tile(), scale), Built("townmap/ashbury/ashbury000748.bmp", Tile(5), scale),
             Built("townmap/ashbury crematorium - lev 1/ashbury crematorium - lev 1000001.bmp", Tile(9), scale)]);
        Assert.AreEqual(HdInterfaceWriteStatus.Written, result.Status, result.Detail);
        Assert.AreEqual(3, result.Entries);
        return directory;
    }

    [TestMethod]
    public void KeyPathsAreTheLowercaseTileFilesOfATownFolder()
    {
        Assert.AreEqual("townmap/ashbury/ashbury000748.bmp", HdTownMapKey.Path("Ashbury", 748));
        Assert.AreEqual("townmap/ashbury crematorium - lev 1/ashbury crematorium - lev 1000018.bmp", HdTownMapKey.Path("Ashbury Crematorium - Lev 1", 18));
        Assert.IsTrue(HdTownMapKey.TryParse("TownMap\\Ashbury\\Ashbury000748.BMP", out var town, out var index));
        Assert.AreEqual(("ashbury", 748), (town, index));
        Assert.IsTrue(HdTownMapKey.TryPath("townmap/bates tunnel/bates tunnel000003.bmp", out var path));
        Assert.AreEqual("townmap/bates tunnel/bates tunnel000003.bmp", path);
        Assert.IsTrue(HdTownMapKey.TryPath("townmap/o'brien/o'brien000001.bmp", out _), "an apostrophe is a shipped name character");
        foreach (var bad in new[]
        {
            "townmap/ashbury/ashbury748.bmp", "townmap/ashbury/ashbury0007480.bmp", "townmap/ashbury/other000748.bmp", "townmap/ashbury/ashbury000748.png", "townmap/ashbury/sub/ashbury000748.bmp",
            "worldmap/ashbury/ashbury000748.bmp", "townmap/ashbury /ashbury 000748.bmp", "townmap//000748.bmp", "townmap/a#b/a#b000748.bmp", "townmap/" + new string('a', 41) + "/" + new string('a', 41) + "000748.bmp",
            "townmap/ashbury/ashbury00074x.bmp", "townmap/.a/.a000748.bmp",
        }) Assert.IsFalse(HdTownMapKey.TryPath(bad, out _), bad);
        Assert.AreNotEqual(HdTownMapKey.Key("townmap/ashbury/ashbury000001.bmp"), HdTownMapKey.Key("townmap/ashbury/ashbury000002.bmp"));
        Assert.AreNotEqual(HdTownMapKey.Key("townmap/hum1/hum1000001.bmp"), HdMapKey.Key("worldmap/hum1.bmp"));
        Assert.AreEqual(0, HdTownMapKey.Key("townmap/ashbury/ashbury000001.bmp").View);
        Assert.AreEqual("ashbury-crematorium---lev-1", HdTownMapKey.FileTown("ashbury crematorium - lev 1"));
        Assert.ThrowsExactly<ArgumentException>(() => HdTownMapKey.Query("townmap/ashbury/ashbury000001.bmp", 250, 250, new byte[250 * 250 * 4]));
    }

    [TestMethod]
    public void TownEntriesWriteOpenAndBindWithTheirLegacyTileAtTheScaleTimesItsSize()
    {
        foreach (var scale in new[] { 2, 4 })
        {
            var opened = HdPack.Open(WritePack("bind" + scale, scale));
            Assert.AreEqual(HdPackOpenStatus.Opened, opened.Status, opened.Detail);
            var pack = opened.Pack!;
            Assert.AreEqual(3, pack.Entries.Count(e => e.Kind == HdEntryKind.TownMap));
            Assert.AreEqual(3, pack.SpriteCount);
            Assert.AreEqual(0, pack.InterfaceSprites.Count(), "the thousands of tiles of a town are not converted into the device cache's mip chains");
            Assert.IsTrue(pack.VerifyIntegrity().Intact);
            Assert.AreEqual("townmap/ashbury/ashbury000748.bmp", pack.Entries.Single(e => e.MapPath!.EndsWith("748.bmp", StringComparison.Ordinal)).MapPath);

            var tile = Tile(5);
            var bound = pack.ResolveSprite(HdTownMapKey.Query("townmap/Ashbury/Ashbury000748.bmp", 64, 32, tile.Rgba), tile.Rgba);
            Assert.AreEqual(HdFrameStatus.Hd, bound.Status, bound.Invalid.ToString());
            Assert.AreEqual((64 * scale, 32 * scale), (bound.Width, bound.Height), "256 x 128 at the 4x pack: render scale 1.0");
            CollectionAssert.AreEqual(Redraw(tile, scale), bound.Rgba);

            // Another tile's pixels under this name are a changed source; a name the pack lacks is missing; the tile of another name never binds this image.
            var other = Tile(6);
            Assert.AreEqual(HdFrameStatus.SourceMismatch, pack.ResolveSprite(HdTownMapKey.Query("townmap/ashbury/ashbury000748.bmp", 64, 32, other.Rgba), other.Rgba).Status);
            Assert.AreEqual(HdFrameStatus.FrameMissing, pack.ResolveSprite(HdTownMapKey.Query("townmap/ashbury/ashbury000749.bmp", 64, 32, tile.Rgba), tile.Rgba).Status);
            Assert.AreEqual(HdFrameStatus.SourceMismatch, pack.ResolveSprite(HdTownMapKey.Query("townmap/ashbury/ashbury000000.bmp", 64, 32, tile.Rgba), tile.Rgba).Status);
        }
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

    private static JsonObject First(JsonObject manifest) => manifest["entries"]!.AsArray().Select(e => e!.AsObject()).First(e => (string?)e["kind"] == "townmap");

    [TestMethod]
    public void OpenRefusesAnEntryThatBreaksTheTownRules()
    {
        Assert.AreEqual(HdPackOpenStatus.Opened, OpenAfter("same", _ => { }));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, OpenAfter("size", m => First(m)["legacy"]!["width"] = 65), "a tile is 64 x 32 (another size is refused, the layout never sees it)");
        Assert.AreEqual(HdPackOpenStatus.InvalidField, OpenAfter("height", m => First(m)["legacy"]!["height"] = 64));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, OpenAfter("upper", m => First(m)["townmap"]!["path"] = "townmap/Ashbury/Ashbury000000.bmp"));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, OpenAfter("folder", m => First(m)["townmap"]!["path"] = "worldmap/ashbury/ashbury000000.bmp"));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, OpenAfter("name", m => First(m)["townmap"]!["path"] = "townmap/ashbury/other000000.bmp"));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, OpenAfter("extra", m => First(m)["townmap"]!["scale"] = 1.0), "no per-entry scale: the pack's scale is the ratio");
        Assert.AreEqual(HdPackOpenStatus.InvalidField, OpenAfter("property", m => { var e = First(m); e["worldmap"] = e["townmap"]!.DeepClone(); e.Remove("townmap"); }));
        Assert.AreEqual(HdPackOpenStatus.DuplicateKey, OpenAfter("duplicate", m =>
        {
            var entries = m["entries"]!.AsArray().Select(e => e!.AsObject()).Where(e => (string?)e["kind"] == "townmap").ToArray();
            entries[1]["townmap"]!["path"] = entries[0]["townmap"]!["path"]!.GetValue<string>();
        }), "one path, one entry");
    }

    [TestMethod]
    public void TownWritesAndOtherWritesLeaveEachOthersEntriesAlone()
    {
        const int Scale = 2;
        var directory = Path.Combine(_root, "coexist");
        var spriteRgba = new byte[4 * 4 * 4];
        for (var i = 0; i < 16; i++) { spriteRgba[i * 4] = (byte)(i * 9); spriteRgba[i * 4 + 1] = 40; spriteRgba[i * 4 + 2] = 90; spriteRgba[i * 4 + 3] = 255; }
        var art = new HdInterfaceArt("synthetic.art", HdInterfaceKey.Identity("synthetic.art", new byte[160]), [new HdLegacyRaster(4, 4, spriteRgba)]);
        HdBuiltInterfaceFrame Frame() => new(art, 0, 4, 4, Redraw(new PortraitBitmapImage(4, 4, spriteRgba), Scale), new(1, 1, 0, 0, 1));
        var map = PortraitBitmap.Decode(HdMapTests.Bmp24(250, 250, Colour))!;

        Assert.AreEqual(HdInterfaceWriteStatus.Written, HdInterfacePackWriter.Write(directory, "mixed", Scale, [Frame()], DateTimeOffset.UnixEpoch).Status);
        Assert.AreEqual(HdInterfaceWriteStatus.Written, HdInterfacePackWriter.Write(directory, "mixed", Scale, [], DateTimeOffset.UnixEpoch, maps:
            [new(HdEntryKind.WorldMap, "worldmap/smallmapchunks001.bmp", 250, 250, map.Rgba, Redraw(map, Scale))]).Status);
        var withTown = HdInterfacePackWriter.Write(directory, "mixed", Scale, [], DateTimeOffset.UnixEpoch, townTiles:
            [Built("townmap/ashbury/ashbury000000.bmp", Tile(), Scale), Built("townmap/ashbury/ashbury000001.bmp", Tile(2), Scale)]);
        Assert.AreEqual(HdInterfaceWriteStatus.Written, withTown.Status, withTown.Detail);
        Assert.AreEqual(0, withTown.Replaced, "the tiles replaced nothing: the sprite and the world-map tile stay");
        var pack = HdPack.Open(directory).Pack!;
        Assert.AreEqual((1, 1, 2), (pack.Entries.Count(e => e.Kind == HdEntryKind.Sprite), pack.Entries.Count(e => e.Kind == HdEntryKind.WorldMap), pack.Entries.Count(e => e.Kind == HdEntryKind.TownMap)));
        var before = pack.Entries.Where(e => e.Kind != HdEntryKind.TownMap).ToDictionary(e => e.Kind, e => e.Sha256);

        // A write that names one tile replaces that tile only.
        var replaced = HdInterfacePackWriter.Write(directory, "mixed", Scale, [], DateTimeOffset.UnixEpoch, townTiles: [Built("townmap/ashbury/ashbury000001.bmp", Tile(2), Scale, tint: 40)]);
        Assert.AreEqual(HdInterfaceWriteStatus.Written, replaced.Status, replaced.Detail);
        Assert.AreEqual(1, replaced.Replaced);
        pack = HdPack.Open(directory).Pack!;
        Assert.AreEqual(2, pack.Entries.Count(e => e.Kind == HdEntryKind.TownMap));
        foreach (var kind in before.Keys) Assert.AreEqual(before[kind], pack.Entries.Single(e => e.Kind == kind).Sha256, kind.ToString());

        // A write of interface frames and a write of map bitmaps leave the town tiles.
        Assert.AreEqual(HdInterfaceWriteStatus.Written, HdInterfacePackWriter.Write(directory, "mixed", Scale, [Frame()], DateTimeOffset.UnixEpoch).Status);
        Assert.AreEqual(HdInterfaceWriteStatus.Written, HdInterfacePackWriter.Write(directory, "mixed", Scale, [], DateTimeOffset.UnixEpoch, maps:
            [new(HdEntryKind.WorldMap, "worldmap/smallmapchunks001.bmp", 250, 250, map.Rgba, Redraw(map, Scale, 30))]).Status);
        pack = HdPack.Open(directory).Pack!;
        Assert.AreEqual((1, 1, 2), (pack.Entries.Count(e => e.Kind == HdEntryKind.Sprite), pack.Entries.Count(e => e.Kind == HdEntryKind.WorldMap), pack.Entries.Count(e => e.Kind == HdEntryKind.TownMap)));
        Assert.IsTrue(pack.VerifyIntegrity().Intact);
    }

    [TestMethod]
    public void WriterRefusesABadTownItem()
    {
        var directory = Path.Combine(_root, "bad");
        var tile = Tile();
        HdInterfaceWriteResult Write(params HdBuiltTownTile[] tiles) => HdInterfacePackWriter.Write(directory, "bad", 2, [], DateTimeOffset.UnixEpoch, townTiles: tiles);
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, Write(Built("townmap/Ashbury/ashbury000000.bmp", tile, 2)).Status, "an upper-case path");
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, Write(Built("townmap/ashbury/ashbury0.bmp", tile, 2)).Status, "a tile number of other than six digits");
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, Write(Built("townmap/ashbury/ashbury000000.bmp", tile, 2), Built("townmap/ashbury/ashbury000000.bmp", tile, 2)).Status, "twice");
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, Write(Built("townmap/a b/a b000001.bmp", tile, 2), Built("townmap/a-b/a-b000001.bmp", tile, 2)).Status, "two towns that come out as one file name");
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, Write(new HdBuiltTownTile("townmap/ashbury/ashbury000000.bmp", 250, 250, new byte[250 * 250 * 4], () => new byte[500 * 500 * 4])).Status, "a tile is 64 x 32");
        var translucent = Redraw(tile, 2); translucent[3] = 10;
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, Write(new HdBuiltTownTile("townmap/ashbury/ashbury000000.bmp", 64, 32, tile.Rgba, () => translucent)).Status, "a tile is opaque");
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, Write(new HdBuiltTownTile("townmap/ashbury/ashbury000000.bmp", 64, 32, tile.Rgba, () => new byte[10])).Status, "a redraw of another size");
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, Write(new HdBuiltTownTile("townmap/ashbury/ashbury000000.bmp", 64, 32, tile.Rgba, () => null)).Status, "a redraw that does not read");
        Assert.IsFalse(File.Exists(Path.Combine(directory, "manifest.json")), "nothing was written");
    }

    private static byte[] PlanJson(int scale, params (string Bmp, string Image)[] items) =>
        System.Text.Encoding.UTF8.GetBytes(new JsonObject
        {
            ["schema"] = 1, ["kind"] = HdTownMapPlan.Kind, ["packId"] = "town-plan", ["scale"] = scale,
            ["items"] = new JsonArray([.. items.Select(i => (JsonNode)new JsonObject { ["bmp"] = i.Bmp, ["image"] = i.Image })]),
        }.ToJsonString());

    [TestMethod]
    public void PlanParsesAndBuildsOnlyWhatMatchesTheOriginalTileSize()
    {
        var tileBmp = HdMapTests.Bmp24(64, 32, Colour);
        var plan = HdTownMapPlan.Parse(PlanJson(4, ("townmap/ashbury/ashbury000748.bmp", "a.png"), ("townmap/ashbury/ashbury000749.bmp", "b.png")));
        Assert.AreEqual((4, "town-plan", 2), (plan.Scale, plan.PackId, plan.Items.Count));
        HdRgbImage Image(int width, int height) => new(width, height, new float[width * height * 3]);
        byte[]? Legacy(string path) => path.StartsWith("townmap/ashbury/", StringComparison.Ordinal) ? tileBmp : null;
        HdRgbImage? Read(string name) => name is "a.png" or "b.png" ? Image(256, 128) : name == "wide.png" ? Image(256, 256) : null;
        var good = HdTownMapImport.Build(plan, Legacy, Read);
        Assert.IsTrue(good.Ok, string.Join("; ", good.Problems));
        Assert.AreEqual(2, good.Tiles.Count);
        Assert.AreEqual(256 * 128 * 4, good.Tiles[0].Redraw()!.Length);
        Assert.IsTrue(good.Tiles[0].Redraw()!.Where((_, i) => i % 4 == 3).All(a => a == 255), "written opaque");

        // A redraw of the wrong size (a 0.5 render for a 4x plan), an original that is not in the game, an odd-sized original, a bad name, a repeat and an unread image are each reported.
        var wrongSize = HdTownMapImport.Build(HdTownMapPlan.Parse(PlanJson(4, ("townmap/ashbury/ashbury000748.bmp", "wide.png"))), Legacy, Read);
        Assert.IsFalse(wrongSize.Ok); Assert.AreEqual(0, wrongSize.Tiles.Count);
        Assert.IsFalse(HdTownMapImport.Build(HdTownMapPlan.Parse(PlanJson(2, ("townmap/ashbury/ashbury000748.bmp", "a.png"))), Legacy, Read).Ok, "128 x 64 expected at a 2x plan");
        Assert.IsFalse(HdTownMapImport.Build(HdTownMapPlan.Parse(PlanJson(4, ("townmap/absent/absent000001.bmp", "a.png"))), Legacy, Read).Ok);
        Assert.IsFalse(HdTownMapImport.Build(HdTownMapPlan.Parse(PlanJson(4, ("townmap/ashbury/ashbury000748.bmp", "a.png"))), _ => HdMapTests.Bmp24(100, 100, Colour), Read).Ok);
        Assert.IsFalse(HdTownMapImport.Build(HdTownMapPlan.Parse(PlanJson(4, ("worldmap/smallmapchunks001.bmp", "a.png"))), Legacy, Read).Ok);
        Assert.IsFalse(HdTownMapImport.Build(HdTownMapPlan.Parse(PlanJson(4, ("townmap/ashbury/ashbury000748.bmp", "a.png"), ("townmap/ashbury/ashbury000748.bmp", "a.png"))), Legacy, Read).Ok);
        Assert.IsFalse(HdTownMapImport.Build(HdTownMapPlan.Parse(PlanJson(4, ("townmap/ashbury/ashbury000748.bmp", "missing.png"))), Legacy, Read).Ok);
        Assert.ThrowsExactly<FormatException>(() => HdTownMapPlan.Parse("{\"schema\":1,\"kind\":\"other\",\"packId\":\"x\",\"scale\":4,\"items\":[]}"u8));
    }
}
