using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Traversal;

[TestClass]
public sealed class AreaStepTests
{
    [TestMethod]
    public void Tile_name_flags_follow_the_first_slash_and_ignore_other_characters()
    {
        Assert.AreEqual(false, AreaStepRules.TileNameBlocks("aaa/s/n 4"));
        Assert.AreEqual(true, AreaStepRules.TileNameBlocks("aaa/b/p 0"));
        Assert.AreEqual(true, AreaStepRules.TileNameBlocks("aaa/x/f"));
        Assert.AreEqual(false, AreaStepRules.TileNameBlocks("aaa/n b"));
        Assert.AreEqual(false, AreaStepRules.TileNameBlocks("aaa"));
        Assert.AreEqual(false, AreaStepRules.TileNameBlocks("abcd 3"));
        Assert.IsNull(AreaStepRules.TileNameBlocks("ab"));
    }

    [TestMethod]
    public void Tile_name_table_ranks_within_categories_and_requires_anchors()
    {
        var records = new SortedDictionary<int, string> { [0] = "oo0/b", [1] = "oo1", [100] = "nn0", [102] = "nn1/f", [200] = "ii0", [300] = "xx0", [400] = "edge" };
        var table = AreaStepRules.TileNameTable(records)!;
        CollectionAssert.AreEqual(new[] { 2, 2, 1, 1 }, table.Select(c => c.Length).ToArray());
        Assert.AreEqual(true, AreaStepRules.TileBlocks((1u << 22) | 0x100, table));
        Assert.AreEqual(true, AreaStepRules.TileBlocks(0x180, table));
        Assert.AreEqual(false, AreaStepRules.TileBlocks((1u << 22) | 0x180, table));
        Assert.AreEqual(false, AreaStepRules.TileBlocks(0, table));
        Assert.IsNull(AreaStepRules.TileBlocks((5u << 22) | 0x80, table));
        records[399] = "ab"; Assert.IsNull(AreaStepRules.TileNameTable(records));
        records.Remove(399); records.Remove(200); Assert.IsNull(AreaStepRules.TileNameTable(records));
    }

    [TestMethod]
    public void Effect_grammar_admits_add_percent_and_bounds_only()
    {
        var parsed = AreaStepRules.ParseEffect("st +2, maxhps +15%, wp min 3, ST -1, level max 9")!;
        Assert.AreEqual(5, parsed.Count);
        CollectionAssert.AreEqual(new[] { '+', '%', 'n', '+', 'x' }, parsed.Select(p => p.Operator).ToArray());
        Assert.AreEqual("st", parsed[3].Attribute);
        foreach (var refused in new[] { "unknowntoken +1", "st", "st ?3", "st *3", "st /2", "st min -1", "st\t+1", "st +1\nwp +1" })
            Assert.IsNull(AreaStepRules.ParseEffect(refused), refused);
        Assert.AreEqual(0, AreaStepRules.ParseEffect("")!.Count);
    }

    [TestMethod]
    public void Effects_stack_percent_then_add_then_strongest_bounds()
    {
        IReadOnlyList<AreaStepRules.EffectChange>[] stack = [[new("st", '%', 50), new("st", '+', 1)], [new("st", '+', 2), new("st", 'x', 20)]];
        Assert.AreEqual(9, AreaStepRules.ApplyEffects(stack, "st", 4));
        Assert.AreEqual(20, AreaStepRules.ApplyEffects(stack, "st", 13));
        Assert.AreEqual(4, AreaStepRules.ApplyEffects(stack, "wp", 4));
        Assert.AreEqual(-1, AreaStepRules.ApplyEffects([[new("st", '%', -50)]], "st", -3));
        Assert.AreEqual(5, AreaStepRules.ApplyEffects([[new("st", '+', -9), new("st", 'n', 2), new("st", 'n', 5), new("st", 'x', 7), new("st", 'x', 6)]], "st", 4));
    }

    [TestMethod]
    public void Critter_life_is_exact_inside_common_bounds_and_fails_closed_otherwise()
    {
        int[] Stats(int st, int wp, int level, int poison = 0) { var s = new int[28]; s[0] = st; s[6] = wp; s[17] = level; s[24] = poison; return s; }
        var table = new Dictionary<int, IReadOnlyList<AreaStepRules.EffectChange>?> { [70] = [new("st", '+', 4)], [71] = [new("maxhps", '+', 100)], [72] = null };
        var alive = AreaStepRules.Evaluate(new(Stats(8, 8, 1), [70], 0, 2, 10), table);
        Assert.AreEqual((AreaStepRules.Life.Alive, 30L, 30L), (alive.Life, alive.Minimum, alive.Maximum));
        Assert.AreEqual(AreaStepRules.Life.Dead, AreaStepRules.Evaluate(new(Stats(8, 8, 1), [], 0, 0, 32000), table).Life);
        var high = AreaStepRules.Evaluate(new(Stats(25, 8, 1), [], 0, 0, 0), table);
        Assert.AreEqual((8 + 2 * (20 + 1) + 4L, 8 + 2 * (25 + 1) + 4L), (high.Minimum, high.Maximum));
        var floor = AreaStepRules.Evaluate(new(Stats(0, 0, 0), [], 0, -7, 0), table);
        Assert.AreEqual((AreaStepRules.Life.Dead, 0L, 0L), (floor.Life, floor.Minimum, floor.Maximum));
        Assert.AreEqual(8 + 2 * (51 + 1) + 4L, AreaStepRules.Evaluate(new(Stats(1, 8, 90), [], 0, 0, 0), table).Maximum);
        Assert.AreEqual(8 + 2 * (6 + 1) + 4L, AreaStepRules.Evaluate(new(Stats(8, 8, 1, 250), [], 0, 0, 0), table).Minimum);
        Assert.AreEqual(8 + 2 * (5 + 1) + 4L, AreaStepRules.Evaluate(new(Stats(8, 8, 1, 900), [], 0, 0, 0), table).Minimum);
        Assert.AreEqual("CRITTER_MAX_HP_EFFECT_UNSUPPORTED", AreaStepRules.Evaluate(new(Stats(8, 8, 1), [71], 0, 0, 0), table).Reason);
        Assert.AreEqual("CRITTER_EFFECT_UNSUPPORTED", AreaStepRules.Evaluate(new(Stats(8, 8, 1), [72], 0, 0, 0), table).Reason);
        Assert.AreEqual("CRITTER_EFFECT_INDEX_UNSUPPORTED", AreaStepRules.Evaluate(new(Stats(8, 8, 1), [400], 0, 0, 0), table).Reason);
        Assert.AreEqual("CRITTER_STATS_UNRESOLVED", AreaStepRules.Evaluate(new(new int[3], [], 0, 0, 0), table).Reason);
        Assert.AreEqual(AreaStepRules.Life.Alive, AreaStepRules.Evaluate(new(Stats(8, 8, 1), [99], 0, 0, 0), table).Life, "Undefined slots carry no change.");
        var straddle = AreaStepRules.Evaluate(new(Stats(25, 1, 0), [], 0, 0, 50), table);
        Assert.AreEqual(AreaStepRules.Life.Unknown, straddle.Life);
    }

    [TestMethod]
    public void Patch_targets_keep_both_guid_byte_orders_and_count_other_lines()
    {
        var targets = AreaStepRules.PatchTargets("# c\nG_01234567_89AB_CDEF_0123_456789ABCDEF: obj_flag OF_OFF\nA_00000001: origin 0\nbad\n", out var malformed);
        Assert.AreEqual(1, targets.Count); Assert.AreEqual(2, malformed);
        Assert.AreEqual("0123456789ABCDEF0123456789ABCDEF", Convert.ToHexString(targets[0].TextOrder));
        Assert.AreEqual("67452301AB89EFCD0123456789ABCDEF", Convert.ToHexString(targets[0].MixedEndian));
    }

    [TestMethod]
    public void Mes_reader_rejects_duplicates_and_nested_braces()
    {
        var mes = AreaStepRules.ParseMes(Encoding.ASCII.GetBytes("// note\n{0}{a/b}\nloose {5}  {x y}\n"));
        Assert.AreEqual(2, mes.Count); Assert.AreEqual("x y", mes[5]);
        foreach (var bad in new[] { "{1}{a}{1}{b}", "{1}{a{b}", "{x}{a}", "{1} a" })
            Assert.AreEqual(AreaStepInvocationCode.MalformedSource, Throws<AreaStepInvocationException>(() => AreaStepRules.ParseMes(Encoding.ASCII.GetBytes(bad))).Code, bad);
    }

    [TestMethod]
    public void Steps_check_destination_terrain_then_object_legs_without_shoulder_terrain()
    {
        // Admitted sector (1,1): cells 64..127. (66,64) terrain-blocked; blocker object at (64,66); dead critter at (65,66).
        var snapshot = Synthetic(blockedTerrain: [new(66, 64)], blockers: [new(64, 66)], open: [new(65, 66)]);
        Assess(snapshot, new(65, 64), CardinalTraversalDirection.XPlus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "DESTINATION_TERRAIN_BLOCKED");
        Assess(snapshot, new(66, 64), CardinalTraversalDirection.XMinus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, "COMPLETE_NONBLOCKING_EVIDENCE");
        Assess(snapshot, new(65, 65), CardinalTraversalDirection.XPlusYMinus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "DESTINATION_TERRAIN_BLOCKED");
        // The blocked tile (66,64) is a shoulder of both diagonals below: shoulder terrain is never consulted.
        Assess(snapshot, new(65, 64), CardinalTraversalDirection.XPlusYPlus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, "COMPLETE_NONBLOCKING_EVIDENCE");
        Assess(snapshot, new(67, 64), CardinalTraversalDirection.XMinusYPlus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, "COMPLETE_NONBLOCKING_EVIDENCE");
        var shoulder = Assess(snapshot, new(64, 65), CardinalTraversalDirection.XPlusYPlus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "SHOULDER_OBJECT_BLOCKER");
        Assert.AreEqual(AreaStepObjectRole.CounterClockwiseShoulder, shoulder.Objects.Single(o => o.Disposition == CardinalTraversalDisposition.Blocked).Role);
        Assess(snapshot, new(64, 65), CardinalTraversalDirection.YPlus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "DESTINATION_OBJECT_BLOCKER");
        var dead = Assess(snapshot, new(65, 65), CardinalTraversalDirection.YPlus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, "COMPLETE_NONBLOCKING_EVIDENCE");
        Assert.AreEqual(1, dead.Objects.Count);
    }

    [TestMethod]
    public void Unknown_evidence_is_retained_and_blockers_keep_unrelated_gaps()
    {
        var snapshot = Synthetic(unknownTerrain: [new(70, 70)], unknownObjects: [new(71, 71), new(69, 71)], blockers: [new(69, 69)]);
        var unknown = Assess(snapshot, new(70, 71), CardinalTraversalDirection.YMinus, CardinalTraversalCoverage.Unknown, null, "REQUIRED_EVIDENCE_UNAVAILABLE");
        Assert.AreEqual("FIXTURE_TERRAIN_UNKNOWN", unknown.Gaps.Single().ReasonCode);
        Assess(snapshot, new(70, 70), CardinalTraversalDirection.XPlusYPlus, CardinalTraversalCoverage.Unknown, null, "REQUIRED_EVIDENCE_UNAVAILABLE");
        var blocked = Assess(snapshot, new(70, 70), CardinalTraversalDirection.XMinusYPlus, CardinalTraversalCoverage.Unknown, null, "REQUIRED_EVIDENCE_UNAVAILABLE");
        Assert.AreEqual(AreaStepObjectRole.Destination, blocked.Gaps.Single().Role);
        var clean = Assess(snapshot, new(70, 70), CardinalTraversalDirection.XMinusYMinus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "DESTINATION_OBJECT_BLOCKER");
        Assert.AreEqual(0, clean.Gaps.Count);
        var kept = Assess(Synthetic(blockedTerrain: [new(80, 80)], unknownObjects: [new(80, 80)]), new(79, 80), CardinalTraversalDirection.XPlus,
            CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "DESTINATION_TERRAIN_BLOCKED");
        Assert.AreEqual("FIXTURE_OBJECT_UNKNOWN", kept.Gaps.Single().ReasonCode);
        Assert.IsFalse(kept.CompleteCoverage);
        var withGap = Assess(snapshot, new(70, 71), CardinalTraversalDirection.XMinus, CardinalTraversalCoverage.Unknown, null, "REQUIRED_EVIDENCE_UNAVAILABLE");
        Assert.IsFalse(withGap.CompleteCoverage);
    }

    [TestMethod]
    public void Requests_outside_the_profile_are_unsupported_not_fallbacks()
    {
        var snapshot = Synthetic();
        AreaStepCell inside = new(64, 70);
        Assess(snapshot, inside, CardinalTraversalDirection.XMinus, CardinalTraversalCoverage.Unsupported, null, "DESTINATION_OUTSIDE_ADMITTED_AREA");
        Assess(snapshot, new(63, 70), CardinalTraversalDirection.XPlus, CardinalTraversalCoverage.Unsupported, null, "SOURCE_OUTSIDE_ADMITTED_AREA");
        Assert.AreEqual("SAME_CELL_UNSUPPORTED", snapshot.Assess(snapshot.Request(inside, inside, CardinalTraversalDirection.XPlus)).ReasonCode);
        Assert.AreEqual("NONADJACENT_UNSUPPORTED", snapshot.Assess(snapshot.Request(inside, new(66, 70), CardinalTraversalDirection.XPlus)).ReasonCode);
        Assert.AreEqual("DIRECTION_MISMATCH", snapshot.Assess(snapshot.Request(inside, new(65, 70), CardinalTraversalDirection.YPlus)).ReasonCode);
        Assert.AreEqual("EPOCH_MISMATCH", snapshot.Assess(new(snapshot.ProfileId, snapshot.EvidenceSha256, 4, inside, new(65, 70), CardinalTraversalDirection.XPlus, snapshot.DeclaredContext)).ReasonCode);
        Assert.AreEqual("EVIDENCE_IDENTITY_MISMATCH", snapshot.Assess(new(snapshot.ProfileId, new string('0', 64), 3, inside, new(65, 70), CardinalTraversalDirection.XPlus, snapshot.DeclaredContext)).ReasonCode);
        Assert.AreEqual("PROFILE_UNSUPPORTED", snapshot.Assess(new("OTHER", snapshot.EvidenceSha256, 3, inside, new(65, 70), CardinalTraversalDirection.XPlus, snapshot.DeclaredContext)).ReasonCode);
        var flying = new CardinalTraversalContext("ISOLATED_PC_CRITTER_ART_NONPOLYMORPH", 15, 2, 0, 0, false, false, CardinalTraversalActorState.Floating, 0);
        Assert.AreEqual("CONTEXT_UNSUPPORTED", snapshot.Assess(new(snapshot.ProfileId, snapshot.EvidenceSha256, 3, inside, new(65, 70), CardinalTraversalDirection.XPlus, flying)).ReasonCode);
        var path = new CardinalTraversalContext("ISOLATED_PC_CRITTER_ART_NONPOLYMORPH", 15, 2, 0, 0x1000, false, false, CardinalTraversalActorState.OrdinaryGround, 0);
        var unavailable = snapshot.Assess(new(snapshot.ProfileId, snapshot.EvidenceSha256, 3, inside, new(65, 70), CardinalTraversalDirection.XPlus, path));
        Assert.AreEqual((CardinalTraversalCoverage.Unknown, "PATH_BRANCH_UNAVAILABLE"), (unavailable.Coverage, unavailable.ReasonCode));
    }

    [TestMethod]
    public void Structural_inspection_reads_masks_tile_names_and_registered_objects_from_the_area_snapshot()
    {
        var sector = Sector(Record(Id(0)));
        var moduleBytes = Dat(("maps/test/67108865.sec", sector));
        using var fixture = new Fixture([(67108865, [])], moduleBytes: moduleBytes);
        var area = fixture.Owner.LoadStrict();
        using var module = DatArchive.Open(new MemoryStream(moduleBytes));
        using var names = DatArchive.Open(new MemoryStream(Dat(("art/tile/tilename.mes", Encoding.ASCII.GetBytes("{0}{grs/n}{100}{rck}{200}{flr}{300}{stn/s/n 4}{400}{edge}")))));
        using var effects = DatArchive.Open(new MemoryStream(Dat(("rules/effect.mes", Encoding.ASCII.GetBytes("{50}{st +1}")))));
        var snapshot = AreaStepInspector.Inspect(area, module, names, effects, null, ReadOnlyMemory<byte>.Empty, 3);
        Assert.AreEqual(AreaStepInputKind.Structural, snapshot.InputIdentity.Kind);
        Assert.AreEqual(4096, snapshot.AdmittedCells);
        Assert.AreEqual(0, snapshot.TerrainUnknownCells);
        Assert.AreEqual(0, snapshot.TerrainBlockedCells, "Placeholder 0xAA0003 keeps the zeroed mask; the multi-slash name does not block.");
        Assert.AreEqual(1, snapshot.ObjectBlockerCells);
        var blocked = snapshot.Assess(snapshot.Request(new(65, 64), new(64, 64), CardinalTraversalDirection.XMinus));
        Assert.AreEqual((CardinalTraversalDisposition.Blocked, "DESTINATION_OBJECT_BLOCKER"), (blocked.Disposition, blocked.ReasonCode));
        Assert.AreEqual(AreaStepTerrainSource.TileName, blocked.DestinationTerrain!.Source);
        var open = snapshot.Assess(snapshot.Request(new(65, 64), new(66, 64), CardinalTraversalDirection.XPlus));
        Assert.IsTrue(open.CompleteCoverage);
        Assert.AreEqual(snapshot.EvidenceSha256, AreaStepInspector.Inspect(area, module, names, effects, null, ReadOnlyMemory<byte>.Empty, 3).EvidenceSha256);

        using var blocking = DatArchive.Open(new MemoryStream(Dat(("art/tile/tilename.mes", Encoding.ASCII.GetBytes("{0}{grs}{100}{rck}{200}{flr}{300}{blk/b}")))));
        var walled = AreaStepInspector.Inspect(area, module, blocking, effects, null, ReadOnlyMemory<byte>.Empty, 3);
        Assert.AreEqual(4096, walled.TerrainBlockedCells);
        Assert.AreNotEqual(snapshot.EvidenceSha256, walled.EvidenceSha256);
        using var broken = DatArchive.Open(new MemoryStream(Dat(("art/tile/tilename.mes", Encoding.ASCII.GetBytes("{0}{grs}{100}{rck}{300}{stn}")))));
        Assert.AreEqual(4096, AreaStepInspector.Inspect(area, module, broken, effects, null, ReadOnlyMemory<byte>.Empty, 3).TerrainUnknownCells);
        using var missing = DatArchive.Open(new MemoryStream(Dat(("other.mes", Encoding.ASCII.GetBytes("{0}{x}")))));
        Assert.AreEqual(AreaStepInvocationCode.InvalidSelection, Throws<AreaStepInvocationException>(() => AreaStepInspector.Inspect(area, module, missing, effects, null, ReadOnlyMemory<byte>.Empty, 3)).Code);
        using var otherModule = DatArchive.Open(new MemoryStream(Dat(("maps/test/67108865.sec", Sector()))));
        Assert.AreEqual(AreaStepInvocationCode.FingerprintMismatch, Throws<AreaStepInvocationException>(() => AreaStepInspector.Inspect(area, otherModule, names, effects, null, ReadOnlyMemory<byte>.Empty, 3)).Code);
    }

    [TestMethod]
    public void Mobile_records_named_by_map_patch_stay_unknown_in_either_byte_order()
    {
        // Own ID bytes 8..23 are 2A 00 .. 00; both GUID spellings below name that byte run in one of the two orders.
        var mobile = Record(Id(1, 42), 64 + (64L << 32));
        var mobileFile = new byte[16].Concat(mobile).ToArray();
        var moduleBytes = Dat(("maps/test/67108865.sec", Sector()), ("maps/test/0.mob", mobile));
        using var fixture = new Fixture([(67108865, [])], aggregate: [mobile], moduleBytes: moduleBytes);
        var area = fixture.Owner.LoadStrict();
        using var module = DatArchive.Open(new MemoryStream(moduleBytes));
        using var names = DatArchive.Open(new MemoryStream(Dat(("art/tile/tilename.mes", Encoding.ASCII.GetBytes("{0}{grs}{100}{rck}{200}{flr}{300}{stn}")))));
        using var effects = DatArchive.Open(new MemoryStream(Dat(("rules/effect.mes", Encoding.ASCII.GetBytes("{50}{st +1}")))));
        AreaStepSnapshot With(string patch)
        {
            using var archive = DatArchive.Open(new MemoryStream(Dat(("rules/map.patch", Encoding.ASCII.GetBytes(patch)))));
            return AreaStepInspector.Inspect(area, module, names, effects, archive, mobileFile, 3);
        }
        var request = (From: new AreaStepCell(65, 64), To: new AreaStepCell(64, 64));
        var unpatched = With("# none\n");
        Assert.AreEqual("DESTINATION_OBJECT_BLOCKER", unpatched.Assess(unpatched.Request(request.From, request.To, CardinalTraversalDirection.XMinus)).ReasonCode);
        foreach (var line in new[] { "G_2A000000_0000_0000_0000_000000000000: obj_flag OF_OFF", "G_0000002A_0000_0000_0000_000000000000: obj_flag OF_OFF" })
        {
            var patched = With(line + "\n");
            Assert.AreEqual(1, patched.UnknownObjects, line);
            var result = patched.Assess(patched.Request(request.From, request.To, CardinalTraversalDirection.XMinus));
            Assert.AreEqual(("MAP_PATCH_TARGETS_OBJECT", CardinalTraversalCoverage.Unknown), (result.Gaps.Single().ReasonCode, result.Coverage));
        }
        Assert.AreEqual(1, With("A_00000001: origin 0\n").UnknownObjects, "Unsupported patch lines leave every mobile unresolved.");
        Assert.AreEqual(AreaStepInvocationCode.FingerprintMismatch,
            Throws<AreaStepInvocationException>(() => AreaStepInspector.Inspect(area, module, names, effects, null, mobileFile[..^1], 3)).Code);
    }

    private static AreaStepAssessment Assess(AreaStepSnapshot snapshot, AreaStepCell from, CardinalTraversalDirection direction,
        CardinalTraversalCoverage coverage, CardinalTraversalDisposition? disposition, string reason)
    {
        (int Dx, int Dy)[] deltas = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        var to = new AreaStepCell(from.X + deltas[(int)direction].Dx, from.Y + deltas[(int)direction].Dy);
        var result = snapshot.Assess(snapshot.Request(from, to, direction));
        Assert.AreEqual((coverage, disposition, reason), (result.Coverage, result.Disposition, result.ReasonCode), $"{from}->{to}");
        return result;
    }

    private static AreaStepSnapshot Synthetic(AreaStepCell[]? blockedTerrain = null, AreaStepCell[]? unknownTerrain = null,
        AreaStepCell[]? blockers = null, AreaStepCell[]? open = null, AreaStepCell[]? unknownObjects = null)
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 64; x < 128; x++)
            for (var y = 64; y < 128; y++)
            {
                AreaStepCell cell = new(x, y);
                terrain[cell] = unknownTerrain?.Contains(cell) == true
                    ? new(cell, 0, CardinalTraversalCoverage.Unknown, null, null, "FIXTURE_TERRAIN_UNKNOWN")
                    : new(cell, 0, CardinalTraversalCoverage.Supported, blockedTerrain?.Contains(cell) == true ? CardinalTraversalDisposition.Blocked : CardinalTraversalDisposition.Open,
                        AreaStepTerrainSource.TileName, "FIXTURE");
            }
        var objects = new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>();
        var index = 0;
        AreaObjectKey Key() => new("fixture", 1, AreaSourceKind.Static, 67108865, index++, 0, "id", 1);
        foreach (var cell in blockers ?? []) objects[cell] = [new(Key(), cell, 3, CardinalTraversalCoverage.Supported, CardinalTraversalApplicability.Applicable, CardinalTraversalDisposition.Blocked, "OBJECT_BLOCKS")];
        foreach (var cell in open ?? []) objects[cell] = [new(Key(), cell, 16, CardinalTraversalCoverage.Supported, CardinalTraversalApplicability.Applicable, CardinalTraversalDisposition.Open, "CRITTER_DEAD", -5, -5)];
        foreach (var cell in unknownObjects ?? []) objects[cell] = [new(Key(), cell, 16, CardinalTraversalCoverage.Unknown, CardinalTraversalApplicability.Unknown, null, "FIXTURE_OBJECT_UNKNOWN")];
        return new(new(AreaStepInputKind.Structural, "fixture", [new("module", 1, "aa")], "f00d"), 3, [(1, 1)], terrain, objects);
    }
}
