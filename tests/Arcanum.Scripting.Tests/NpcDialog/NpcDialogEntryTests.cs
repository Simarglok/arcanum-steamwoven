using System.Collections;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Scripting.NpcDialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.NpcDialog;

[TestClass, TestCategory("NpcDialogEntrySynthetic")]
public sealed class NpcDialogEntryTests
{
    [TestMethod]
    public void Production_wire_snapshot_false_path_and_literal_query_transcript()
    {
        using var f = new NpcDialogFixture();
        var p = f.Prepare(); Assert.AreEqual(NpcDialogAdmission.Admitted, p.Admission, p.Error?.Code.ToString());
        Assert.AreEqual(71, p.Plan!.Attachment.ScriptId.Value);
        Assert.AreEqual(AreaValueOrigin.Instance, p.Plan.Attachment.Origin);
        Assert.AreEqual(0x12U, p.Plan.Attachment.Flags); Assert.AreEqual(0x34U, p.Plan.Attachment.Counters);
        Assert.AreNotEqual(f.Snapshot.Objects[0].PrototypeReferenceSha256, f.Snapshot.Objects[0].PrototypeOwnSha256);
        var q = new EntryQueries(); var result = NpcDialogEntryResolver.Resolve(p.Plan, q, new());
        Assert.AreEqual(NpcDialogResolutionKind.Resolved, result.Kind);
        Assert.AreEqual(4, result.Entry!.ScrLine.Value); Assert.AreEqual(901, result.Entry.DlgNode.Value);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, result.Trace.Select(t => t.Line.Value).ToArray());
        CollectionAssert.AreEqual(new[] { "V0", "F0", "V0", "L4:1", "V1", "J2", "V2", "L11:3", "V3", "V4" }, q.Calls);
        CollectionAssert.AreEqual(Enumerable.Range(1, 10).ToArray(), q.Contexts.Select(c => c.RequestOrdinal).ToArray());
        Assert.IsTrue(q.Contexts.All(c => c.Invocation == f.Invocation));
    }
    [TestMethod]
    public void Whole_field_inheritance_null_empty_missing_and_zero_never_merge_events()
    {
        using (var f = new NpcDialogFixture(inherit: true))
        { var p = f.Prepare(); Assert.AreEqual(NpcDialogAdmission.Admitted, p.Admission); Assert.AreEqual(82, p.Plan!.Attachment.ScriptId.Value); Assert.AreEqual(AreaValueOrigin.Prototype, p.Plan.Attachment.Origin); }
        foreach (var sap in new[] { new byte[] { 0 }, NpcDialogFixture.Sap(), NpcDialogFixture.Sap((8, 71)), NpcDialogFixture.Sap((9, 0)) })
        {
            using var f = new NpcDialogFixture(instanceSap: sap);
            var p = f.Prepare(); Assert.AreEqual(sap.SequenceEqual(NpcDialogFixture.Sap((9, 0))) ? NpcDialogAdmission.ZeroScript : NpcDialogAdmission.NoDialogAttachment, p.Admission);
            Assert.IsNull(p.Plan); Assert.IsNull(p.Error);
        }
        using var ranked = new NpcDialogFixture(instanceSap: NpcDialogFixture.Sap((1, 3), (9, 71), (34, 90)));
        Assert.AreEqual(71, ranked.Prepare().Plan!.Attachment.ScriptId.Value);
    }
    [TestMethod]
    public void Every_predicate_true_short_circuits_to_computed_non_original_node()
    {
        using var f = new NpcDialogFixture(); var plan = f.Prepare().Plan!;
        for (var predicate = 0; predicate < 4; predicate++)
        {
            var q = new EntryQueries(); for (var i = 0; i < predicate; i++) q.Answers.Enqueue(NpcDialogQueryStatus.KnownFalse); q.Answers.Enqueue(NpcDialogQueryStatus.KnownTrue);
            var r = NpcDialogEntryResolver.Resolve(plan, q, new()); Assert.AreEqual(NpcDialogResolutionKind.Resolved, r.Kind); Assert.AreEqual(999, r.Entry!.DlgNode.Value);
            Assert.AreEqual(5 + predicate, r.Entry.ScrLine.Value); Assert.AreEqual(predicate + 1, q.Calls.Count(c => !c.StartsWith('V')));
        }
    }
    [TestMethod]
    public void Each_predicate_nonvalue_or_failure_stops_without_false_default()
    {
        using var f = new NpcDialogFixture(); var plan = f.Prepare().Plan!;
        foreach (var status in new[] { NpcDialogQueryStatus.Unknown, NpcDialogQueryStatus.Unavailable, NpcDialogQueryStatus.Failed, NpcDialogQueryStatus.Stale })
        for (var predicate = 0; predicate < 4; predicate++)
        {
            var q = new EntryQueries(); for (var i = 0; i < predicate; i++) q.Answers.Enqueue(NpcDialogQueryStatus.KnownFalse); q.Answers.Enqueue(status);
            var r = NpcDialogEntryResolver.Resolve(plan, q, new()); Assert.IsNull(r.Entry);
            Assert.AreEqual(predicate, (r.Frontier?.Line ?? r.Error!.Line)!.Value.Value);
            Assert.AreEqual(predicate + 1, q.Calls.Count(c => !c.StartsWith('V')));
            Assert.AreEqual(status is NpcDialogQueryStatus.Unknown or NpcDialogQueryStatus.Unavailable ? NpcDialogResolutionKind.Frontier : NpcDialogResolutionKind.Error, r.Kind);
        }
    }
    [TestMethod]
    public void Version_checkpoints_detect_owner_source_and_attachment_changes_at_every_call()
    {
        using var f = new NpcDialogFixture(); var plan = f.Prepare().Plan!;
        foreach (var checkpoint in new[] { 1, 3, 5, 7, 9, 10 })
        for (var kind = 0; kind < 3; kind++)
        {
            var q = new EntryQueries();
            q.Versions = c => c.RequestOrdinal == checkpoint ? q.Version(c) with
            {
                StateVersion = kind == 0 ? new(Guid.NewGuid(), 17) : c.Invocation.StateVersion,
                AttachmentStateVersion = kind == 1 ? new(c.Invocation.AttachmentStateVersion.OwnerEpoch, 30) : c.Invocation.AttachmentStateVersion,
                SourceVersion = kind == 2 ? new(Guid.NewGuid(), "explicit-selected-archives-v1", c.Invocation.SourceVersion.Bindings) : c.Invocation.SourceVersion,
            } : q.Version(c);
            var r = NpcDialogEntryResolver.Resolve(plan, q, new()); Assert.AreEqual(NpcDialogCode.QueryStale, r.Error!.Code); Assert.AreEqual(checkpoint, q.Calls.Count);
        }
        foreach (var status in new[] { NpcDialogVersionStatus.Unknown, NpcDialogVersionStatus.Unavailable, NpcDialogVersionStatus.Failed, NpcDialogVersionStatus.Stale })
        { var q = new EntryQueries(); q.Versions = c => q.Version(c) with { Status = status }; var r = NpcDialogEntryResolver.Resolve(plan, q, new()); Assert.IsNull(r.Entry); Assert.AreEqual(1, q.Calls.Count); }
    }
    [TestMethod]
    public void Query_context_and_sanitized_exception_failures_are_terminal()
    {
        using var f = new NpcDialogFixture(); var plan = f.Prepare().Plan!;
        foreach (var versions in new[] { false, true })
        {
            var q = new EntryQueries();
            if (versions) q.Versions = c => q.Version(c) with { Context = c with { RequestOrdinal = 99 } };
            else q.Boolean = c => new(c with { Line = new(8) }, NpcDialogQueryStatus.KnownFalse);
            Assert.AreEqual(NpcDialogCode.WrongContext, NpcDialogEntryResolver.Resolve(plan, q, new()).Error!.Code);
        }
        var throwing = new EntryQueries { Boolean = _ => throw new InvalidOperationException("private-path-and-text") };
        var result = NpcDialogEntryResolver.Resolve(plan, throwing, new()); Assert.AreEqual(NpcDialogCode.QueryFailed, result.Error!.Code);
        Assert.IsFalse(result.Error.ToString().Contains("private-path-and-text", StringComparison.Ordinal));
    }
    [TestMethod]
    public void Static_known_malformed_untaken_arms_and_nodes_fail_before_a_plan_exists()
    {
        foreach (var (component, type, tag, value, code) in new (int, int, byte, int, NpcDialogCode)[]
        { (44, 3, 3, 99, NpcDialogCode.MalformedOperand), (88, 4, 3, 777, NpcDialogCode.WrongNodeRole), (44, 4, 3, 555, NpcDialogCode.MissingNode), (44, 4, 3, 0, NpcDialogCode.MalformedOperand) })
        {
            var bytes = NpcDialogFixture.Scr(); NpcDialogFixture.Operation(bytes, 0, component, type, tag, value);
            using var f = new NpcDialogFixture(script: bytes); var p = f.Prepare(); Assert.IsNull(p.Plan); Assert.AreEqual(code, p.Error!.Code);
        }
        var flag = NpcDialogFixture.Scr(); NpcDialogFixture.Operation(flag, 1, 0, 3, 3, 32);
        using var invalid = new NpcDialogFixture(script: flag); Assert.AreEqual(NpcDialogCode.MalformedOperand, invalid.Prepare().Error!.Code);
    }
    [TestMethod]
    public void Unsupported_arm_preserves_valid_opposite_and_opaque_dead_fields()
    {
        foreach (var type in new[] { 1, 2, 7, 123 })
        {
            var bytes = NpcDialogFixture.Scr(); NpcDialogFixture.Operation(bytes, 0, 44, type, 255, int.MinValue);
            using var f = new NpcDialogFixture(script: bytes); var p = f.Prepare(); Assert.AreEqual(NpcDialogAdmission.Admitted, p.Admission);
            Assert.AreEqual(NpcDialogResolutionKind.Resolved, NpcDialogEntryResolver.Resolve(p.Plan!, new EntryQueries(), new()).Kind);
            var q = new EntryQueries(); q.Answers.Enqueue(NpcDialogQueryStatus.KnownTrue);
            Assert.AreEqual(type == 7 ? NpcDialogCode.UnsupportedCallback : NpcDialogCode.UnsupportedAction, NpcDialogEntryResolver.Resolve(p.Plan!, q, new()).Frontier!.Code);
        }
        var dead = NpcDialogFixture.Scr(2); NpcDialogFixture.Operation(dead, 0, 88, 4, 3, 555); NpcDialogFixture.Operation(dead, 1, 44, 4, 3, 0);
        dead[64 + 11] = 255; NpcDialogFixture.Set(dead, 64 + 40, int.MinValue); NpcDialogFixture.Set(dead, 60, int.MaxValue);
        using var opaque = new NpcDialogFixture(script: dead); var q2 = new EntryQueries();
        Assert.AreEqual(NpcDialogResolutionKind.Resolved, NpcDialogEntryResolver.Resolve(opaque.Prepare().Plan!, q2, new()).Kind);
        CollectionAssert.AreEqual(new[] { "V0", "V0" }, q2.Calls);
        foreach (var (component, code) in new[] { (0, NpcDialogCode.UnsupportedCondition), (44, NpcDialogCode.UnsupportedOperand) })
        {
            var b = NpcDialogFixture.Scr(1); NpcDialogFixture.Operation(b, 0, component, component == 0 ? 91 : 4, 6, 901);
            using var f = new NpcDialogFixture(script: b); var p = f.Prepare(); Assert.AreEqual(code, NpcDialogEntryResolver.Resolve(p.Plan!, new EntryQueries(), new()).Frontier!.Code);
        }
    }
    [TestMethod]
    public void Container_truncation_count_capacity_overflow_and_code_flags_are_static()
    {
        var valid = NpcDialogFixture.Scr(1);
        for (var length = 0; length < valid.Length; length++)
        { using var f = new NpcDialogFixture(script: valid[..length]); Assert.IsNull(f.Prepare().Plan); }
        foreach (var (offset, value, code) in new[] { (52, 0, NpcDialogCode.MalformedContainer), (52, -1, NpcDialogCode.MalformedContainer), (52, int.MaxValue, NpcDialogCode.MalformedContainer), (56, 0, NpcDialogCode.MalformedContainer), (56, 4097, NpcDialogCode.Budget) })
        { var b = (byte[])valid.Clone(); NpcDialogFixture.Set(b, offset, value); using var f = new NpcDialogFixture(script: b); Assert.AreEqual(code, f.Prepare().Error!.Code); }
        using (var f = new NpcDialogFixture(script: [.. valid, 0])) Assert.AreEqual(NpcDialogCode.MalformedContainer, f.Prepare().Error!.Code);
        var flags = (byte[])valid.Clone(); NpcDialogFixture.Set(flags, 48, 1);
        using var flagged = new NpcDialogFixture(script: flags); Assert.AreEqual(NpcDialogCode.UnsupportedRepresentation, flagged.Prepare().Frontier!.Code);
    }
    [TestMethod]
    public void Complete_numeric_resource_keys_and_exact_spelling_no_first_winner()
    {
        using (var f = new NpcDialogFixture(scriptNames: ["scr\\70Other.scr", "scr\\00071Invented.scr", "scr\\72Other.scr"])) Assert.AreEqual(1, f.Prepare().Plan!.ScriptResource.Ordinal);
        foreach (var names in new[] { new[] { "scr\\71Invented.scr", "scr\\00071Invented.scr" }, new[] { "scr\\00071Invented.scr", "scr\\00071Invented.scr" } })
        { using var f = new NpcDialogFixture(scriptNames: names); Assert.AreEqual(NpcDialogCode.AmbiguousResource, f.Prepare().Error!.Code); }
        foreach (var name in new[] { "scr\\71000Other.scr", "scr\\72Other.scr" })
        { using var f = new NpcDialogFixture(scriptNames: [name]); Assert.AreEqual(NpcDialogCode.MissingResource, f.Prepare().Error!.Code); }
        foreach (var name in new[] { "scr\\100000Other.scr", "scr\\999999999999999Other.scr", "scr\\+71Other.scr", "scr\\ 71Other.scr", "scr\\71/Other.scr", "scr\\71:Other.scr", "scr\\0Other.scr" })
        { using var f = new NpcDialogFixture(scriptNames: [name]); Assert.AreEqual(NpcDialogCode.InvalidCatalogueKey, f.Prepare().Error!.Code); }
        using (var f = new NpcDialogFixture(dialogNames: ["dlg\\71Invented.dlg"])) Assert.AreEqual(NpcDialogCode.MissingResource, f.Prepare().Error!.Code);
        using (var f = new NpcDialogFixture(dialogNames: ["dlg\\00071Invented.dlg", "dlg\\00071Invented.dlg"])) Assert.AreEqual(NpcDialogCode.AmbiguousResource, f.Prepare().Error!.Code);
    }
    [TestMethod]
    public void Budgets_cycles_end_of_script_and_cooperative_cancellation()
    {
        using var f = new NpcDialogFixture(); var plan = f.Prepare().Plan!;
        Assert.AreEqual(NpcDialogResolutionKind.Resolved, NpcDialogEntryResolver.Resolve(plan, new EntryQueries(), new() { MaxSteps = 5, MaxQueryCalls = 10 }).Kind);
        Assert.AreEqual(NpcDialogCode.Budget, NpcDialogEntryResolver.Resolve(plan, new EntryQueries(), new() { MaxSteps = 4 }).Frontier!.Code);
        Assert.AreEqual(NpcDialogCode.Budget, NpcDialogEntryResolver.Resolve(plan, new EntryQueries(), new() { MaxQueryCalls = 9 }).Frontier!.Code);
        var staticLimits = new[] { new NpcDialogEntryLimits { MaxGraphDepth = 1 }, new() { MaxScriptBytes = 64 }, new() { MaxScriptEntries = 9 }, new() { MaxDialogRecords = 2 }, new() { MaxDialogFieldBytes = 2 }, new() { MaxCatalogueNameBytes = 4 } };
        foreach (var l in staticLimits) { Assert.IsNull(f.Prepare(l).Plan); var q = new EntryQueries(); Assert.AreEqual(NpcDialogCode.Budget, NpcDialogEntryResolver.Resolve(plan, q, l).Error!.Code); Assert.AreEqual(0, q.Calls.Count); }
        foreach (var l in new[] { new NpcDialogEntryLimits { MaxSteps = 0 }, new() { MaxInvocationDepth = 2 }, new() { MaxQueryCalls = 129 } }) Assert.AreEqual(NpcDialogCode.InvalidLimits, f.Prepare(l).Error!.Code);
        using var cts = new CancellationTokenSource(); cts.Cancel(); Assert.AreEqual(NpcDialogCode.Cancelled, f.Prepare(token: cts.Token).Error!.Code);
        var cancelled = new EntryQueries(); cancelled.Boolean = c => { cts.Cancel(); return new(c, NpcDialogQueryStatus.KnownFalse); };
        using var active = new CancellationTokenSource(); var inside = new EntryQueries { Boolean = c => { active.Cancel(); return new(c, NpcDialogQueryStatus.KnownFalse); } };
        Assert.AreEqual(NpcDialogCode.Cancelled, NpcDialogEntryResolver.Resolve(plan, inside, new(), active.Token).Error!.Code);
        var loop = NpcDialogFixture.Scr(1); NpcDialogFixture.Operation(loop, 0, 44, 3, 3, 0);
        using (var cycles = new NpcDialogFixture(script: loop)) Assert.AreEqual(NpcDialogCode.Cycle, NpcDialogEntryResolver.Resolve(cycles.Prepare().Plan!, new EntryQueries(), new()).Frontier!.Code);
        var end = NpcDialogFixture.Scr(1); NpcDialogFixture.Operation(end, 0, 44, 0, 0, 0);
        using var ending = new NpcDialogFixture(script: end); Assert.AreEqual(NpcDialogCode.NoEntry, NpcDialogEntryResolver.Resolve(ending.Prepare().Plan!, new EntryQueries(), new()).Frontier!.Code);
    }
    [TestMethod]
    public void Owned_plan_survives_disposal_mutation_and_borrowed_positions_are_restored()
    {
        using var f = new NpcDialogFixture(); foreach (var s in f.Streams) s.Position = Math.Min(3, s.Length);
        var p = f.Prepare(); foreach (var s in f.Streams.TakeLast(2)) { Assert.IsTrue(s.CanRead); Assert.AreEqual(3L, s.Position); }
        Array.Fill(f.ScriptArchive, (byte)0); Array.Fill(f.DialogArchive, (byte)0);
        f.Dispose(); var r = NpcDialogEntryResolver.Resolve(p.Plan!, new EntryQueries(), new()); Assert.AreEqual(901, r.Entry!.DlgNode.Value);
        Throws<NotSupportedException>(() => ((IList)r.Trace).Clear());
        using var changed = new NpcDialogFixture(); changed.Streams[^2].Position = 7; changed.ScriptArchive[0] ^= 1;
        Assert.AreEqual(NpcDialogCode.SourceChanged, changed.Prepare().Error!.Code); Assert.AreEqual(7L, changed.Streams[^2].Position); Assert.IsTrue(changed.Streams[^2].CanRead);
    }
    [TestMethod]
    public void Wrong_participants_map_prototype_event_extra_and_default_values_are_admission_failures()
    {
        using var f = new NpcDialogFixture(); var original = f.Invocation;
        foreach (var invocation in new[] { original with { WorldId = default }, original with { PcId = default }, original with { StateVersion = default }, original with { AttachmentStateVersion = default }, original with { MapId = new(99, original.MapId.ModuleSha256) } })
        { f.Invocation = invocation; Assert.AreEqual(NpcDialogAdmission.DeniedParticipants, f.Prepare().Admission); }
        f.Invocation = original with { PrototypeId = new(7002, original.PrototypeId.OwnIdSha256, original.PrototypeId.InputSha256) }; Assert.AreEqual(NpcDialogCode.WrongContext, f.Prepare().Error!.Code);
        f.Invocation = original with { Event = (NpcDialogEvent)8 }; Assert.AreEqual(NpcDialogAdmission.UnsupportedEvent, f.Prepare().Admission);
        f.Invocation = original with { Extra = new(original.WorldId, original.MapId, original.NpcId.AreaObjectKey) }; Assert.AreEqual(NpcDialogAdmission.UnsupportedExtra, f.Prepare().Admission);
        f.Invocation = original with { CallerLine = new(99) }; Assert.AreEqual(NpcDialogCode.MalformedOperand, f.Prepare().Error!.Code);
        Throws<ArgumentException>(() => new NpcDialogWorldId(Guid.Empty)); Throws<ArgumentException>(() => new NpcDialogMapId(1, new string('A', 64)));
        Throws<ArgumentOutOfRangeException>(() => new NpcDialogScriptId(0)); Throws<ArgumentOutOfRangeException>(() => new NpcDialogLocalFlag(32));
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        Assert.Fail($"Expected {typeof(T).Name}.");
    }
}
