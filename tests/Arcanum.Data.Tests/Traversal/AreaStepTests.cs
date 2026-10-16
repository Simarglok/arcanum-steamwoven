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

    [TestMethod]
    public void Object_evaluation_reads_the_documented_slots_and_filters()
    {
        var table = new Dictionary<int, IReadOnlyList<AreaStepRules.EffectChange>?> { [70] = [new("st", '+', 4)] };
        AreaStepObjectEvaluation Eval(int type, params AreaFieldValue[] fields) =>
            AreaStepInspector.Evaluate(Object(type, fields), new(64, 64), default, [], 0, table);
        Assert.AreEqual((CardinalTraversalDisposition.Blocked, "OBJECT_BLOCKS"), Disposition(Eval(3, Value(18, 0))));
        Assert.AreEqual((CardinalTraversalDisposition.Open, "NO_BLOCK_FLAG"), Disposition(Eval(2, Value(18, 0x400))));
        Assert.AreEqual((CardinalTraversalDisposition.Open, "NO_BLOCK_FLAG"), Disposition(Eval(17, Value(18, 0x400))));
        Assert.AreEqual((CardinalTraversalDisposition.Blocked, "OBJECT_BLOCKS"), Disposition(Eval(4, Value(18, 0x40))));
        foreach (var (type, flags, reason) in new[] { (3, 0x1400L, "INVENTORY_NOT_LISTED"), (3, 0x1L, "DESTROYED_OR_OFF_NOT_LISTED"), (16, 0x2L, "DESTROYED_OR_OFF_NOT_LISTED"),
            (5, 0L, "ITEM_TYPE_NOT_QUERIED"), (14, 0L, "ITEM_TYPE_NOT_QUERIED") })
        {
            var excluded = Eval(type, Value(18, flags));
            Assert.AreEqual((CardinalTraversalCoverage.Supported, CardinalTraversalApplicability.NotApplicable, (CardinalTraversalDisposition?)null, reason),
                (excluded.Coverage, excluded.Applicability, excluded.Disposition, excluded.Reason), reason);
        }
        foreach (var (type, fields, reason) in new[] { (0, new[] { Value(18, 0) }, "WALL_SPELL_FLAGS_UNRESOLVED"), (1, [Value(18, 0)], "WALL_OR_PORTAL_UNSUPPORTED"),
            (15, [Value(18, 0)], "PC_RECORD_UNSUPPORTED"), (3, [Value(19, 0)], "FLAGS_UNRESOLVED"), (16, [Value(18, 0)], "CRITTER_STATS_UNRESOLVED") })
        {
            var unknown = Eval(type, fields);
            Assert.AreEqual((CardinalTraversalCoverage.Unknown, reason), (unknown.Coverage, unknown.Reason), reason);
        }
        // NPC: stat base slot 38 (28 entries), effects slot 46, HP points/adjustment/damage slots 26/27/28.
        AreaFieldValue[] Npc(long damage, AreaFieldValue? effects, int statSlot = 38, int damageSlot = 28) =>
            [Value(18, 0), Value(26, 0), Value(27, 2), Value(damageSlot, damage), Stats(statSlot, 8, 8, 1), .. effects is null ? Array.Empty<AreaFieldValue>() : [effects]];
        var alive = Eval(16, Npc(10, ArrayValue(46, 70)));
        Assert.AreEqual((CardinalTraversalDisposition.Blocked, "CRITTER_ALIVE", 30L, 30L), (alive.Disposition, alive.Reason, alive.HpMinimum, alive.HpMaximum));
        Assert.AreEqual((CardinalTraversalDisposition.Open, "CRITTER_DEAD"), Disposition(Eval(16, Npc(32000, null))));
        Assert.AreEqual((CardinalTraversalDisposition.Blocked, "CRITTER_ALIVE"), Disposition(Eval(16, Npc(10, new(46, AreaPresence.Null, AreaValueOrigin.Prototype, null, [], null)))));
        Assert.AreEqual("CRITTER_STATS_UNRESOLVED", Eval(16, Npc(10, null, statSlot: 39)).Reason);
        Assert.AreEqual("CRITTER_STATS_UNRESOLVED", Eval(16, Npc(10, null, damageSlot: 29)).Reason);
        Assert.AreEqual("CRITTER_STATS_UNRESOLVED", Eval(16, Npc(10, new(46, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(1, [70])], null))).Reason);

        var record = new byte[] { 9, 0x2A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 9 };
        var text = AreaStepRules.PatchTargets("G_2A000000_0000_0000_0000_000000000000: x\n", out _);
        var mixed = AreaStepRules.PatchTargets("G_0000002A_0000_0000_0000_000000000000: x\n", out _);
        var other = AreaStepRules.PatchTargets("G_2B000000_0000_0000_0000_000000000000: x\n", out _);
        Assert.AreNotEqual(Convert.ToHexString(text[0].TextOrder), Convert.ToHexString(mixed[0].TextOrder));
        var mobile = Object(3, [Value(18, 0)], AreaSourceKind.Mobile);
        Assert.AreEqual("MAP_PATCH_TARGETS_OBJECT", AreaStepInspector.Evaluate(mobile, new(64, 64), record, text, 0, table).Reason);
        Assert.AreEqual("MAP_PATCH_TARGETS_OBJECT", AreaStepInspector.Evaluate(mobile, new(64, 64), record, mixed, 0, table).Reason);
        Assert.AreEqual("OBJECT_BLOCKS", AreaStepInspector.Evaluate(mobile, new(64, 64), record, other, 0, table).Reason);
        Assert.AreEqual("MAP_PATCH_LINE_UNSUPPORTED", AreaStepInspector.Evaluate(mobile, new(64, 64), record, other, 1, table).Reason);
        Assert.AreEqual("OBJECT_BLOCKS", AreaStepInspector.Evaluate(Object(3, [Value(18, 0)]), new(64, 64), record, text, 1, table).Reason, "Statics are loaded after the patch.");
    }

    [TestMethod]
    public void Inspection_requires_a_strict_publication_and_located_core_objects_and_skips_others()
    {
        var noBlock = Fields((1, Location(0)), (18, I32Bytes(0x400)));
        var moduleBytes = Dat(("maps/test/67108865.sec", Sector(Record(Id(0)), noBlock)));
        using var fixture = new Fixture([(67108865, [])], moduleBytes: moduleBytes);
        var area = fixture.Owner.LoadStrict();
        using var module = DatArchive.Open(new MemoryStream(moduleBytes));
        using var names = DatArchive.Open(new MemoryStream(Dat(("art/tile/tilename.mes", Encoding.ASCII.GetBytes("{0}{grs}{100}{rck}{200}{flr}{300}{stn}")))));
        using var effects = DatArchive.Open(new MemoryStream(Dat(("rules/effect.mes", Encoding.ASCII.GetBytes("{50}{st +1}")))));
        AreaStepSnapshot Inspect(AreaSnapshot snapshot) => AreaStepInspector.Inspect(snapshot, module, names, effects, null, ReadOnlyMemory<byte>.Empty, 3);
        var baseline = Inspect(area);
        Assert.AreEqual((1, 0), (baseline.ObjectBlockerCells, baseline.UnknownObjects), "Two registrations share (64,64); NO_BLOCK does not hide the blocker.");

        AreaSnapshot Copy(IEnumerable<AreaSectorRecord>? sectors = null, IEnumerable<AreaObjectRecord>? objects = null, bool publish = true)
        {
            var copy = new AreaSnapshot(new object(), 1, fixture.Plan, sectors ?? area.Sectors, area.Terrain, objects ?? area.Objects, area.Visuals, area.Reconciliation, area.Endpoints);
            if (publish) copy.MarkStrictlyPublished();
            return copy;
        }
        Assert.AreEqual(AreaStepInvocationCode.InvalidSelection, Throws<AreaStepInvocationException>(() => Inspect(Copy(publish: false))).Code);
        var outside = area.Objects[0] with { WorldX = 10, WorldY = 70 };
        Assert.AreEqual(0, Inspect(Copy(objects: [outside])).ObjectBlockerCells, "An object outside the admitted sectors is not registered here.");
        var unlocated = Throws<AreaStepInvocationException>(() => Inspect(Copy(objects: [area.Objects[0] with { WorldX = null }])));
        Assert.AreEqual((AreaStepInvocationCode.MalformedSource, AreaStepInvocationStage.Objects), (unlocated.Code, unlocated.Stage));
        var forged = Inspect(Copy(sectors: area.Sectors.Select(s => s.Scope == AreaScope.Core ? s with { Placeholder = 0xAA0004 } : s)));
        Assert.AreEqual(4096, forged.TerrainUnknownCells, "A version that promises a mask the payload lacks is never silently clear.");
        var moved = Throws<AreaStepInvocationException>(() => Inspect(Copy(sectors: area.Sectors.Select(s => s with { DatOrdinal = 7 }))));
        Assert.AreEqual((AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Terrain), (moved.Code, moved.Stage));
    }

    [TestMethod]
    public void Serialized_masks_from_version_four_sectors_block_their_bits()
    {
        var mask = new byte[512]; mask[0] = 0b0000_0101; // ordinals 0 and 2 → cells (64,64) and (66,64).
        var moduleBytes = Dat(("maps/test/67108865.sec", MaskedSector(mask)));
        using var fixture = new Fixture([(67108865, [])], moduleBytes: moduleBytes);
        var inspection = fixture.Owner.Inspect(); Assert.IsTrue(inspection.IsComplete, string.Join(',', inspection.Issues)); var area = fixture.Owner.LoadStrict();
        using var module = DatArchive.Open(new MemoryStream(moduleBytes));
        using var names = DatArchive.Open(new MemoryStream(Dat(("art/tile/tilename.mes", Encoding.ASCII.GetBytes("{0}{grs}{100}{rck}{200}{flr}{300}{stn}")))));
        using var effects = DatArchive.Open(new MemoryStream(Dat(("rules/effect.mes", Encoding.ASCII.GetBytes("{50}{st +1}")))));
        var snapshot = AreaStepInspector.Inspect(area, module, names, effects, null, ReadOnlyMemory<byte>.Empty, 3);
        Assert.AreEqual((2, 0), (snapshot.TerrainBlockedCells, snapshot.TerrainUnknownCells));
        var blocked = snapshot.Assess(snapshot.Request(new(65, 64), new(66, 64), CardinalTraversalDirection.XPlus));
        Assert.AreEqual((CardinalTraversalDisposition.Blocked, AreaStepTerrainSource.SerializedMask), (blocked.Disposition, blocked.DestinationTerrain!.Source));
    }

    [TestMethod]
    public void Non_rectangular_admission_refuses_shoulders_and_invalid_directions()
    {
        var snapshot = Synthetic(sectors: [(1, 1), (2, 1), (1, 2)]);
        Assess(snapshot, new(127, 128), CardinalTraversalDirection.XPlusYMinus, CardinalTraversalCoverage.Unsupported, null, "SHOULDER_OUTSIDE_ADMITTED_AREA");
        Assess(snapshot, new(127, 127), CardinalTraversalDirection.XPlus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, "COMPLETE_NONBLOCKING_EVIDENCE");
        Assert.AreEqual("DIRECTION_UNSUPPORTED", snapshot.Assess(snapshot.Request(new(64, 64), new(65, 64), (CardinalTraversalDirection)8)).ReasonCode);
        var far = snapshot.Assess(snapshot.Request(new(64, 64), new(long.MinValue, 64), CardinalTraversalDirection.XMinus));
        Assert.AreEqual("DESTINATION_OUTSIDE_ADMITTED_AREA", far.ReasonCode, "Extreme coordinates are refused without arithmetic overflow.");
        Assert.AreEqual("SOURCE_OUTSIDE_ADMITTED_AREA", snapshot.Assess(snapshot.Request(new(long.MaxValue, 0), new(64, 64), CardinalTraversalDirection.XMinus)).ReasonCode);
    }

    [TestMethod]
    public void Walls_block_the_edge_they_sit_on_from_both_sides_and_leave_their_tile_walkable()
    {
        // A wall on (80,80) on its y-1 edge (7), and an opening piece on (90,80) on its x+1 edge (5).
        var snapshot = Synthetic(walls: [((80, 80), 7, false), ((90, 80), 5, true)]);
        Assert.AreEqual(2, snapshot.WallEdges);
        var leaving = Assess(snapshot, new(80, 80), CardinalTraversalDirection.YMinus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "WALL_EDGE_BLOCKED");
        Assert.IsNotNull(leaving.WallBlocker);
        Assess(snapshot, new(80, 79), CardinalTraversalDirection.YPlus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "WALL_EDGE_BLOCKED");
        Assess(snapshot, new(79, 80), CardinalTraversalDirection.XPlus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, "COMPLETE_NONBLOCKING_EVIDENCE");
        Assess(snapshot, new(80, 80), CardinalTraversalDirection.XPlus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, "COMPLETE_NONBLOCKING_EVIDENCE");
        Assess(snapshot, new(80, 80), CardinalTraversalDirection.YPlus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, "COMPLETE_NONBLOCKING_EVIDENCE");
        // Diagonals check both L-shaped paths: any leg across the wall edge blocks.
        Assess(snapshot, new(80, 80), CardinalTraversalDirection.XPlusYMinus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "WALL_EDGE_BLOCKED");
        Assess(snapshot, new(79, 80), CardinalTraversalDirection.XPlusYMinus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "WALL_EDGE_BLOCKED");
        Assess(snapshot, new(81, 79), CardinalTraversalDirection.XMinusYPlus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "WALL_EDGE_BLOCKED");
        Assess(snapshot, new(78, 80), CardinalTraversalDirection.XMinusYMinus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, "COMPLETE_NONBLOCKING_EVIDENCE");
        // An opening piece is never guessed open or closed.
        var opening = Assess(snapshot, new(90, 80), CardinalTraversalDirection.XPlus, CardinalTraversalCoverage.Unknown, null, "REQUIRED_EVIDENCE_UNAVAILABLE");
        Assert.AreEqual("WALL_OPENING_PIECE_UNSUPPORTED", opening.Gaps.Single().ReasonCode);
        // Terrain is still decided first.
        var terrain = Synthetic(blockedTerrain: [new(80, 79)], walls: [((80, 80), 7, false)]);
        Assess(terrain, new(80, 80), CardinalTraversalDirection.YMinus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "DESTINATION_TERRAIN_BLOCKED");
    }

    [TestMethod]
    public void Wall_objects_are_edge_components_not_destination_blockers()
    {
        var table = new Dictionary<int, IReadOnlyList<AreaStepRules.EffectChange>?>();
        var wall = Object(0, [Value(0, 0x10b02800), Value(18, 0x4000), Value(19, 0)]);
        Assert.AreEqual((CardinalTraversalApplicability.NotApplicable, AreaStepInspector.WallEdgeReason),
            (AreaStepInspector.Evaluate(wall, new(64, 64), default, [], 0, table).Applicability, AreaStepInspector.Evaluate(wall, new(64, 64), default, [], 0, table).Reason));
        Assert.AreEqual("DESTROYED_OR_OFF_NOT_LISTED", AreaStepInspector.Evaluate(Object(0, [Value(0, 0x10b02800), Value(18, 0x2)]), new(64, 64), default, [], 0, table).Reason);
        Assert.AreEqual("WALL_PASSWALLED_NOT_LISTED", AreaStepInspector.Evaluate(Object(0, [Value(0, 0x10b02800), Value(18, 0), Value(19, 0x100000)]), new(64, 64), default, [], 0, table).Reason);
        Assert.AreEqual("WALL_SPELL_FLAGS_UNRESOLVED", AreaStepInspector.Evaluate(Object(0, [Value(0, 0x10b02800), Value(18, 0)]), new(64, 64), default, [], 0, table).Reason);
        Assert.AreEqual("WALL_ART_UNRESOLVED", AreaStepInspector.Evaluate(Object(0, [Value(0, 0x40000000), Value(18, 0), Value(19, 0)]), new(64, 64), default, [], 0, table).Reason);
        Assert.AreEqual("WALL_OR_PORTAL_UNSUPPORTED", AreaStepInspector.Evaluate(Object(1, [Value(18, 0)]), new(64, 64), default, [], 0, table).Reason);
    }

    [TestMethod]
    public void Structural_inspection_registers_wall_edges_from_rotation_piece_and_flags()
    {
        var moduleBytes = Dat(("maps/test/67108865.sec", Sector()));
        using var fixture = new Fixture([(67108865, [])], moduleBytes: moduleBytes);
        var area = fixture.Owner.LoadStrict();
        using var module = DatArchive.Open(new MemoryStream(moduleBytes));
        using var names = DatArchive.Open(new MemoryStream(Dat(("art/tile/tilename.mes", Encoding.ASCII.GetBytes("{0}{grs}{100}{rck}{200}{flr}{300}{stn}")))));
        using var effects = DatArchive.Open(new MemoryStream(Dat(("rules/effect.mes", Encoding.ASCII.GetBytes("{50}{st +1}")))));
        static uint Wall(int rotation, int piece = 0) => 0x1000_0000u | ((uint)piece << 14) | ((uint)rotation << 11);
        AreaObjectRecord At(int x, int y, uint art, long flags = 0, long spell = 0, int index = 0) =>
            Object(0, [Value(0, art), Value(18, flags), Value(19, spell)]) with { WorldX = x, WorldY = y, Key = Object(0, []).Key with { SourceIndex = index } };
        var objects = new[]
        {
            At(80, 80, Wall(6), index: 1),              // rotation 6 rounds up to edge 7 (y-1)
            At(70, 70, Wall(0), index: 2),              // rotation 0 rounds up to edge 1 (x-1)
            At(90, 90, Wall(5, piece: 13), index: 3),   // opening piece on edge 5
            At(100, 100, Wall(7), flags: 0x1, index: 4), // destroyed: not listed
            At(110, 110, Wall(7), spell: 0x100000, index: 5), // pass-walled: not listed
            At(120, 120, 0x40000000, index: 6),         // not wall art: Unknown, a source-tile gap
        };
        var copy = new AreaSnapshot(new object(), 1, fixture.Plan, area.Sectors, area.Terrain, objects, area.Visuals, area.Reconciliation, area.Endpoints);
        copy.MarkStrictlyPublished();
        var snapshot = AreaStepInspector.Inspect(copy, module, names, effects, null, ReadOnlyMemory<byte>.Empty, 3);
        Assert.AreEqual(3, snapshot.WallEdges);
        Assess(snapshot, new(80, 80), CardinalTraversalDirection.YMinus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "WALL_EDGE_BLOCKED");
        Assess(snapshot, new(70, 70), CardinalTraversalDirection.XMinus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "WALL_EDGE_BLOCKED");
        Assess(snapshot, new(69, 70), CardinalTraversalDirection.XPlus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "WALL_EDGE_BLOCKED");
        Assess(snapshot, new(90, 90), CardinalTraversalDirection.XPlus, CardinalTraversalCoverage.Unknown, null, "REQUIRED_EVIDENCE_UNAVAILABLE");
        Assess(snapshot, new(100, 100), CardinalTraversalDirection.YMinus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, "COMPLETE_NONBLOCKING_EVIDENCE");
        Assess(snapshot, new(110, 110), CardinalTraversalDirection.YMinus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, "COMPLETE_NONBLOCKING_EVIDENCE");
        var leaving = Assess(snapshot, new(120, 120), CardinalTraversalDirection.XPlus, CardinalTraversalCoverage.Unknown, null, "REQUIRED_EVIDENCE_UNAVAILABLE");
        Assert.AreEqual("WALL_ART_UNRESOLVED", leaving.Gaps.Single().ReasonCode);
        // A blocker keeps an unrelated opening gap; wall edges change the digest only when present.
        var both = Synthetic(walls: [((80, 80), 7, false), ((81, 79), 1, true)]);
        var blocked = Assess(both, new(80, 80), CardinalTraversalDirection.XPlusYMinus, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "WALL_EDGE_BLOCKED");
        Assert.AreEqual("WALL_OPENING_PIECE_UNSUPPORTED", blocked.Gaps.Single().ReasonCode);
        var without = AreaStepInspector.Inspect(Copy(objects.Take(0)), module, names, effects, null, ReadOnlyMemory<byte>.Empty, 3);
        var withWalls = snapshot.EvidenceSha256;
        Assert.AreNotEqual(withWalls, without.EvidenceSha256);
        Assert.AreEqual(without.EvidenceSha256, AreaStepInspector.Inspect(Copy([]), module, names, effects, null, ReadOnlyMemory<byte>.Empty, 3).EvidenceSha256);

        AreaSnapshot Copy(IEnumerable<AreaObjectRecord> list)
        {
            var c = new AreaSnapshot(new object(), 1, fixture.Plan, area.Sectors, area.Terrain, list, area.Visuals, area.Reconciliation, area.Endpoints);
            c.MarkStrictlyPublished(); return c;
        }
    }

    private static (CardinalTraversalDisposition?, string) Disposition(AreaStepObjectEvaluation e) => (e.Disposition, e.Reason);
    private static AreaFieldValue Value(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue ArrayValue(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((v, i) => new AreaSparseElement(i, [v]))], null);
    private static AreaFieldValue Stats(int slot, int st, int wp, int level)
    {
        var values = new long[28]; values[0] = st; values[6] = wp; values[17] = level; return ArrayValue(slot, values);
    }
    private static AreaObjectRecord Object(int type, AreaFieldValue[] fields, AreaSourceKind kind = AreaSourceKind.Static) =>
        new(new("fixture", 1, kind, kind == AreaSourceKind.Static ? 67108865 : null, 0, 0, "id", 18), 119, type, 7001, "p", "p", "r", "s", AreaScope.Core,
            0, 64, 64, AreaDisposition.BodyEligibleConditionally, fields, fields, true, true, true);
    private static byte[] Fields(params (int Slot, byte[] Bytes)[] values)
    {
        using var s = new MemoryStream(); I32(s, 119); s.Write(Id(1, 7001)); s.Write(Id(0)); I32(s, 3);
        s.WriteByte((byte)values.Length); s.WriteByte(0);
        I32(s, values.Where(v => v.Slot < 32).Aggregate(0, (mask, v) => mask | 1 << v.Slot));
        I32(s, values.Where(v => v.Slot is >= 32 and < 36).Aggregate(0, (mask, v) => mask | 1 << (v.Slot - 32)));
        I32(s, values.Where(v => v.Slot >= 36).Aggregate(0, (mask, v) => mask | 1 << (v.Slot - 36)));
        foreach (var v in values.OrderBy(v => v.Slot)) s.Write(v.Bytes); return s.ToArray();
    }
    private static byte[] Location(long value) { using var s = new MemoryStream(); s.WriteByte(1); I64(s, value); return s.ToArray(); }
    private static byte[] I32Bytes(int value) { using var s = new MemoryStream(); I32(s, value); return s.ToArray(); }
    private static byte[] MaskedSector(byte[] mask)
    {
        // The fixture's version-3 layout (core townmap 2) followed by the version-4 block list.
        using var s = new MemoryStream(); I32(s, 0); s.Write(new byte[16384]); I32(s, 1); I32(s, 0xAA0004);
        I32(s, 0); s.Write(new byte[12]); I32(s, 2); s.Write(new byte[20]);
        s.Write(mask); I32(s, 0); return s.ToArray();
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
        AreaStepCell[]? blockers = null, AreaStepCell[]? open = null, AreaStepCell[]? unknownObjects = null, (long X, long Y)[]? sectors = null,
        ((long X, long Y) Cell, int Edge, bool Opening)[]? walls = null)
    {
        sectors ??= [(1, 1)];
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        foreach (var (sx, sy) in sectors)
        for (var x = sx * 64; x < sx * 64 + 64; x++)
            for (var y = sy * 64; y < sy * 64 + 64; y++)
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
        var edges = (walls ?? []).GroupBy(w => new AreaStepCell(w.Cell.X, w.Cell.Y))
            .ToDictionary(g => g.Key, g => g.Select(w => new AreaStepWallEdge(Key(), g.Key, w.Edge, w.Opening)).ToArray());
        return new(new(AreaStepInputKind.Structural, "fixture", [new("module", 1, "aa")], "f00d"), 3, sectors, terrain, objects, edges);
    }
}
