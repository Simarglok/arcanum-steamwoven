using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Lighting;

// ARC-DATA-039: object lights of a strict snapshot (ledger LT1, LT4) over invented objects; no original data.
[TestClass]
public sealed class ObjectLightTests
{
    private static readonly AreaSectorCoordinate Sector = new(32, 31);
    private const uint Art5 = 0x90280000;

    private static AreaFieldValue Value(int slot, long number) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, number, [], null);
    private static AreaFieldValue Array(int slot, params (int Key, long Word)[] elements) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Prototype, elements.Length, elements.Select(e => new AreaSparseElement(e.Key, [e.Word])).ToArray(), null);

    private static AreaObjectRecord Object(int index, int type, long x, long y, AreaScope scope = AreaScope.Core, params AreaFieldValue[] fields) =>
        new(new(new string('a', 64), 79, AreaSourceKind.Static, Sector.Packed, index, index * 10, new string((char)('b' + index), 64), 10), 119, type, 4000,
            "", "", "", "", scope, null, x, y, AreaDisposition.BodyEligibleConditionally, fields, fields, true, true, true);

    private static AreaSnapshot Snapshot(AreaObjectRecord[] objects, AreaTerrainRecord[]? terrain = null, bool strict = true)
    {
        var key = new AreaInputKey(AreaSourceRole.Module);
        var record = new AreaSectorRecord(new(new string('a', 64), 5051, Sector.Packed), Sector, AreaScope.Core, true, 0, 2, 0, 1, 0xAA0003, 0, [], [], null, null);
        var plan = new AreaSourcePlan(5078, 79, Sector, 2, "maps/cave", [new AreaInputIdentity(key, 1, new string('a', 64))], []);
        var snapshot = new AreaSnapshot(new object(), 1, plan, [record], terrain ?? [], objects, [], new AreaReconciliation(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), []);
        if (strict) snapshot.MarkStrictlyPublished();
        return snapshot;
    }

    private static AreaTerrainRecord Tile(long x, long y, uint art) =>
        new(new(AreaSourceKind.Static, new(new string('a', 64), 5051, Sector.Packed), 0, ""), (int)(x & 63), (int)(y & 63), x, y, art, art, AreaScope.Core,
            new(AreaSourceKind.Static, new(new string('a', 64), 5051, Sector.Packed), 0, ""));

    [TestMethod]
    public void Primary_and_overlay_lights_come_from_the_effective_fields()
    {
        var lamp = Object(0, 3, 2060, 2000, AreaScope.Core,
            Value(2, 5), Value(3, -7), Value(12, 0x10 | 0x80 | 0x8000000), Value(13, Art5 | 2 << 12), Value(14, 0x112233),
            Array(15, (1, 0x2 | 0x20000000)), Array(16, (0, -1), (1, Art5)), Array(17, (1, 0x445566)), Value(36, 0x4));
        var dark = Object(1, 14, 2061, 2001, AreaScope.Core, Value(13, -1), Array(16, (3, Art5)), Array(15, (3, 0x40)));
        var context = Object(2, 3, 2062, 2002, AreaScope.Context, Value(13, Art5));
        var carried = Object(3, 6, 2063, 2003, AreaScope.Core, Value(13, Art5), Value(18, 0x1000));
        Assert.AreEqual(LightingFailure.None, ObjectLightReader.Describe(Snapshot([lamp, dark, context, carried], [Tile(2060, 2000, 0x100)]), out var lights));
        Assert.AreEqual(3, lights.Count);
        var primary = lights[0];
        Assert.AreEqual((-1, 2060L, 2000L, 5, -7), (primary.Slot, primary.X, primary.Y, primary.OffsetX, primary.OffsetY));
        // The saved runtime bits (changed, the primary marker) are dropped; the colour is packed r << 16 | g << 8 | b.
        Assert.AreEqual(StationaryLightFlags.Outdoor, primary.Flags);
        Assert.AreEqual(new AmbientColor(0x11, 0x22, 0x33), primary.Color);
        Assert.AreEqual((5, 2), (primary.ArtNumber, primary.Frame));
        Assert.IsTrue(primary.Nocturnal && primary.OutdoorTile && !primary.ObjectOff);
        Assert.AreEqual((1, StationaryLightFlags.Dark, new AmbientColor(0x44, 0x55, 0x66)), (lights[1].Slot, lights[1].Flags, lights[1].Color));
        Assert.AreEqual((3, StationaryLightFlags.NoShadow, false), (lights[2].Slot, lights[2].Flags, lights[2].Nocturnal));
        Assert.IsFalse(lights[2].OutdoorTile, "no tile record: not an outdoor tile");
        // A facade's outdoor bit is bit 25 (0x5038C0); another ART type has none.
        Assert.AreEqual(LightingFailure.None, ObjectLightReader.Describe(Snapshot([lamp], [Tile(2060, 2000, 0xB2000000)]), out var facade));
        Assert.IsTrue(facade[0].OutdoorTile);
        Assert.AreEqual(LightingFailure.None, ObjectLightReader.Describe(Snapshot([lamp], [Tile(2060, 2000, 0x30000100)]), out var other));
        Assert.IsFalse(other[0].OutdoorTile);
    }

    [TestMethod]
    public void Nocturnal_scenery_on_an_outdoor_tile_is_built_only_at_night()
    {
        var at = new ObjectLight(default, 3, -1, 0, 0, 0, 0, 0, Art5, default, false, true, true);
        Assert.IsFalse(ObjectLightReader.BuiltAtLoad(at, 6));
        Assert.IsFalse(ObjectLightReader.BuiltAtLoad(at, 17));
        Assert.IsTrue(ObjectLightReader.BuiltAtLoad(at, 18));
        Assert.IsTrue(ObjectLightReader.BuiltAtLoad(at, 5));
        Assert.IsTrue(ObjectLightReader.BuiltAtLoad(at with { OutdoorTile = false }, 12), "an indoor tile keeps it on");
        Assert.IsTrue(ObjectLightReader.BuiltAtLoad(at with { Nocturnal = false }, 12));
        // A built nocturnal light is switched on with its object (LT4, LT13); another keeps its saved flags.
        var saved = at with { Flags = StationaryLightFlags.Off | StationaryLightFlags.Outdoor };
        Assert.AreEqual(StationaryLightFlags.Outdoor, ObjectLightReader.FlagsAtLoad(saved, 20));
        Assert.AreEqual(StationaryLightFlags.Outdoor, ObjectLightReader.FlagsAtLoad(saved with { OutdoorTile = false }, 12));
        Assert.AreEqual(saved.Flags, ObjectLightReader.FlagsAtLoad(saved with { Nocturnal = false }, 12));
        Assert.ThrowsExactly<ArgumentException>(() => ObjectLightReader.FlagsAtLoad(saved, 12));
    }

    [TestMethod]
    public void Unknown_bits_other_art_and_caps_fail_closed()
    {
        static LightingFailure One(params AreaFieldValue[] fields) => ObjectLightReader.Describe(Snapshot([Object(0, 3, 2060, 2000, AreaScope.Core, fields)]), out _);
        Assert.AreEqual(LightingFailure.LightFlagsUnsupported, One(Value(13, Art5), Value(12, 0x100)));
        Assert.AreEqual(LightingFailure.None, One(Value(13, Art5), Value(12, 0x20)), "no animation is data");
        // LT6: a light saved while animating (0x4) does not animate again.
        ObjectLightReader.Describe(Snapshot([Object(0, 3, 2060, 2000, AreaScope.Core, Value(13, Art5), Value(12, 0x4 | 0x10))]), out var saved);
        Assert.AreEqual(StationaryLightFlags.Outdoor | StationaryLightFlags.NoAnimation, saved[0].Flags);
        Assert.AreEqual(LightingFailure.LightArtUnsupported, One(Value(13, 0x80280000)), "not a light ART");
        Assert.AreEqual(LightingFailure.LightArtUnsupported, One(Value(13, Art5 | 0x200)), "rotation");
        Assert.AreEqual(LightingFailure.LightRecordMalformed, One(Value(13, Art5), Value(2, 16385)));
        Assert.AreEqual(LightingFailure.LightRecordMalformed, One(Array(16, (4, Art5))), "overlay slot 4");
        Assert.AreEqual(LightingFailure.LightRecordMalformed, One(new AreaFieldValue(16, AreaPresence.Array, AreaValueOrigin.Instance, 1, [new(0, [])], null)), "no word");
        var many = Enumerable.Range(0, ObjectLightReader.MaxLights + 1).Select(i => Object(i % 1000, 3, 2060, 2000, AreaScope.Core, Value(13, Art5))).ToArray();
        Assert.AreEqual(LightingFailure.LimitExceeded, ObjectLightReader.Describe(Snapshot(many), out _));
        Assert.AreEqual(LightingFailure.None, ObjectLightReader.Describe(Snapshot([Object(0, 3, 2060, 2000)]), out var none));
        Assert.AreEqual(0, none.Count);
    }
}
