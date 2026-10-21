using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Lighting;

// ARC-RENDER-012: stationary sector lights. Literal 48-byte wires, independently authored ART/DAT bytes and
// hand-computed colours; no production helper computes an expected value.
[TestClass]
public sealed class StationaryLightTests
{
    private static readonly AreaSectorCoordinate Sector = new(32, 31); // world cells x 2048..2111, y 1984..2047
    private const long Packed = 2080374816;

    // Invented values throughout; no word is taken from an original record.
    private static int[] Wire(long x = 2060, long y = 2000, int ox = 11, int oy = -7, uint flags = 0, uint art = 0x90280000, uint rbg = 0x00332211,
        int owner = 0, uint tint = 0x00aabbcc, uint palette = 0xdeadbeef, uint padding = 1)
        => [owner, 0, (int)x, (int)y, ox, oy, (int)flags, (int)art, (int)rbg, (int)tint, (int)palette, (int)padding];

    [TestMethod]
    public void Decode_reads_absolute_location_signed_offsets_flags_art_and_rbg_colour()
    {
        Assert.AreEqual(LightingFailure.None, StationaryLightReader.TryDecode(Sector, 2, Wire(ox: -4, oy: 9, flags: 10), out var light));
        Assert.AreEqual((Packed, 2, 2060L, 2000L, -4, 9), (light!.Sector, light.Index, light.X, light.Y, light.OffsetX, light.OffsetY));
        Assert.AreEqual(StationaryLightFlags.Dark | StationaryLightFlags.Indoor, light.Flags);
        Assert.AreEqual(0x90280000u, light.ArtId); Assert.AreEqual(5, light.ArtNumber);
        // Bytes 11 22 33 00 are r, b, g, padding.
        Assert.AreEqual(new AmbientColor(0x11, 0x33, 0x22), light.Color);
        // The uninterpreted tint/palette/padding words stay bound by the record digest.
        TryDecode(Wire(ox: -4, oy: 9, flags: 10, palette: 0), out var other);
        Assert.AreNotEqual(light.RecordSha256, other!.RecordSha256);
        Assert.AreEqual(light with { RecordSha256 = "", Index = 0 }, other with { RecordSha256 = "" });
    }

    private static LightingFailure TryDecode(int[] words, out StationaryLight? light) => StationaryLightReader.TryDecode(Sector, 0, words, out light);

    [TestMethod]
    public void Decode_refuses_object_lights_foreign_locations_unknown_flags_and_non_light_art()
    {
        Assert.AreEqual(LightingFailure.LightNotStationary, TryDecode(Wire(owner: 1), out _));
        Assert.AreEqual(LightingFailure.LightOutsideSector, TryDecode(Wire(x: 2112), out _));
        Assert.AreEqual(LightingFailure.LightOutsideSector, TryDecode(Wire(x: 2047), out _));
        Assert.AreEqual(LightingFailure.LightOutsideSector, TryDecode(Wire(y: 1983), out _));
        Assert.AreEqual(LightingFailure.None, TryDecode(Wire(x: 2111, y: 2047), out _));
        Assert.AreEqual(LightingFailure.LightFlagsUnsupported, TryDecode(Wire(flags: 4), out _));
        Assert.AreEqual(LightingFailure.LightFlagsUnsupported, TryDecode(Wire(flags: 0x20), out _));
        // ARC-DATA-039: 0x40 keeps a light out of the original's object shadows only (ledger LT14).
        Assert.AreEqual(LightingFailure.None, TryDecode(Wire(flags: 0x40), out var noShadow));
        Assert.AreEqual(StationaryLightFlags.NoShadow, noShadow!.Flags);
        Assert.AreEqual(LightingFailure.LightArtUnsupported, TryDecode(Wire(art: 0x80280000), out _), "type 8 is not a light");
        Assert.AreEqual(LightingFailure.LightArtUnsupported, TryDecode(Wire(art: 0x90281000), out _), "frame 1");
        Assert.AreEqual(LightingFailure.LightArtUnsupported, TryDecode(Wire(art: 0x90280200), out _), "rotation 1");
        Assert.AreEqual(LightingFailure.LightArtUnsupported, TryDecode(Wire(art: 0x90280001), out _), "rotating light");
        Assert.AreEqual(LightingFailure.LightRecordMalformed, TryDecode(Wire(ox: 16385), out _));
        Assert.AreEqual(LightingFailure.LightRecordMalformed, StationaryLightReader.TryDecode(Sector, 0, new int[11], out _));
    }

    // ---- Field arithmetic over a literal 4x2 frame, hotspot (1,1) ----
    // Row 0: (100,0,0) (0,0,0)key (10,20,30) (255,255,255); row 1: transparent, (50,50,50), (0,0,0)key, (200,100,0).
    private static byte[] Mask()
    {
        var m = new byte[4 * 2 * 4];
        void Set(int x, int y, byte r, byte g, byte b, byte a = 255) { var at = (y * 4 + x) * 4; m[at] = r; m[at + 1] = g; m[at + 2] = b; m[at + 3] = a; }
        Set(0, 0, 100, 0, 0); Set(1, 0, 0, 0, 0, 0); Set(2, 0, 10, 20, 30); Set(3, 0, 255, 255, 255);
        Set(1, 1, 50, 50, 50); Set(2, 1, 0, 0, 0, 0); Set(3, 1, 200, 100, 0);
        return m;
    }
    private static StationaryLight Light(long x, long y, int ox, int oy, StationaryLightFlags flags, AmbientColor color, int index = 0)
        => new(Packed, index, x, y, ox, oy, flags, 7u << 19 | 9u << 28, color, "x");

    [TestMethod]
    public void Field_places_the_frame_like_a_body_and_adds_tinted_pixels()
    {
        // Cell (2048,1984) is the chart origin: tile screen (0,0); rect = (0 + 3 + 40 - 1, 0 + 2 + 20 - 1) = (42,21,4,2).
        var source = AreaStationaryLightSource.Create(5078, [Light(2048, 1984, 3, 2, 0, AmbientColor.Neutral)], [(7, 4, 2, 1, 1, Mask())]);
        var field = new StationaryLightField(source, 2048, 1984);
        Assert.AreEqual(new Arcanum.Data.Terrain.TerrainRect(42, 21, 4, 2), field.Placements.Single().Rect);
        var pair = new AmbientPair(new(1, 2, 3), new(4, 5, 6));
        var basis = new AmbientColor(200, 10, 0);
        Assert.AreEqual(new AmbientColor(255, 10, 0), field.Point(42, 21, basis, pair), "white light: the pixel itself, saturating");
        Assert.AreEqual(basis, field.Point(43, 21, basis, pair), "colour-key pixel");
        Assert.AreEqual(basis, field.Point(42, 22, basis, pair), "transparent pixel");
        Assert.AreEqual(basis, field.Point(41, 21, basis, pair), "outside the rectangle");
        Assert.AreEqual(basis, field.Point(46, 21, basis, pair), "outside the rectangle");
        Assert.AreEqual(new AmbientColor(210, 30, 30), field.Point(44, 21, basis, pair));

        // A non-white colour multiplies: (200,100,0) x (128,255,64) / 255 = (100,100,0).
        source = AreaStationaryLightSource.Create(5078, [Light(2048, 1984, 3, 2, 0, new(128, 255, 64))], [(7, 4, 2, 1, 1, Mask())]);
        field = new StationaryLightField(source, 2048, 1984);
        Assert.AreEqual(new AmbientColor(100, 100, 0), field.Point(45, 22, default, pair));
        // INDOOR takes the hour's indoor colour; OUTDOOR the outdoor one; INDOOR wins when both are set.
        Assert.AreEqual(new AmbientColor(4, 5, 6), StationaryLightField.Tint(Light(0, 0, 0, 0, StationaryLightFlags.Indoor | StationaryLightFlags.Outdoor, new(9, 9, 9)), pair));
        Assert.AreEqual(new AmbientColor(1, 2, 3), StationaryLightField.Tint(Light(0, 0, 0, 0, StationaryLightFlags.Outdoor, new(9, 9, 9)), pair));
        Assert.AreEqual(new AmbientColor(9, 9, 9), StationaryLightField.Tint(Light(0, 0, 0, 0, 0, new(9, 9, 9)), pair));
    }

    [TestMethod]
    public void Field_applies_dark_lights_in_order_and_ignores_off_lights()
    {
        var lights = new[]
        {
            Light(2048, 1984, 3, 2, StationaryLightFlags.Dark, AmbientColor.Neutral, 0),
            Light(2048, 1984, 3, 2, 0, AmbientColor.Neutral, 1),
            Light(2048, 1984, 3, 2, StationaryLightFlags.Off, AmbientColor.Neutral, 2),
        };
        var field = new StationaryLightField(AreaStationaryLightSource.Create(5078, lights, [(7, 4, 2, 1, 1, Mask())]), 2048, 1984);
        // Point: 30 - 100 floors at 0, then + 100 = 100 (the OFF light adds nothing).
        Assert.AreEqual(new AmbientColor(100, 0, 0), field.Point(42, 21, new(30, 0, 0), AmbientPair.Neutral));
        // The 4x2 rectangle at (42,21) holds no lattice node (x a multiple of 40 and y of 20): no lattice.
        Assert.IsNull(field.Lattice(AmbientPair.Neutral));
    }

    [TestMethod]
    public void Lattice_sums_lighter_and_darker_separately_and_composes_over_the_ambient()
    {
        // A 81x41 frame whose every pixel is (60,0,0) at hotspot (0,0) and offset (-40,-20): rect (0,0,81,41),
        // covering nodes x 0,40,80 and y 0,20,40.
        var mask = new byte[81 * 41 * 4];
        for (var at = 0; at < mask.Length; at += 4) { mask[at] = 60; mask[at + 3] = 255; }
        var lights = new[]
        {
            Light(2048, 1984, -40, -20, 0, AmbientColor.Neutral, 0),
            Light(2048, 1984, -40, -20, 0, AmbientColor.Neutral, 1),
            Light(2048, 1984, -40, -20, StationaryLightFlags.Dark, AmbientColor.Neutral, 2),
        };
        var field = new StationaryLightField(AreaStationaryLightSource.Create(5078, lights, [(7, 81, 41, 0, 0, mask)]), 2048, 1984);
        var lattice = field.Lattice(AmbientPair.Neutral)!;
        Assert.AreEqual((0, 0, 3, 3), (lattice.FirstColumn, lattice.FirstRow, lattice.Columns, lattice.Rows));
        Assert.AreEqual((new AmbientColor(120, 0, 0), new AmbientColor(60, 0, 0)), lattice.At(1, 1));
        Assert.AreEqual((default(AmbientColor), default(AmbientColor)), lattice.At(3, 1), "outside the lattice");
        // (200 + 120 saturates to 255) - 60 = 195: the lattice clamps before subtracting.
        Assert.AreEqual(new AmbientColor(195, 0, 0), StationaryLightField.Compose(new(200, 0, 0), new(120, 0, 0), new(60, 0, 0)));
        // At a node the bilinear terrain colour is the composed node; midway it is the average.
        Assert.AreEqual((195d, 0d, 0d), StationaryLightField.TerrainPixel(lattice, new(200, 0, 0), 40, 20));
        var beyond = StationaryLightField.TerrainPixel(lattice, new(200, 0, 0), 100, 20); // nodes 80 (195) and 120 (outside: 200)
        Assert.AreEqual(197.5, beyond.R, 1e-9);
        Assert.IsNull(new StationaryLightField(AreaStationaryLightSource.Create(5078, [lights[0] with { Flags = StationaryLightFlags.Off }],
            [(7, 81, 41, 0, 0, mask)]), 2048, 1984).Lattice(AmbientPair.Neutral));
    }

    [TestMethod]
    public void Synthetic_source_requires_frames_and_known_flags()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AreaStationaryLightSource.Create(1, [Light(0, 0, 0, 0, 0, default)], []));
        Assert.ThrowsExactly<ArgumentException>(() => AreaStationaryLightSource.Create(1, [Light(0, 0, 0, 0, (StationaryLightFlags)4, default)], [(7, 4, 2, 1, 1, Mask())]));
        Assert.ThrowsExactly<ArgumentException>(() => AreaStationaryLightSource.Create(1, [], [(7, 4, 2, 1, 1, new byte[3])]));
    }

    // ---- Reader over a synthetic ART resource and a strict-shaped snapshot ----

    private static AreaSnapshot Snapshot(IReadOnlyList<IReadOnlyList<int>> lights, bool strict = true, AreaScope scope = AreaScope.Core)
    {
        var key = new AreaInputKey(AreaSourceRole.Module);
        var record = new AreaSectorRecord(new(new string('a', 64), 5051, Packed), Sector, scope, true, 0, 2, lights.Count, 1,
            0xAA0003, 0, [], lights, null, null);
        var plan = new AreaSourcePlan(5078, 79, Sector, 2, "maps/cave", [new AreaInputIdentity(key, 1, new string('a', 64))], []);
        var snapshot = new AreaSnapshot(new object(), 1, plan, [record], [], [], [], new AreaReconciliation(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), []);
        if (strict) snapshot.MarkStrictlyPublished();
        return snapshot;
    }

    // An 8-bit single-group single-frame ART, 3x1 raw pixels with indexes 0, 1, 2; palette index 0 is
    // (9,9,9) (the colour key), index 1 is (9,9,9) too, index 2 is (40,80,120). Stored BGRA.
    private static byte[] Art(int framesPerGroup = 1, int framesPerSecond = 0)
    {
        var bytes = new byte[132 + 1024 + 28 * framesPerGroup + 3 * framesPerGroup];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), framesPerGroup);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), framesPerSecond);
        void Palette(int index, byte r, byte g, byte b) { var at = 132 + index * 4; bytes[at] = b; bytes[at + 1] = g; bytes[at + 2] = r; }
        Palette(0, 9, 9, 9); Palette(1, 9, 9, 9); Palette(2, 40, 80, 120);
        for (var f = 0; f < framesPerGroup; f++)
        {
            var frame = 132 + 1024 + 28 * f;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame), 3); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 8), 3); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 12), 2);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 16), 1);
            // Each frame's offset step: (f, -f).
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 20), f); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 24), -f);
            var payload = 132 + 1024 + 28 * framesPerGroup + 3 * f;
            bytes[payload] = 0; bytes[payload + 1] = 1; bytes[payload + 2] = 2;
        }
        return bytes;
    }

    private static StationaryLightReadResult Read(AreaSnapshot snapshot, params (string Name, byte[] Bytes)[] entries)
    {
        using var archive = DatArchive.Open(new MemoryStream(Dat(entries)));
        return StationaryLightReader.Read(archive, snapshot);
    }
    private static readonly byte[] Names = Encoding.ASCII.GetBytes("// lights\n{0}{Disk1x1}\n{5}{Disk5x5}\n{7}{../evil}\n");

    [TestMethod]
    public void Reader_decodes_core_lights_and_resolves_their_frames_by_number()
    {
        var result = Read(Snapshot([Wire(), Wire(x: 2090, y: 2030, ox: 5, oy: -3)]), ("art\\light\\light.mes", Names), ("art\\light\\Disk5x5.art", Art()));
        Assert.AreEqual(LightingFailure.None, result.Failure);
        var source = result.Source!;
        Assert.AreEqual(5078, source.CatalogueMap);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(Names)).ToLowerInvariant(), source.NamesSha256);
        Assert.AreEqual(2, source.Lights.Count); Assert.AreEqual(1, source.Frames.Count);
        var frame = source.Frames[5];
        Assert.AreEqual(("art/light/disk5x5.art", 3, 1, 2, 1), (frame.Name, frame.Width, frame.Height, frame.HotX, frame.HotY));
        // Index 0 (transparent) and index 1 (same colour as the key) contribute nothing; index 2 does.
        Assert.IsFalse(frame.TryPixel(0, 0, out _)); Assert.IsFalse(frame.TryPixel(1, 0, out _));
        Assert.IsTrue(frame.TryPixel(2, 0, out var color)); Assert.AreEqual(new AmbientColor(40, 80, 120), color);
    }

    [TestMethod]
    public void Reader_keeps_every_frame_of_an_animated_light_art()
    {
        // ARC-DATA-039 (ledger LT6, LT7): frame 0 is the light's frame; its Frames list all of them with the ART's rate and steps.
        var result = Read(Snapshot([Wire()]), ("art\\light\\light.mes", Names), ("art\\light\\Disk5x5.art", Art(framesPerGroup: 3, framesPerSecond: 8)));
        Assert.AreEqual(LightingFailure.None, result.Failure);
        var frame = result.Source!.Frames[5];
        Assert.IsTrue(frame.Animated);
        Assert.AreEqual(8, frame.FramesPerSecond);
        Assert.AreEqual(3, frame.Frames.Count);
        Assert.AreSame(frame, frame.Frames[0]);
        CollectionAssert.AreEqual(new[] { (0, 0), (1, -1), (2, -2) }, frame.Frames.Select(f => (f.StepX, f.StepY)).ToArray());
        Assert.IsTrue(frame.Frames[2].TryPixel(2, 0, out var color)); Assert.AreEqual(new AmbientColor(40, 80, 120), color);
        var still = Read(Snapshot([Wire()]), ("art\\light\\light.mes", Names), ("art\\light\\Disk5x5.art", Art())).Source!.Frames[5];
        Assert.IsFalse(still.Animated); Assert.AreEqual(0, still.FramesPerSecond);
    }

    [TestMethod]
    public void Reader_fails_closed()
    {
        var names = ("art\\light\\light.mes", Names);
        Assert.AreEqual(LightingFailure.SnapshotNotStrict, Read(Snapshot([Wire()], strict: false), names, ("art/light/disk5x5.art", Art())).Failure);
        Assert.AreEqual(LightingFailure.MissingResource, Read(Snapshot([Wire()])).Failure);
        Assert.AreEqual(LightingFailure.MissingResource, Read(Snapshot([Wire()]), names).Failure);
        Assert.AreEqual(LightingFailure.AmbiguousResource, Read(Snapshot([Wire()]), names, ("art/light/disk5x5.art", Art()), ("ART/LIGHT/DISK5X5.ART", Art())).Failure);
        Assert.AreEqual(LightingFailure.LightArtNotCatalogued, Read(Snapshot([Wire(art: 0x90300000)]), names).Failure, "number 6 is absent");
        Assert.AreEqual(LightingFailure.UnsafeResourceName, Read(Snapshot([Wire(art: 0x90380000)]), names).Failure, "number 7 traverses");
        Assert.AreEqual(LightingFailure.LightArtUnsupported, Read(Snapshot([Wire()]), names, ("art/light/disk5x5.art", Art(framesPerGroup: 2))).Failure, "animated without a rate");
        Assert.AreEqual(LightingFailure.LightArtUnsupported, Read(Snapshot([Wire()]), names, ("art/light/disk5x5.art", Art(framesPerGroup: StationaryLightReader.MaxArtFrames + 1, framesPerSecond: 8))).Failure, "too many frames");
        Assert.AreEqual(LightingFailure.LightArtUnsupported, Read(Snapshot([Wire()]), names, ("art/light/disk5x5.art", new byte[40])).Failure);
        Assert.AreEqual(LightingFailure.LightOutsideSector, Read(Snapshot([Wire(x: 1)]), names, ("art/light/disk5x5.art", Art())).Failure);
        // Context sectors are not read; no lights means no resource is needed at all.
        var context = Read(Snapshot([Wire(x: 1)], scope: AreaScope.Context));
        Assert.AreEqual(LightingFailure.None, context.Failure); Assert.AreEqual(0, context.Source!.Lights.Count); Assert.IsNull(context.Source.NamesSha256);
        Assert.AreEqual(LightingFailure.LimitExceeded, Read(Snapshot(Enumerable.Repeat<IReadOnlyList<int>>(Wire(), StationaryLightReader.MaxLights + 1).ToArray()), names, ("art/light/disk5x5.art", Art())).Failure);
    }

    /// <summary>ARC-DATA-027: a synthetic installation (tig.dat, an empty module archive, the given base archive and loose
    /// files relative to the game folder) read through its content stack.</summary>
    private static StationaryLightReadResult ReadStack(AreaSnapshot snapshot, (string Name, byte[] Bytes)[] arcanum1, params (string Path, byte[] Bytes)[] loose)
    {
        var root = Path.Combine(Path.GetTempPath(), "arc-lights-stack-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "modules"));
            File.WriteAllBytes(Path.Combine(root, "tig.dat"), Dat());
            File.WriteAllBytes(Path.Combine(root, "modules", "Mod.dat"), Dat());
            File.WriteAllBytes(Path.Combine(root, "arcanum1.dat"), Dat(arcanum1));
            foreach (var (relative, bytes) in loose)
            {
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            var discovery = Arcanum.Data.Content.ContentMountPlan.Discover(root, "Mod");
            var opened = Arcanum.Data.Content.ContentStack.Open(discovery.Plan!);
            Assert.AreEqual(Arcanum.Data.Content.ContentStackStatus.Ready, opened.Status, opened.Status + " " + opened.Detail);
            using var stack = opened.Stack!;
            return StationaryLightReader.Read(stack, snapshot);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void Stack_reader_takes_the_name_table_from_the_base_and_the_art_from_the_winner()
    {
        var snapshot = Snapshot([Wire()]);
        var base1 = new[] { ("art/light/disk5x5.art", Art()), ("art/light/light.mes", Names) };
        var result = ReadStack(snapshot, base1);
        Assert.AreEqual(LightingFailure.None, result.Failure);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(Names)).ToLowerInvariant(), result.Source!.NamesSha256);

        // The name table is read at game start, so a module copy is never read; the ART is read when drawn, so a
        // module-folder copy wins.
        var moduleNames = Encoding.ASCII.GetBytes("{5}{Other}\n");
        var animated = Art(framesPerGroup: 2);
        var overridden = ReadStack(snapshot, base1, ("modules/Mod/art/light/light.mes", moduleNames));
        Assert.AreEqual(result.Source.NamesSha256, overridden.Source!.NamesSha256);
        Assert.AreEqual(LightingFailure.LightArtUnsupported, ReadStack(snapshot, base1, ("modules/Mod/art/light/disk5x5.art", animated)).Failure);
        // A loose data/ copy is a base source above the archives: number 5 now names other.art, which nothing holds.
        Assert.AreEqual(LightingFailure.MissingResource, ReadStack(snapshot, base1, ("data/art/light/light.mes", moduleNames)).Failure);
        Assert.AreEqual(LightingFailure.MissingResource, ReadStack(snapshot, [("art/light/disk5x5.art", Art())], ("modules/Mod/art/light/light.mes", Names)).Failure);
    }

    // Opt-in original data: the four cave records and their two frames.
    [TestMethod, TestCategory("RealData")]
    public void Original_cave_has_four_stationary_lights_with_two_static_frames()
    {
        var cave = Environment.GetEnvironmentVariable("ARC_CAVE_MOBILE");
        if (string.IsNullOrEmpty(cave)) Assert.Inconclusive("Opt-in original inputs and ARC_CAVE_MOBILE are required.");
        using var owner = AreaOriginalValidator.OpenCaveFiles(Required("ARC_AREA_MODULE"), Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"), cave, Required("ARC_AREA_PROTOTYPES"));
        var snapshot = owner.LoadStrict();
        using var resource = DatArchive.Open(Required("ARC_AREA_RESOURCE2"), new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 });
        var result = StationaryLightReader.Read(resource, snapshot);
        Assert.AreEqual(LightingFailure.None, result.Failure);
        var source = result.Source!;
        Assert.AreEqual("a6c9f3df3ecdf6b7b6c522138f6c64376e3236c6d2ea4f7eae08aabd1f15606c", source.NamesSha256);
        Assert.AreEqual(4, source.Lights.Count);
        Assert.IsTrue(source.Lights.All(light => light.Sector == Packed && light.Flags == StationaryLightFlags.None));
        Assert.AreEqual("11ad9c3e3406e6b5f9d301aca67eaa063ddd7dae282c1250b40cb46f83919b8e", source.Lights[0].RecordSha256, "the RES-018 record 0 digest");
        CollectionAssert.AreEqual(new[] { 5, 5, 5, 2 }, source.Lights.Select(light => light.ArtNumber).ToArray());
        Assert.AreEqual(("art/light/disk3x3.art", 233, 117, 115, 57), (source.Frames[2].Name, source.Frames[2].Width, source.Frames[2].Height, source.Frames[2].HotX, source.Frames[2].HotY));
        Assert.AreEqual(("art/light/disk5x5.art", 450, 225, 225, 112), (source.Frames[5].Name, source.Frames[5].Width, source.Frames[5].Height, source.Frames[5].HotX, source.Frames[5].HotY));
        var core = snapshot.Sectors.Single(sector => sector.Scope == AreaScope.Core).Coordinate;
        var lattice = new StationaryLightField(source, core.OriginX, core.OriginY).Lattice(AmbientPair.Neutral)!;
        Assert.AreEqual((28, 29), (lattice.Columns, lattice.Rows));
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new AssertInconclusiveException(name + " is required.");

    private static byte[] Dat(params (string Name, byte[] Bytes)[] entries)
    {
        using var output = new MemoryStream(); var offsets = new List<uint>();
        foreach (var entry in entries) { offsets.Add(checked((uint)output.Position)); output.Write(entry.Bytes); }
        using var index = new MemoryStream(); U32(index, checked((uint)entries.Length));
        foreach (var (entry, i) in entries.Select((entry, i) => (entry, i)))
        {
            var name = Encoding.ASCII.GetBytes(entry.Name + "\0");
            U32(index, checked((uint)name.Length)); index.Write(name); index.Write(new byte[4]); U32(index, 1);
            U32(index, checked((uint)entry.Bytes.Length)); U32(index, checked((uint)entry.Bytes.Length)); U32(index, offsets[i]);
        }
        index.Position = 0; index.CopyTo(output); output.Write(new byte[16]); output.Write("1TAD"u8);
        U32(output, checked((uint)entries.Sum(e => e.Name.Length + 1))); U32(output, checked((uint)(index.Length + 28)));
        return output.ToArray();
    }
    private static void U32(Stream stream, uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); stream.Write(bytes); }
}
