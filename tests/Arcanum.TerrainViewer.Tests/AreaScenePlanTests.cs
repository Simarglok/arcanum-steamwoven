using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RenderDisposition = Arcanum.World.AreaDisposition;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaScenePlanTests
{
    private static readonly string Digest = new('a', 64);
    private static AreaVisualKey TerrainKey(long sector, int index) => new(AreaSourceKind.Terrain, new(Digest, 5051, sector), index, "");
    private static AreaObjectRecord Body(int index, long x = 92928, long y = 82560, uint flags = 0, int scale = 100, long scenery = 0, int type = 3)
    {
        var key = new AreaObjectKey(Digest, 5051, AreaSourceKind.Static, 86570436012, index, index * 100, Digest, 100);
        var fields = new[] { Field(18, flags), Field(8, 0), Field(9, 0xFFFFFF), Field(10, 255), Field(11, scale), Field(36, scenery) };
        return new(key, 119, type, 1, Digest, Digest, Digest, Digest, AreaScope.Core, null, x, y,
            Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, fields, fields, true, true, true);
    }
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaVisualRecord Visual(AreaVisualKey key, int width = 80, int height = 40, int hotX = 0, int hotY = 0, int ox = 0, int oy = 0, int scale = 100)
        => new(key, key.Kind == AreaSourceKind.Terrain ? 0u : 0x40000000u, 0,
            new(new(AreaSourceRole.Resource2), 1, 0, 100, 100, 0, Digest), Digest, 100, 0, 0, 0, 0, 0, 0,
            false, width, height, hotX, hotY, 777, -999, ox, oy, scale, 1, width * height * 4, Digest, "synthetic");
    private static AreaVisualRecord BodyVisual(AreaObjectRecord body, int width = 120, int height = 240, int hotX = 60, int hotY = 220, int ox = 0, int oy = 0, int scale = 100)
        => Visual(new(body.Key.Kind, new(Digest, 5051, body.Key.Sector!.Value), body.Key.SourceIndex, Digest, Object: body.Key), width, height, hotX, hotY, ox, oy, scale);

    [TestMethod]
    public void A_roof_block_is_drawn_from_its_normalized_tile_less_120_200_and_its_hot_spot_after_the_plan_commands()
    {
        // ARC-RENDER-022 (ledger RF1, RF7): the block at the chart origin, normalized to (+2, +2): projected (0, 80).
        var tile = TerrainKey(100, 0);
        var sector = new AreaSectorKey(Digest, 5051, 100);
        AreaVisualKey RoofKey(int slot) => new(AreaSourceKind.Terrain, sector, 4096 + slot, "", AreaLayerKind.Roof);
        var plain = Visual(RoofKey(0), 300, 150, -95, 7) with { RawArt = 10u << 28 };
        var mirrored = Visual(RoofKey(1), 300, 150, -95, 7) with { RawArt = (10u << 28) | 1, Mirrored = true };
        AreaRoofRecord Roof(int slot, long x, int piece) => new(sector, slot, x, 82560, 10u << 28, piece, false, false, RoofKey(slot));
        var plan = AreaScenePlan.Create([new AreaTerrainRecord(tile, 0, 0, 92928, 82560, 0, 0, AreaScope.Core, tile)], [], [Visual(tile), plain, mirrored],
            null, [Roof(0, 92928, 0), Roof(1, 92932, 9)]);
        Assert.AreEqual(1, plan.Commands.Count, "roofs stay out of the terrain and body commands");
        Assert.AreEqual(2, plan.Roofs.Count);
        Assert.AreEqual(new TerrainRect(0 - 120 + 95, 80 - 200 - 7, 300, 150), plan.Roofs[0].Command.Destination);
        // Four tiles further on x: projected (-160, 160); a mirrored roof frame takes hot spot x 0.
        Assert.AreEqual(new TerrainRect(-160 - 120, 160 - 200 - 7, 300, 150), plan.Roofs[1].Command.Destination);
        Assert.IsTrue(plan.Roofs[1].Command.FlipX);
        CollectionAssert.IsSubsetOf(new[] { plan.Roofs[0].Command.Frame }, plan.Frames.ToArray());
    }

    [TestMethod]
    public void A_body_scaled_about_its_hotspot_keeps_the_hotspot_on_its_tile_ARC_UI_063()
    {
        var tile = TerrainKey(100, 0);
        var body = Body(1);
        var plan = AreaScenePlan.Create([new AreaTerrainRecord(tile, 0, 0, 92928, 82560, 0, 0, AreaScope.Core, tile)], [body], [Visual(tile), BodyVisual(body)]);
        var visual = BodyVisual(body);
        var full = AreaScenePlan.Place(plan.Chart, body.Key with { SourceIndex = 9 }, visual, 92928, 82560, flat: false);
        var (at, hotX, hotY) = (full.Destination, 60, 220);
        Assert.AreSame(full, AreaScenePlan.ScaledAboutHotspot(full, visual, 100));
        Assert.AreEqual(new TerrainRect(at.X + hotX - 120, at.Y + hotY - 440, 240, 480), AreaScenePlan.ScaledAboutHotspot(full, visual, 200).Destination, "double size");
        Assert.AreEqual(new TerrainRect(at.X + hotX - 30, at.Y + hotY - 110, 60, 120), AreaScenePlan.ScaledAboutHotspot(full, visual, 50).Destination, "half size");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaScenePlan.ScaledAboutHotspot(full, visual, 0));
    }

    [TestMethod]
    public void A_placed_body_stands_where_a_T0_body_of_its_frame_would_and_a_flat_one_joins_the_flat_commands_in_plan_order()
    {
        // ARC-RENDER-033: an item lying where no T0 body stood is placed by the rule Create places a body with.
        var tile = TerrainKey(100, 0);
        var upright = Body(1); var flat = Body(2, x: 92930, flags: 0x4);
        var plan = AreaScenePlan.Create([new AreaTerrainRecord(tile, 0, 0, 92928, 82560, 0, 0, AreaScope.Core, tile)], [upright, flat],
            [Visual(tile), BodyVisual(upright), BodyVisual(flat, 40, 20, 20, 10)]);
        var drawn = plan.Commands.Single(c => c.Identity == upright.Key);
        var placedKey = upright.Key with { SourceIndex = 9 };
        var placed = AreaScenePlan.Place(plan.Chart, placedKey, BodyVisual(upright), 92928, 82560, flat: false);
        Assert.AreEqual((drawn.Destination, drawn.Phase, drawn.FirstRank, drawn.SecondRank), (placed.Destination, placed.Phase, placed.FirstRank, placed.SecondRank));
        var flatDrawn = plan.Commands.Single(c => c.Identity == flat.Key);
        var lying = AreaScenePlan.Place(plan.Chart, placedKey, BodyVisual(flat, 40, 20, 20, 10), 92929, 82560, flat: true);
        Assert.AreEqual(3, lying.Phase);
        // Flat commands in plan order: the tile, then the lying body at x 92929 before the flat T0 body at 92930.
        var flats = plan.Commands.Where(c => c.Phase < 4).ToArray();
        var merged = AreaScenePlan.WithMovingFlat(flats, [lying, placed], new HashSet<AreaObjectKey?> { placedKey });
        CollectionAssert.AreEqual(new[] { flats[0], lying, flatDrawn }, merged.ToArray(), "the upright one stays out of the flat list");
        // A flat T0 body that moved is drawn only where it moved to.
        var moved = flatDrawn with { MapX = 92931 };
        CollectionAssert.AreEqual(new[] { flats[0], moved }, AreaScenePlan.WithMovingFlat(flats, [moved], new HashSet<AreaObjectKey?> { flat.Key }).ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => AreaScenePlan.Place(plan.Chart, placedKey, Visual(tile) with { RawArt = 1u << 28 }, 92928, 82560, false));
    }

    [TestMethod]
    public void A_placed_body_takes_the_blend_of_its_blit_flags_as_a_map_body_does_ARC_MAGIC_054()
    {
        var tile = TerrainKey(100, 0);
        var body = Body(1);
        var plan = AreaScenePlan.Create([new AreaTerrainRecord(tile, 0, 0, 92928, 82560, 0, 0, AreaScope.Core, tile)], [body], [Visual(tile), BodyVisual(body)]);
        var plain = AreaScenePlan.Place(plan.Chart, body.Key with { SourceIndex = 9 }, BodyVisual(body), 92928, 82560, flat: false);
        // The same rule as a map body's State: every valid blit state of a map body (mirrored art or not) gives the same blend and flips as a placed body.
        foreach (var mirrored in new[] { false, true })
            foreach (var (blit, alpha) in new[] { (0L, 255L), (0x10L, 255L), (0x40L, 255L), (0x100L, 95L), (0x150L, 255L), (0x140L, 255L), (0x1L, 255L), (0x2L, 255L), (0x3L, 255L), (0x111L, 255L), (0x100L, 0L) })
            {
                var visual = BodyVisual(body) with { Mirrored = mirrored };
                var mapBody = AreaScenePlan.Create([], [With(body, (8, blit), (10, alpha))], [visual]).Commands.Single();
                var placedBody = AreaScenePlan.WithBlend(AreaScenePlan.Place(plan.Chart, body.Key with { SourceIndex = 9 }, visual, 92928, 82560, flat: false), (uint)blit, (int)alpha);
                Assert.AreEqual((mapBody.Additive, mapBody.Multiply, mapBody.Unlit, mapBody.Alpha, mapBody.FlipX, mapBody.FlipY),
                    (placedBody.Additive, placedBody.Multiply, placedBody.Unlit, placedBody.Alpha, placedBody.FlipX, placedBody.FlipY), $"blit 0x{blit:X} alpha {alpha} mirrored {mirrored}");
            }
        Assert.AreEqual(plain, AreaScenePlan.WithBlend(plain, 0, 255), "no blit flag: the plain draw");
        var additive = AreaScenePlan.WithBlend(plain, 0x10, 255);
        Assert.IsTrue(additive.Additive && additive.Unlit && !additive.Multiply && additive.Alpha == 255 && additive.Blended, "additive 0x10, base palette, unlit");
        var both = AreaScenePlan.WithBlend(plain, 0x150, 80);
        Assert.IsTrue(both.Additive && !both.Multiply && both.Alpha == 255, "additive wins over multiply and constant alpha");
        var multiply = AreaScenePlan.WithBlend(plain, 0x140, 80);
        Assert.IsTrue(multiply.Multiply && !multiply.Additive && multiply.Unlit && multiply.Alpha == 255, "multiply wins over constant alpha");
        var constant = AreaScenePlan.WithBlend(plain, 0x100, 80);
        Assert.IsTrue(constant.Alpha == 80 && constant.Unlit && !constant.Additive && !constant.Multiply, "constant alpha 0x100 reads the blit alpha");
        var flipped = AreaScenePlan.WithBlend(plain, 0x3, 255);
        Assert.AreEqual((!plain.FlipX, true), (flipped.FlipX, flipped.FlipY), "the mirror bits flip across x and y");
        Assert.AreEqual((plain.Destination, plain.Phase, plain.Visual), (additive.Destination, additive.Phase, additive.Visual), "the blend moves nothing");
        Assert.ThrowsExactly<ArgumentNullException>(() => AreaScenePlan.WithBlend(null!, 0, 255));
    }

    [TestMethod]
    public void AdjacentSeamsRetainOccurrenceKeysAndGlobalChart()
    {
        var a = TerrainKey(100, 63); var b = TerrainKey(101, 0); var c = TerrainKey(102, 0);
        var records = new[] { new AreaTerrainRecord(a, 63, 0, 92991, 82560, 0, 0, AreaScope.Core, a),
            new AreaTerrainRecord(b, 0, 0, 92992, 82560, 0, 0, AreaScope.Core, b),
            new AreaTerrainRecord(c, 0, 0, 92992, 82624, 0, 0, AreaScope.Core, b) };
        var plan = AreaScenePlan.Create(records, [], [Visual(a), Visual(b)]);
        Assert.AreEqual(new TerrainRect(-2519,1260,78,40), plan.Commands.Single(command => command.Visual == a).Destination);
        Assert.AreEqual(new TerrainRect(-2559,1280,78,40), plan.Commands.Single(command => command.Visual == b).Destination);
        Assert.AreEqual(new TerrainRect(1,2560,78,40), plan.Commands.Single(command => command.Visual == c).Destination);
        Assert.AreEqual(3, plan.Commands.Select(command => command.Visual).Distinct().Count());
        Assert.AreEqual(1, plan.Commands.Select(command => command.Frame).Distinct().Count());
    }
    [TestMethod]
    public void EachBodyFindsItsOwnVisualAndTwoBodyVisualsForOneObjectAreRefused()
    {
        // ARC-RENDER-019: bodies find their visual by object key (a lookup, not a scan per body).
        var bodies = Enumerable.Range(0, 200).Select(index => Body(index, 92928 + index, 82560)).ToArray();
        var plan = AreaScenePlan.Create([], bodies, bodies.Select((body, index) => BodyVisual(body, 10 + index, 20)).Reverse().ToArray());
        Assert.AreEqual(200, plan.Commands.Count);
        foreach (var command in plan.Commands) Assert.AreEqual(10 + command.Identity!.Value.SourceIndex, command.Destination.Width);
        var twice = BodyVisual(bodies[0]) with { Key = BodyVisual(bodies[0]).Key with { SourceIndex = 9999 } };
        Assert.AreEqual("AREA_VISUAL_AMBIGUOUS", Assert.ThrowsExactly<InvalidOperationException>(() => AreaScenePlan.Create([], [bodies[0]], [BodyVisual(bodies[0]), twice])).Message);
        // A body without a visual is still refused.
        Assert.AreEqual("AREA_VISUAL_REQUIRED", Assert.ThrowsExactly<InvalidOperationException>(() => AreaScenePlan.Create([], [bodies[0]], [])).Message);
    }
    [TestMethod]
    public void BodyCenterSignedHotspotsAndSerializedScaleDoNotUseArtOffsets()
    {
        var body = Body(1, scale: 75); var visual = BodyVisual(body, 167,321,-12,-40,scale:75);
        var command = AreaScenePlan.Create([], [body], [visual]).Commands.Single();
        Assert.AreEqual(new TerrainRect(49,50,125,240), command.Destination);
        Assert.AreEqual(92928, command.MapX); Assert.AreEqual(82560, command.MapY);
        var shrunk = Body(1, flags: 0x80, scale:75);
        Assert.AreEqual(new TerrainRect(44,35,62,120), AreaScenePlan.Create([], [shrunk], [visual]).Commands.Single().Destination);
    }
    [TestMethod]
    public void GlobalMapOrderDiffersFromProjectedAnchorYAndIsIndependentOfCanopySize()
    {
        var a = Body(0, 92940,82560); var b = Body(1,92928,82561);
        var plan = AreaScenePlan.Create([], [b,a], [BodyVisual(a,1000,1000), BodyVisual(b,10,10)]);
        CollectionAssert.AreEqual(new[] { a.Key,b.Key }, plan.Commands.Select(command => command.Identity!.Value).ToArray());
        var second = AreaScenePlan.Create([], [a,b], [BodyVisual(a,10,10),BodyVisual(b,1000,1000)]);
        CollectionAssert.AreEqual(plan.Commands.Select(command => command.Identity!.Value).ToArray(),second.Commands.Select(command => command.Identity!.Value).ToArray());
    }
    [TestMethod]
    public void LocalSignedRanksAndFlatUnderAllHaveLiteralOrdering()
    {
        Assert.AreEqual((27,-23), AreaScenePlan.SameCellRanks(-11,3));
        Assert.AreEqual((20,-20), AreaScenePlan.SameCellRanks(0,0));
        Assert.AreEqual((-1,1), AreaScenePlan.SameCellRanks(43,1));
        var upright = Body(0); var flat = Body(1, flags:4); var under = Body(2, flags:4,scenery:512);
        var plan = AreaScenePlan.Create([], [upright,flat,under], [BodyVisual(upright),BodyVisual(flat),BodyVisual(under)]);
        CollectionAssert.AreEqual(new[] { under.Key,flat.Key,upright.Key }, plan.Commands.Select(command => command.Identity!.Value).ToArray());
        CollectionAssert.AreEqual(new[] { 2,3,4 }, plan.Commands.Select(command => command.Phase).ToArray());
    }
    // ARC-DEV-173: the original's tile list ranks a wall or portal by its ART rotation alone (insert 0x4F20A0, rank 0x4F2230).
    [TestMethod]
    public void A_wall_or_portal_ranks_by_its_rotation_and_every_other_object_by_its_offsets()
    {
        foreach (var rotation in new[] { 0, 1, 6, 7 })
            foreach (var type in new[] { 0, 1 })
                Assert.AreEqual((0, -40), AreaScenePlan.BodyRanks(type, ArtOf(1, rotation), 11, 7), $"type {type} rotation {rotation}");
        foreach (var rotation in new[] { 2, 3, 4, 5 })
            foreach (var type in new[] { 0, 1 })
                Assert.AreEqual((38, -2), AreaScenePlan.BodyRanks(type, ArtOf(1, rotation), -11, 3), $"type {type} rotation {rotation}");
        Assert.AreEqual(AreaScenePlan.SameCellRanks(-11, 3), AreaScenePlan.BodyRanks(3, ArtOf(4, 3), -11, 3));
        Assert.AreEqual(AreaScenePlan.SameCellRanks(0, 0), AreaScenePlan.BodyRanks(16, ArtOf(2, 3), 0, 0));
        // The art rotation of the original (0x502880): none for the classes 0, 5, 6, 8, 10, 11; bits 9..11 for 9 and 14.
        foreach (var cls in new uint[] { 0, 5, 6, 8, 10, 11 }) Assert.AreEqual(0, AreaScenePlan.ArtRotation(ArtOf(cls, 5)));
        foreach (var cls in new uint[] { 9, 14 }) Assert.AreEqual(5, AreaScenePlan.ArtRotation((cls << 28) | (5u << 9)));
        Assert.AreEqual(5, AreaScenePlan.ArtRotation(ArtOf(1, 5)));
    }
    private static uint ArtOf(uint artClass, int rotation) => (artClass << 28) | ((uint)rotation << 11);

    [TestMethod]
    public void A_scenery_standing_on_a_walls_tile_is_drawn_after_a_far_side_wall_and_before_a_near_side_wall()
    {
        // The owner's bathtub (ARC-DEV-173): a scenery with a small instance offset (ranks 19, -19) on the tile of a building's wall.
        // A wall of the near sides (rotation 2..5) must cover it; a wall of the far sides (rotation 0, 1, 6, 7) is covered by it.
        foreach (var (rotation, wallLast) in new[] { (2, true), (5, true), (0, false), (1, false), (6, false), (7, false) })
        {
            var scenery = Body(0, 92928, 82560); var wall = Body(1, 92928, 82560, type: 0);
            var sceneryVisual = BodyVisual(scenery, ox: 2, oy: 0);
            var wallVisual = BodyVisual(wall) with { RawArt = ArtOf(1, rotation) };
            // The file order puts the wall first and the scenery second, as in the owner's map; the rank decides, not the file.
            var order = AreaScenePlan.Create([], [wall, scenery], [wallVisual, sceneryVisual]).Commands.Select(command => command.Identity!.Value).ToArray();
            CollectionAssert.AreEqual(wallLast ? new[] { scenery.Key, wall.Key } : new[] { wall.Key, scenery.Key }, order, $"rotation {rotation}");
        }
    }
    [TestMethod]
    public void InventoryAndHiddenBodiesAreKeyedReasonsNotDroppedResources()
    {
        var a = Body(0,flags:0x1000); var b = Body(1,flags:0x100); var c = Body(2,flags:0x200); var d = Body(3);
        var plan = AreaScenePlan.Create([], [a,b,c,d], [BodyVisual(a),BodyVisual(b),BodyVisual(c),BodyVisual(d)]);
        Assert.AreEqual(1,plan.Commands.Count); Assert.AreEqual(4,plan.Coverage.Count);
        Assert.AreEqual("ExteriorSectorInsertionRefusedByInventory", plan.Coverage.Single(row=>row.Identity==a.Key).Reason);
        Assert.AreEqual("BodySuppressedBySerializedState", plan.Coverage.Single(row=>row.Identity==b.Key).Reason);
        Assert.AreEqual("InvisibleNoDetectionObserver", plan.Coverage.Single(row=>row.Identity==c.Key).Reason);
        Assert.AreEqual(RenderDisposition.UprightBody,plan.Coverage.Single(row=>row.Identity==d.Key).Disposition);
    }
    [TestMethod]
    public void MissingRequiredFieldAndUnsupportedBlendAreFrontiers()
    {
        var body = Body(0);
        Assert.ThrowsExactly<InvalidOperationException>(() => AreaScenePlan.Create([], [body with { EffectiveFields = body.EffectiveFields.Where(field=>field.Slot!=18).ToArray() }], [BodyVisual(body)]));
        Assert.ThrowsExactly<InvalidOperationException>(() => AreaScenePlan.Create([], [body with { EffectiveFields = body.EffectiveFields.Select(field=>field.Slot==8?Field(8,0x20):field).ToArray() }], [BodyVisual(body)]));
    }
    private static AreaObjectRecord With(AreaObjectRecord body, params (int Slot, long Value)[] values)
    {
        var fields = body.EffectiveFields.Select(field => values.Any(v => v.Slot == field.Slot) ? Field(field.Slot, values.Single(v => v.Slot == field.Slot).Value) : field).ToArray();
        return body with { DirectFields = fields, EffectiveFields = fields };
    }
    private static AreaDrawCommand Drawn(AreaObjectRecord body, int height = 240)
        => AreaScenePlan.Create([], [body], [BodyVisual(body, height: height)]).Commands.Single();
    [TestMethod]
    public void BlendStatesFollowTheOriginalPrecedenceAndAreUnlit()
    {
        var body = Body(0);
        var multiply = Drawn(With(body, (8, 0x40)));
        Assert.IsTrue(multiply.Multiply && multiply.Unlit && !multiply.Additive && multiply.Alpha == 255 && multiply.Blended);
        // Additive wins over multiply and constant alpha and is unlit too (ARC-RENDER-026, ledger BL2).
        var additive = Drawn(With(body, (8, 0x150)));
        Assert.IsTrue(additive.Additive && !additive.Multiply && additive.Unlit && additive.Alpha == 255);
        var constant = Drawn(With(body, (8, 0x101), (10, 95)));
        Assert.IsTrue(constant.Alpha == 95 && constant.Unlit && !constant.Multiply && !constant.Additive && constant.FlipX && constant.Blended);
        var mulOverAlpha = Drawn(With(body, (8, 0x140)));
        Assert.IsTrue(mulOverAlpha.Multiply && mulOverAlpha.Alpha == 255);
        var opaque = Drawn(body);
        Assert.IsTrue(!opaque.Multiply && !opaque.Unlit && opaque.WadeRows == 0 && !opaque.Blended);
        var plan = AreaScenePlan.Create([], [With(body, (8, 0x40))], [BodyVisual(body)]);
        Assert.AreEqual(AreaAmbientChannel.Unlit, AreaAmbientPolicy.Channels(plan, [], [BodyVisual(body)])[multiply.Visual]);
        Assert.AreEqual(AreaAmbientChannel.Outdoor, AreaAmbientPolicy.Channels(AreaScenePlan.Create([], [body], [BodyVisual(body)]), [], [BodyVisual(body)])[opaque.Visual]);
        // Subtract, other alpha modes, an alpha the drawn branch never reads and an out-of-range alpha stay refused.
        foreach (var (blit, alpha) in new[] { (0x20L, 255L), (0x200L, 255L), (0x400L, 255L), (0x40L, 95L), (0x150L, 95L), (0x140L, 95L), (0L, 95L), (0x100L, 256L), (0x100L, -1L) })
            Assert.ThrowsExactly<InvalidOperationException>(() => Drawn(With(body, (8, blit), (10, alpha))), $"blit 0x{blit:X} alpha {alpha}");
        // A frozen body takes the original's frozen tint before any blit blend; it is not drawn as an ordinary body.
        Assert.ThrowsExactly<InvalidOperationException>(() => Drawn(Body(0, flags: 0x10000000)));
    }
    [TestMethod]
    public void WadingBodiesDrawTheirLowerFifteenDestinationRowsOrAFlatBodyWhole()
    {
        Assert.AreEqual(AreaScenePlan.WadingRows, Drawn(Body(0, flags: 0x20000)).WadeRows);
        // Destination rows, after scale: a half-size body still wades in its lowest 15 drawn rows.
        var scaled = AreaScenePlan.Create([], [Body(0, flags: 0x20000, scale: 50)], [BodyVisual(Body(0), height: 240)]).Commands.Single();
        Assert.AreEqual((120, 15), (scaled.Destination.Height, scaled.WadeRows));
        Assert.AreEqual(240, Drawn(Body(0, flags: 0x20004)).WadeRows);
        Assert.AreEqual(10, Drawn(Body(0, flags: 0x20000), height: 10).WadeRows);
        Assert.AreEqual(0, Drawn(Body(0, flags: 0x60000)).WadeRows, "water walking");
        Assert.AreEqual(0, Drawn(With(Body(0, flags: 0x20000), (8, 0x10))).WadeRows, "additive keeps its blend");
        Assert.AreEqual(0, Drawn(With(Body(0, flags: 0x20000), (8, 0x40))).WadeRows, "multiply keeps its blend");
        var ghost = Drawn(With(Body(0, flags: 0x20000), (8, 0x100), (10, 95)));
        Assert.IsTrue(ghost.WadeRows == 15 && ghost.Alpha == 95 && ghost.Blended);
    }
    [TestMethod]
    public void AreaModeRequiresAllRolesAndRejectsCallerActorOverrides()
    {
        var args = new[] { "--mode","crash-site-area","--module","module","--resource","resource2","--actor-resource","resource1","--prototypes","directory","--area-mobile","mobile","--area-effects","resource3","--area-patch","patch","--source-id","synthetic" };
        var parsed = TerrainViewerArguments.Parse(args);
        Assert.AreEqual(TerrainViewerMode.Area,parsed.Mode); Assert.AreEqual("mobile",parsed.AreaMobilePath);
        Assert.AreEqual(ActorDiagnosticAction.Idle,parsed.ActorInput!.Action);
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(args.Concat(new[]{"--actor-facing","5"}).ToArray()));
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(args.Where((_,i)=>i!=10&&i!=11).ToArray()));
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(args.Concat(new[]{"--area-mobile","second"}).ToArray()));
        Assert.AreEqual(("resource3","patch"),(parsed.AreaEffectsPath,parsed.AreaPatchPath));
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(args.Where((_,i)=>i!=12&&i!=13).ToArray()));
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(args.Where((_,i)=>i!=14&&i!=15).ToArray()));
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(args.Concat(new[]{"--area-patch","second"}).ToArray()));
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(["--mode","world-diagnostic","--module","m","--resource","r","--actor-resource","a","--prototypes","p","--area-effects","e","--source-id","s"]));
    }

    [TestMethod]
    public void A_body_whose_art_id_names_no_art_is_suppressed_without_a_command()
    {
        // ARC-DATA-038: a core, unsuppressed scenery body with no visual and ART id 0xFFFFFFFF (ledger NA1) is covered as
        // Suppressed "NoArt" and draws nothing; another type-15 id, or a wall with that id, still requires a visual.
        var drawn = AreaCompositionFixture.Body(0);
        var visual = AreaCompositionFixture.Visual(drawn, 160, 160, 40, 80);
        AreaObjectRecord WithArt(int index, long art, int type = 3)
        {
            var body = AreaCompositionFixture.Body(index);
            var fields = body.EffectiveFields.Append(AreaCompositionFixture.Field(0, art)).ToArray();
            return body with { Type = type, DirectFields = fields, EffectiveFields = fields };
        }
        var none = WithArt(1, unchecked((int)uint.MaxValue));
        var plan = AreaScenePlan.Create([], [drawn, none], [visual]);
        Assert.AreEqual(1, plan.Commands.Count);
        Assert.IsFalse(plan.Commands.Any(c => c.Identity == none.Key));
        var row = plan.Coverage.Single(c => c.Identity == none.Key);
        Assert.AreEqual((RenderDisposition.Suppressed, "NoArt", (AreaVisualKey?)null), (row.Disposition, row.Reason, row.Visual));
        foreach (var refused in new[] { WithArt(2, unchecked((int)0xF0000000u)), WithArt(3, unchecked((int)uint.MaxValue), type: 0) })
            Assert.AreEqual("AREA_VISUAL_REQUIRED", Assert.ThrowsExactly<InvalidOperationException>(() => AreaScenePlan.Create([], [drawn, refused], [visual])).Message);
    }
}
