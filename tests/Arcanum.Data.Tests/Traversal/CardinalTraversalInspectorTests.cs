using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Traversal.CardinalTraversalFixture;

namespace Arcanum.Data.Tests.Traversal;

[TestClass]
public sealed class CardinalTraversalInspectorTests
{
    internal static CardinalTraversalRequest Request(CardinalTraversalSnapshot s, CardinalTraversalCell? from = null, CardinalTraversalCell? to = null,
        CardinalTraversalDirection direction = CardinalTraversalDirection.XMinus, CardinalTraversalContext? context = null,
        string profile = "BASE_SECTOR_1452_1290", long epoch = 7, CardinalTraversalInputIdentity? identity = null, int sx = 1452, int sy = 1290) =>
        new(profile, identity ?? s.InputIdentity, epoch, sx, sy, from ?? new(6, 1), to ?? new(5, 1), direction, context ?? s.DeclaredContext);
    internal static CardinalTraversalContext Context(string id = "ISOLATED_PC_CRITTER_ART_NONPOLYMORPH", int kind = 15, int art = 2,
        uint spell = 0, uint path = 0, bool polymorph = false, bool editor = false, CardinalTraversalActorState state = CardinalTraversalActorState.OrdinaryGround, int supplements = 0) =>
        new(id, kind, art, spell, path, polymorph, editor, state, supplements);
    internal static void Result(CardinalTraversalAssessment a, string coverage, string? disposition, bool complete)
    {
        Assert.AreEqual(coverage, a.Coverage.ToString()); Assert.AreEqual(disposition, a.Disposition?.ToString());
        Assert.AreEqual(complete, a.StepEvidence.CompleteCoverage);
        Assert.AreEqual(a.Coverage == CardinalTraversalCoverage.Supported, a.Disposition.HasValue);
        Assert.AreSame(a.Components, a.StepEvidence.Components); Assert.AreSame(a.RecordAssessments, a.StepEvidence.RecordAssessments); Assert.AreSame(a.Gaps, a.StepEvidence.Gaps);
    }

    [TestMethod]
    public void Empty_census_is_complete_with_literal_component_order_and_applicability()
    {
        using var f = new CardinalTraversalFixture(); var s = f.Inspect(); var a = s.Assess(Request(s));
        Result(a, "Supported", "Open", true); Assert.AreEqual(0, a.RecordAssessments.Count); Assert.AreEqual(0, a.Gaps.Count);
        CollectionAssert.AreEqual(Enumerable.Range(0, 16).ToArray(), a.Components.Select(c => (int)c.ComponentKind).ToArray());
        foreach (var i in new[] { 1, 2, 3, 5, 13, 14, 15 }) { Assert.AreEqual(CardinalTraversalApplicability.NotApplicable, a.Components[i].Applicability); Assert.IsNull(a.Components[i].Disposition); }
        Assert.AreEqual(2, s.TileScriptCount); Assert.AreEqual(12, s.SectorScriptBytes);
        Assert.AreEqual(92934, a.From.WorldX); Assert.AreEqual(92933, a.To.WorldX); Assert.AreEqual(82561, a.To.WorldY); Assert.AreEqual(70, a.From.Ordinal); Assert.AreEqual(69, a.To.Ordinal);
        Assert.AreEqual(CardinalTraversalInputKind.Structural, s.InputIdentity.Kind); Assert.IsNull(s.InputIdentity.OriginalIdentity);
        Assert.AreEqual(0, s.InputIdentity.Sector.EntryOrdinal); Assert.AreEqual(1u, s.InputIdentity.Sector.Flags);
        Assert.AreEqual(0, s.InputIdentity.SelectedNameEntryOrdinal);
    }

    [TestMethod]
    public void All_contributors_and_asymmetric_source_destination_not_first_record_win()
    {
        foreach (var flags in new[] { new uint[] { 0 }, new uint[] { 1024, 1024 }, new uint[] { 1024, 0 }, new uint[] { 0, 1024 } })
        {
            using var f = new CardinalTraversalFixture(flags.Select(v => Record(v)).ToArray()); var s = f.Inspect();
            var a = s.Assess(Request(s)); Result(a, "Supported", flags.Contains(0u) ? "Blocked" : "Open", true);
            Assert.AreEqual(flags.Length, a.RecordAssessments.Count); CollectionAssert.AreEqual(flags, a.RecordAssessments.Select(r => r.EffectiveFlags!.Value).ToArray());
            var reverse = s.Assess(Request(s, new(5, 1), new(6, 1), CardinalTraversalDirection.XPlus)); Result(reverse, "Supported", "Open", true);
            Assert.IsTrue(reverse.RecordAssessments.All(r => r.Result.Applicability == CardinalTraversalApplicability.NotApplicable));
        }
    }

    [TestMethod]
    public void Independent_tile_blockers_retain_all_uncertainties_but_scenery_requires_full_closure()
    {
        var uncertain = new[] { Record(0), Record(0, type: 0), Record(8193), Record(null, aid: 7002), AbsentLocation(), Record(location: World(int.MinValue, int.MaxValue)) };
        using (var f = new CardinalTraversalFixture(uncertain, blockedOrdinal: 69, names: null))
        {
            var s = f.Inspect(); var a = s.Assess(Request(s)); Result(a, "Supported", "Blocked", false);
            Assert.AreEqual(6, a.RecordAssessments.Count);
            foreach (var code in new[] { "TILE_NAME_ENTRY_MISSING", "TYPE_UNADMITTED", "DYNAMIC_UNADMITTED", "FLAGS_UNRESOLVED", "DIRECT_LOCATION_ABSENT", "DIRECT_LOCATION_OVERFLOW", "REGISTRATION_UNCERTAIN", "EXTENT_UNCERTAIN", "SOURCE_EDGE_CENSUS_UNCERTAIN", "OTHER_TYPE_CENSUS_UNCERTAIN" }) Assert.IsTrue(a.Gaps.Any(g => g.ReasonCode == code), code);
            var rejected = s.Assess(Request(s, context: Context(kind: 14))); Result(rejected, "Unsupported", null, false); Assert.AreEqual(0, rejected.Components.Count);
        }
        using (var f = new CardinalTraversalFixture([Record(0), Record(1024, type: 0)])) { var s = f.Inspect(); Result(s.Assess(Request(s)), "Unknown", null, false); }
        using (var f = new CardinalTraversalFixture([Record(0)], names: null)) { var s = f.Inspect(); var a = s.Assess(Request(s)); Result(a, "Supported", "Blocked", false); Assert.AreEqual(1, a.Gaps.Count); }
        foreach (var raw in new[] { 0xB0000000u, 0xB0123450u }) { using var f = new CardinalTraversalFixture(uncertain, destination: raw); var s = f.Inspect(); Result(s.Assess(Request(s)), "Supported", "Blocked", false); }
        using (var f = new CardinalTraversalFixture(uncertain, names: "{0}{aaa/b}")) { var s = f.Inspect(); Result(s.Assess(Request(s)), "Supported", "Blocked", false); }
        using (var f = new CardinalTraversalFixture(blockedOrdinal: 70)) { var s = f.Inspect(); Result(s.Assess(Request(s)), "Supported", "Open", true); }
    }

    [TestMethod]
    public void Literal_wire_flags_defaults_exclusions_and_unmasked_all_type_census()
    {
        foreach (var (direct, inherited, expected) in new (uint?, uint, string)[] { (0, 1024, "Blocked"), (1024, 0, "Open"), (null, 1024, "Open"), (null, 0, "Blocked") })
        {
            using var f = new CardinalTraversalFixture([Record(direct, slot19: inherited)]); f.PutPrototype(7001, inherited); var s = f.Inspect(); var a = s.Assess(Request(s)); Result(a, "Supported", expected, true);
            Assert.AreEqual(direct, s.Census[0].DirectFlags); Assert.AreEqual(direct ?? inherited, s.Census[0].EffectiveFlags);
            Assert.AreEqual(direct.HasValue ? StaticObstructionFlagsSource.Direct : StaticObstructionFlagsSource.Prototype, s.Census[0].FlagsSource);
        }
        foreach (var flags in new uint[] { 4096, 1, 2, 4099, 5123 }) { using var f = new CardinalTraversalFixture([Record(flags)]); var s = f.Inspect(); var a = s.Assess(Request(s)); Result(a, "Supported", "Open", true); Assert.AreEqual(CardinalTraversalApplicability.NotApplicable, a.RecordAssessments[0].RegistrationApplicability); }
        foreach (var flags in new uint[] { 8192, 8193, 8194, 12288, 13315 }) { using var f = new CardinalTraversalFixture([Record(flags)]); var s = f.Inspect(); var a = s.Assess(Request(s)); Result(a, "Unknown", null, false); Assert.IsTrue(a.Gaps.Any(g => g.ReasonCode == "DYNAMIC_UNADMITTED")); }
        using (var f = new CardinalTraversalFixture(Enumerable.Range(0, 18).Select(t => Record(0x12345678, type: t)).ToArray()))
        {
            var s = f.Inspect(); var a = s.Assess(Request(s)); Result(a, "Unknown", null, false); Assert.AreEqual(18, s.Census.Count);
            foreach (var row in s.Census) { Assert.AreEqual(0x12345678u, row.DirectFlags); Assert.AreEqual(World(), row.PackedLocation); if (row.ObjectType != 3) Assert.IsNull(row.EffectiveFlags); }
            Assert.AreEqual(17, a.Gaps.Count(g => g.ReasonCode == "TYPE_UNADMITTED"));
        }
        foreach (var (x, y, status) in new[] { (92927, 82560, "OutOfRange"), (92992, 82560, "OutOfRange"), (92928, 82559, "OutOfRange"), (92928, 82624, "OutOfRange"), (int.MinValue, int.MaxValue, "Overflow"), (int.MaxValue, int.MinValue, "Overflow") })
        { using var f = new CardinalTraversalFixture([Record(1024, World(x, y))]); var s = f.Inspect(); Assert.AreEqual(status, s.Census[0].LocationStatus.ToString()); Assert.IsNull(s.Census[0].LocalCell); Result(s.Assess(Request(s)), "Unknown", null, false); }
    }

    [TestMethod]
    public void Missing_mismatched_and_duplicate_defaults_are_unknown_malformed_defaults_abort()
    {
        using var f = new CardinalTraversalFixture([Record(null)]);
        Result(f.Inspect().Assess(Request(f.Inspect())), "Unknown", null, false);
        f.PutPrototype(7001, 1024); f.PutPrototype(7001, 0, filename: "007001 - duplicate.pro"); var d = f.Inspect(); Result(d.Assess(Request(d)), "Unknown", null, false);
        File.Delete(Path.Combine(f.PrototypeRoot, "007001 - duplicate.pro")); f.PutPrototype(7001, 1024, StaticObjects.StaticObjectLoaderTests.Prototype(7002, 123, 1024)); var wrong = f.Inspect(); Result(wrong.Assess(Request(wrong)), "Unknown", null, false);
        var otherType = StaticObjects.StaticObjectLoaderTests.Prototype(7001, 123, 1024); BinaryPrimitives.WriteInt32LittleEndian(otherType.AsSpan(52), 2); f.PutPrototype(7001, 1024, otherType); var t = f.Inspect(); Result(t.Assess(Request(t)), "Unknown", null, false);
        f.PutPrototype(7001, 1024, [1]); Assert.AreEqual(CardinalTraversalInvocationCode.MalformedSource, Throws<CardinalTraversalInvocationException>(() => f.Inspect()).Code);
        File.Delete(Path.Combine(f.PrototypeRoot, "007001 - fixture.pro")); Directory.Delete(f.PrototypeRoot); var missing = f.Inspect(); Result(missing.Assess(Request(missing)), "Unknown", null, false);
        foreach (var aid in new[] { -1, 1000000 }) { using var invalid = new CardinalTraversalFixture([Record(null, aid: aid)]); var s = invalid.Inspect(); Result(s.Assess(Request(s)), "Unknown", null, false); }
    }

    [TestMethod]
    public void Sparse_name_keys_category_bits_material_and_facade_polarity_remain_distinct()
    {
        foreach (var (raw, key) in new[] { (0x180u, 7), (0x400180u, 42) })
        { using var f = new CardinalTraversalFixture(destination: raw, names: "{42}{aaa}{7}{bbb}{300}{ccc}"); var s = f.Inspect(); var a = s.Assess(Request(s)); Result(a, "Supported", "Open", true); var inputs = a.Components.Single(c => (int)c.ComponentKind == 6).Inputs.ToDictionary(i => i.Code, i => i.Value); Assert.AreEqual(3L, inputs["CATEGORY_BITS"]); Assert.AreEqual(0L, inputs["NAME_CATEGORY_INDEX"]); Assert.AreEqual((long)key, inputs["MES_KEY"]); }
        foreach (var suffix in new[] { "b", "f", "sbf -8" }) { using var f = new CardinalTraversalFixture(names: "{0}{aaa/" + suffix + "}"); var s = f.Inspect(); Result(s.Assess(Request(s)), "Supported", "Blocked", true); }
        foreach (var value in new[] { "aaa", "aaa/sinp", "aaa/s +8" }) { using var f = new CardinalTraversalFixture(names: "{0}{" + value + "}"); var s = f.Inspect(); Result(s.Assess(Request(s)), "Supported", "Open", true); }
        foreach (var value in new[] { "", "badlong", "aaa/x", "aaa/b/x" }) { using var f = new CardinalTraversalFixture(names: "{0}{" + value + "}"); var s = f.Inspect(); Result(s.Assess(Request(s)), "Unknown", null, false); }
        using (var f = new CardinalTraversalFixture(destination: 0x400180)) { var s = f.Inspect(); Result(s.Assess(Request(s)), "Unknown", null, false); }
        foreach (var raw in new[] { 0xB0000001u, 0xB0123451u }) { using var f = new CardinalTraversalFixture(destination: raw, names: null); var s = f.Inspect(); Result(s.Assess(Request(s)), "Supported", "Open", true); }
        using (var f = new CardinalTraversalFixture(mask: false)) { var s = f.Inspect(); Result(s.Assess(Request(s)), "Unknown", null, false); }
        using (var f = new CardinalTraversalFixture(duplicateNames: true)) { var s = f.Inspect(); var a = s.Assess(Request(s)); Result(a, "Unknown", null, false); Assert.IsNull(s.InputIdentity.SelectedNameEntryOrdinal); Assert.IsTrue(a.Gaps.Any(g => g.ReasonCode == "TILE_NAME_ENTRY_AMBIGUOUS")); }
        using (var f = new CardinalTraversalFixture()) { var s = f.Inspect(nameSource: false); var a = s.Assess(Request(s)); Result(a, "Unknown", null, false); Assert.IsTrue(a.Gaps.Any(g => g.ReasonCode == "TILE_NAME_SOURCE_ABSENT")); }
    }

    [TestMethod]
    public void Geometry_context_epoch_identity_validation_precedes_every_blocker()
    {
        using var f = new CardinalTraversalFixture(blockedOrdinal: 69); var s = f.Inspect();
        foreach (var (from, to, dir) in new[] { (new CardinalTraversalCell(6, 1), new CardinalTraversalCell(5, 1), 1), (new(5, 1), new(5, 2), 3), (new(5, 1), new(6, 1), 5), (new(5, 2), new(5, 1), 7) }) Assert.AreEqual(CardinalTraversalCoverage.Supported, s.Assess(Request(s, from, to, (CardinalTraversalDirection)dir)).Coverage);
        foreach (var (to, dir) in new[] { (new CardinalTraversalCell(5, 0), 0), (new(5, 2), 2), (new(7, 2), 4), (new(7, 0), 6) })
        { var a = s.Assess(Request(s, to: to, direction: (CardinalTraversalDirection)dir)); Result(a, "Unknown", null, false); Assert.AreEqual("DIAGONAL_RULE_UNPROVED", a.Gaps.Single().ReasonCode); var both = s.Assess(Request(s, to: to, direction: (CardinalTraversalDirection)dir, context: Context(path: 0x1000))); Assert.AreEqual(2, both.Gaps.Count); }
        Result(s.Assess(Request(s, context: Context(path: 0x1000))), "Unknown", null, false);
        foreach (var context in new[] { Context(id: "OTHER"), Context(kind: 14), Context(art: 1), Context(spell: 1), Context(path: 1), Context(polymorph: true), Context(editor: true), Context(state: CardinalTraversalActorState.Floating), Context(state: CardinalTraversalActorState.Passwall), Context(state: CardinalTraversalActorState.SpecialOverride), Context(state: (CardinalTraversalActorState)999), Context(supplements: 1) }) { var a = s.Assess(Request(s, context: context)); Result(a, "Unsupported", null, false); Assert.AreEqual("CONTEXT_UNSUPPORTED", a.ReasonCode); }
        foreach (var (from, to, dir, reason) in new[] {
            (new CardinalTraversalCell(5, 1), new CardinalTraversalCell(5, 1), 1, "SAME_CELL_UNSUPPORTED"), (new(7, 1), new(5, 1), 1, "NONADJACENT_UNSUPPORTED"),
            (new(6, 1), new(5, 1), 5, "DIRECTION_MISMATCH"), (new(6, 1), new(5, 1), 99, "DIRECTION_UNSUPPORTED"),
            (new(4, 0), new(3, 0), 1, "ZONE_UNSUPPORTED"), (new(8, 3), new(9, 3), 5, "ZONE_UNSUPPORTED"),
            (new(4, 0), new(4, -1), 7, "CROSS_SECTOR_DATA_ABSENT"), (new(4, 0), new(3, -1), 0, "CROSS_SECTOR_DATA_ABSENT"),
            (new(8, 3), new(8, 4), 3, "ZONE_UNSUPPORTED") })
        { var a = s.Assess(Request(s, from, to, (CardinalTraversalDirection)dir)); Result(a, "Unsupported", null, false); Assert.AreEqual(reason, a.ReasonCode); Assert.IsNull(a.To.WorldX); Assert.IsNull(a.To.Ordinal); }
        foreach (var bad in new[] { -1, 64, int.MinValue, int.MaxValue }) foreach (var c in new[] { new CardinalTraversalCell(bad, 1), new(5, bad) }) foreach (var source in new[] { true, false }) Result(s.Assess(Request(s, source ? c : new(6, 1), source ? new(5, 1) : c)), "Unsupported", null, false);
        foreach (var req in new[] { Request(s, profile: "OTHER"), Request(s, epoch: 8), Request(s, sx: 1453), Request(s, sy: 1291) }) Result(s.Assess(req), "Unsupported", null, false);
        using var other = new CardinalTraversalFixture(destination: 0xB0000001); Result(s.Assess(Request(s, identity: other.Inspect().InputIdentity)), "Unsupported", null, false);
        Assert.AreEqual(7L, s.EvidenceEpoch);
    }

    [TestMethod]
    public void Owned_models_survive_mutation_disposal_and_zero_query_reads_for_every_result_class()
    {
        var f = new CardinalTraversalFixture([Record(0)]); var beforeM = f.ModuleBytes.ToArray(); var beforeR = f.ResourceBytes.ToArray(); var s = f.Inspect();
        CollectionAssert.AreEqual(beforeM, f.ModuleBytes); CollectionAssert.AreEqual(beforeR, f.ResourceBytes);
        f.ModuleStream.Armed = f.ResourceStream.Armed = true; Array.Fill(f.ModuleBytes, (byte)0); Array.Fill(f.ResourceBytes, (byte)0); Directory.Delete(f.PrototypeRoot, true);
        for (var i = 0; i < 2; i++)
        {
            Result(s.Assess(Request(s)), "Supported", "Blocked", true); Result(s.Assess(Request(s, new(5, 1), new(6, 1), CardinalTraversalDirection.XPlus)), "Supported", "Open", true);
            Result(s.Assess(Request(s, context: Context(path: 0x1000))), "Unknown", null, false); Result(s.Assess(Request(s, epoch: 9)), "Unsupported", null, false);
            if (i == 0) f.Dispose();
        }
        Assert.AreEqual(0, f.ModuleStream.ForbiddenReads); Assert.AreEqual(0, f.ResourceStream.ForbiddenReads);
        var a = s.Assess(Request(s)); Throws<NotSupportedException>(() => ((IList<CardinalTraversalCensusRecord>)s.Census).Clear());
        Throws<NotSupportedException>(() => ((IList<CardinalTraversalComponentAssessment>)a.Components).Clear());
        Throws<NotSupportedException>(() => ((IList<CardinalTraversalRecordAssessment>)a.RecordAssessments).Clear());
        Throws<NotSupportedException>(() => ((IList<CardinalTraversalGap>)a.Gaps).Clear());
        Throws<NotSupportedException>(() => ((IList<CardinalTraversalNumericInput>)a.Components[0].Inputs).Clear());
        Throws<NotSupportedException>(() => ((IList<CardinalTraversalProvenance>)a.Components[0].Provenance).Clear());
        Throws<NotSupportedException>(() => ((IList<string>)a.StepEvidence.Provenance).Clear());
        var types = typeof(CardinalTraversalSnapshot).Assembly.GetTypes().Where(t => t.Namespace == "Arcanum.Data.Traversal" && t.IsPublic && t.IsClass && !typeof(Exception).IsAssignableFrom(t));
        foreach (var t in types) { Assert.IsTrue(t.IsSealed, t.Name); Assert.IsTrue(t.GetProperties().All(p => p.SetMethod is null), t.Name); if (t != typeof(CardinalTraversalContext) && t != typeof(CardinalTraversalRequest) && t != typeof(CardinalTraversalInspector) && t != typeof(AreaStepRequest) && t != typeof(AreaStepInspector)) Assert.AreEqual(0, t.GetConstructors().Length, t.Name); }
    }

    [TestMethod]
    public void Malformed_source_cap_and_arguments_are_sanitized_construction_failures_not_evidence()
    {
        foreach (var bytes in new[] { Record()[..^1], StaticObjects.StaticObjectLoaderTests.Record(7001, 1, 0, 0, version: 118), StaticObjects.StaticObjectLoaderTests.Record(7001, 1, 0, 0, badLocationPresence: true) })
        { using var f = new CardinalTraversalFixture([bytes]); var e = Throws<CardinalTraversalInvocationException>(() => f.Inspect()); Assert.AreEqual("CARDINAL_INVOCATION_FAILED", e.Message); Assert.IsNull(e.InnerException); Assert.IsFalse(e.ToString().Contains(f.PrototypeRoot)); Assert.AreEqual(CardinalTraversalInvocationCode.MalformedSource, e.Code); Assert.AreEqual(1, f.Module.Entries.Count); }
        foreach (var mes in new[] { "{0}{aaa}{0}{bbb}", "{0}{aaa" }) { using var f = new CardinalTraversalFixture(names: mes); Throws<CardinalTraversalInvocationException>(() => f.Inspect()); }
        using (var f = new CardinalTraversalFixture(names: "{0}{a\u0001a}")) { var s = f.Inspect(); Result(s.Assess(Request(s)), "Unknown", null, false); }
        using (var f = new CardinalTraversalFixture()) { f.ResourceBytes[4] = 0x80; Throws<CardinalTraversalInvocationException>(() => f.Inspect()); }
        using var capped = new CardinalTraversalFixture([Record(), Record()]);
        foreach (var options in new[] { new StaticObjectLoaderOptions { MaxRecords = 1 }, new StaticObjectLoaderOptions { MaxSectorBytes = 1 }, new StaticObjectLoaderOptions { MaxFields = 1 } }) Assert.AreEqual(CardinalTraversalInvocationCode.LimitExceeded, Throws<CardinalTraversalInvocationException>(() => capped.Inspect(objects: options)).Code);
        Assert.AreEqual(CardinalTraversalInvocationCode.LimitExceeded, Throws<CardinalTraversalInvocationException>(() => capped.Inspect(terrain: new TerrainLoaderOptions { MaxMesDecodedEntryBytes = 1 })).Code);
        foreach (var id in new[] { "bad/path", "bad\\path", "bad:label", "bad\nlabel", "Ж", new string('a', 129) }) Throws<ArgumentException>(() => Context(id));
        Throws<ArgumentOutOfRangeException>(() => capped.Inspect(epoch: -1)); Throws<ArgumentOutOfRangeException>(() => Context(supplements: -1)); Throws<ArgumentNullException>(() => capped.Inspect().Assess(null!));
        var absent = Throws<CardinalTraversalInvocationException>(() => CardinalTraversalInspector.ValidateOriginalData(Path.Combine(capped.PrototypeRoot, "private-file"), "private-resource", capped.PrototypeRoot, 7)); Assert.AreEqual(CardinalTraversalInvocationCode.InputUnavailable, absent.Code); Assert.IsNull(absent.InnerException);
        var wrong = Path.Combine(capped.PrototypeRoot, "wrong.dat"); File.WriteAllBytes(wrong, capped.ModuleBytes); Assert.AreEqual(CardinalTraversalInvocationCode.FingerprintMismatch, Throws<CardinalTraversalInvocationException>(() => CardinalTraversalInspector.ValidateOriginalData(wrong, wrong, capped.PrototypeRoot, 7)).Code);
    }

    [TestMethod]
    public void Selected_source_geometry_metadata_profile_and_caller_shape_fail_without_snapshot()
    {
        using var f = new CardinalTraversalFixture();
        foreach (var selector in new[] {
            new TerrainSectorSelector(SectorPath, "BASE", 1453, 1290, 86570436012, 92928, 82560, 64, 64),
            new(SectorPath, "BASE", 1452, 1291, 86570436012, 92928, 82560, 64, 64),
            new(SectorPath, "BASE", 1452, 1290, 1, 92928, 82560, 64, 64),
            new(SectorPath, "BASE", 1452, 1290, 86570436012, int.MaxValue, int.MinValue, 64, 64),
            new(SectorPath, "BASE", 1452, 1290, 86570436012, 92928, 82560, 64, 64, new(463, 2, 47291)),
            new("other.sec", "BASE", 1452, 1290, 86570436012, 92928, 82560, 64, 64) })
            Assert.AreEqual(CardinalTraversalInvocationCode.InvalidSelection, Throws<CardinalTraversalInvocationException>(() =>
                CardinalTraversalInspector.InspectSector(f.Source, f.Prototypes, "MODULE", "RESOURCE", "PROTOTYPES", selector, null, 7)).Code);
        foreach (var names in new[] { new TerrainTileNameSource(f.Resource, "OTHER", NamePath, TerrainPathProfile.CeNonWindowsMacOs), new(f.Resource, "RESOURCE", "other.mes", TerrainPathProfile.CeNonWindowsMacOs), new(f.Module, "RESOURCE", NamePath, TerrainPathProfile.CeNonWindowsMacOs) })
            Assert.AreEqual(CardinalTraversalInvocationCode.InvalidSelection, Throws<CardinalTraversalInvocationException>(() => CardinalTraversalInspector.InspectSector(f.Source, f.Prototypes, "MODULE", "RESOURCE", "PROTOTYPES", f.Selector, names, 7)).Code);
        var s = f.Inspect();
        Throws<ArgumentNullException>(() => new CardinalTraversalRequest("BASE", null!, 7, 1452, 1290, new(6, 1), new(5, 1), CardinalTraversalDirection.XMinus, s.DeclaredContext));
        Throws<ArgumentNullException>(() => new CardinalTraversalRequest("BASE", s.InputIdentity, 7, 1452, 1290, new(6, 1), new(5, 1), CardinalTraversalDirection.XMinus, null!));
        Throws<ArgumentOutOfRangeException>(() => Request(s, epoch: -1));
        Throws<ArgumentOutOfRangeException>(() => f.Inspect(objects: new StaticObjectLoaderOptions { MaxRecords = 0 }));
        foreach (var profile in new[] { "", "bad/path", "bad\nprofile" }) Throws<ArgumentException>(() => Request(s, profile: profile));
    }

    [TestMethod]
    public void Raw_defaults_forms_and_terminal_framing_never_fabricate_census_or_permission()
    {
        var notTypeA = Record(null); BinaryPrimitives.WriteInt16LittleEndian(notTypeA.AsSpan(4), 2);
        using (var f = new CardinalTraversalFixture([notTypeA])) { var s = f.Inspect(); Assert.IsNull(s.Census[0].PrototypeAid); Result(s.Assess(Request(s)), "Unknown", null, false); }
        foreach (var count in new[] { 0, 2 })
        { var sector = Sector([Record()]); BinaryPrimitives.WriteInt32LittleEndian(sector.AsSpan(sector.Length - 4), count); using var f = new CardinalTraversalFixture(sector: sector); Throws<CardinalTraversalInvocationException>(() => f.Inspect()); }
        using (var f = new CardinalTraversalFixture([StaticObjects.StaticObjectLoaderTests.Record(7001, null, 1024, 0xffffffff, World(), int.MinValue, int.MaxValue)]))
        { var s = f.Inspect(); Result(s.Assess(Request(s)), "Supported", "Open", true); }
        foreach (var raw in new[] { 0x10000000u, 0xF0000000u }) { using var f = new CardinalTraversalFixture(destination: raw); var s = f.Inspect(); Result(s.Assess(Request(s)), "Unknown", null, false); }
        using (var f = new CardinalTraversalFixture([Record(null)], names: null)) { var s = f.Inspect(); var a = s.Assess(Request(s)); Result(a, "Unknown", null, false); Assert.IsTrue(a.Gaps.Any(g => g.ReasonCode == "FLAGS_UNRESOLVED")); Assert.IsTrue(a.Gaps.Any(g => g.ReasonCode == "TILE_NAME_ENTRY_MISSING")); }
        using (var f = new CardinalTraversalFixture([Record(0), Record(1024, World(92934, 82561))], blockedOrdinal: 69))
        { var s = f.Inspect(); var a = s.Assess(Request(s, new(5, 1), new(6, 1), CardinalTraversalDirection.XPlus)); Result(a, "Supported", "Open", true); }
    }

    internal static T Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T e) { return e; } Assert.Fail("Expected " + typeof(T).Name); throw new InvalidOperationException(); }
}
