using System.Collections;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.NpcDialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.NpcDialog;

[TestClass, TestCategory("NpcDialogEntrySynthetic")]
public sealed class NpcDialogEntryBoundaryTests
{
    [TestMethod]
    public void Malformed_original_wire_arrays_and_duplicate_instances_never_get_to_queries()
    {
        var arrays = new List<byte[]> { NpcDialogFixture.Sap((9, 71), (9, 72)) };
        foreach (var (offset, value) in new[] { (1, 8), (5, -1), (5, int.MaxValue), (25, 0), (33, int.MinValue) })
        { var sap = NpcDialogFixture.Sap((9, 71)); NpcDialogFixture.Set(sap, offset, value); arrays.Add(sap); }
        foreach (var sap in arrays) RejectFixture(() => new(instanceSap: sap));
        RejectFixture(() => new(duplicateNpc: true));
        var prototype = NpcDialogFixture.Prototype(NpcDialogFixture.Sap((9, 71))); NpcDialogFixture.Set(prototype, 36, 7002);
        RejectFixture(() => new(prototype: prototype));
    }
    [TestMethod]
    public void Missing_location_and_non_npc_loaded_facts_cannot_be_admitted()
    {
        CheckOrReject(NpcDialogFixture.Record(NpcDialogFixture.Sap((9, 71)), location: false));
        var record = NpcDialogFixture.Record(NpcDialogFixture.Sap((9, 71))); NpcDialogFixture.Set(record, 52, 15);
        CheckOrReject(record);
    }
    [TestMethod]
    public void Absent_null_and_empty_inherited_sap_all_mean_no_attachment()
    {
        foreach (var sap in new[] { new byte[] { 0 }, NpcDialogFixture.Sap(), NpcDialogFixture.Sap((8, 82)) })
        { using var f = new NpcDialogFixture(inherit: true, prototypeSap: sap); Assert.AreEqual(NpcDialogAdmission.NoDialogAttachment, f.Prepare().Admission); }
    }
    [TestMethod]
    public void Wrong_row_raw_semantic_prototype_source_and_missing_area_binding_reject_before_calls()
    {
        using var f = new NpcDialogFixture(); var original = f.Invocation;
        var npc = original.NpcId; var hash = new string('b', 64);
        foreach (var changed in new[] { new NpcDialogNpcId(npc.WorldId, npc.MapId, npc.AreaObjectKey, hash, npc.SemanticSha256),
            new(npc.WorldId, npc.MapId, npc.AreaObjectKey, npc.RawSha256, hash),
            new(npc.WorldId, npc.MapId, npc.AreaObjectKey with { SourceIndex = 42 }, npc.RawSha256, npc.SemanticSha256) })
        { f.Invocation = original with { NpcId = changed }; Assert.AreEqual(NpcDialogCode.WrongContext, f.Prepare().Error!.Code); }
        f.Invocation = original with { PrototypeId = new(original.PrototypeId.Number, hash, original.PrototypeId.InputSha256) };
        Assert.AreEqual(NpcDialogCode.WrongContext, f.Prepare().Error!.Code);
        var version = new NpcDialogSourceVersion(original.SourceVersion.Generation, "explicit-selected-archives-v1", original.SourceVersion.Bindings.Where(b => b.Role != "Area:Prototype:7001"));
        var policy = new NpcDialogResourcePolicy("explicit-selected-archives-v1", version, f.Resources.ScriptSourceId, f.Resources.DialogSourceId, f.Resources.Sources);
        Assert.AreEqual(NpcDialogCode.WrongSource, NpcDialogEntryResolver.Prepare(f.Snapshot, original with { SourceVersion = version }, policy, new()).Error!.Code);
        Assert.AreEqual(NpcDialogCode.WrongSource, NpcDialogEntryResolver.Prepare(f.Snapshot, original, policy, new()).Error!.Code);
    }
    [TestMethod]
    public void Exact_structural_caps_and_each_one_below_rechecked_before_external_ports()
    {
        using var f = new NpcDialogFixture(); var p = f.Prepare().Plan!;
        var exact = new NpcDialogEntryLimits { MaxScriptBytes = 1252, MaxStoredResourceBytes = 1252, MaxScriptEntries = 10,
            MaxDialogBytes = NpcDialogFixture.Dlg().Length, MaxDialogRecords = 3, MaxDialogFieldBytes = 8,
            MaxCatalogueEntries = 1, MaxCatalogueNameBytes = 22, MaxTotalCatalogueNameBytes = 22, MaxGraphDepth = 5, MaxSteps = 5, MaxQueryCalls = 10 };
        Assert.AreEqual(NpcDialogAdmission.Admitted, f.Prepare(exact).Admission);
        Assert.AreEqual(NpcDialogResolutionKind.Resolved, NpcDialogEntryResolver.Resolve(p, new EntryQueries(), exact).Kind);
        foreach (var l in new[] { exact with { MaxScriptBytes = 1251 }, exact with { MaxStoredResourceBytes = 1251 }, exact with { MaxScriptEntries = 9 },
            exact with { MaxDialogBytes = NpcDialogFixture.Dlg().Length - 1 }, exact with { MaxDialogRecords = 2 }, exact with { MaxDialogFieldBytes = 7 },
            exact with { MaxCatalogueNameBytes = 21 }, exact with { MaxTotalCatalogueNameBytes = 21 }, exact with { MaxGraphDepth = 4 } })
        {
            Assert.IsNull(f.Prepare(l).Plan); var q = new EntryQueries(); var r = NpcDialogEntryResolver.Resolve(p, q, l);
            Assert.AreEqual(NpcDialogCode.Budget, r.Error!.Code); Assert.AreEqual(0, q.Calls.Count);
        }
        using var catalogue = new NpcDialogFixture(scriptNames: ["scr\\70Other.scr", "scr\\00071Invented.scr"]);
        Assert.AreEqual(NpcDialogCode.Budget, catalogue.Prepare(new() { MaxCatalogueEntries = 1 }).Error!.Code);
    }
    [TestMethod]
    public void All_limits_reject_attempts_to_raise_hard_caps_and_nonpositive_values()
    {
        using var f = new NpcDialogFixture();
        foreach (var property in typeof(NpcDialogEntryLimits).GetProperties())
        foreach (var value in new[] { -1, 0, (int)property.GetValue(new NpcDialogEntryLimits())! + 1 })
        {
            var limits = new NpcDialogEntryLimits(); property.SetValue(limits, value);
            Assert.AreEqual(NpcDialogCode.InvalidLimits, f.Prepare(limits).Error!.Code);
        }
    }
    [TestMethod]
    public void Maximum_count_capacity_and_last_caller_line_no_capacity_allocation_or_magic_entry()
    {
        var script = new byte[64 + 4096 * 132]; NpcDialogFixture.Set(script, 52, 4096); NpcDialogFixture.Set(script, 56, 4096);
        NpcDialogFixture.Operation(script, 4095, 44, 4, 3, 999);
        using var f = new NpcDialogFixture(script: script); f.Invocation = f.Invocation with { CallerLine = new(4095) };
        var p = f.Prepare(); Assert.AreEqual(NpcDialogAdmission.Admitted, p.Admission);
        var r = NpcDialogEntryResolver.Resolve(p.Plan!, new EntryQueries(), new()); Assert.AreEqual(4095, r.Entry!.ScrLine.Value); Assert.AreEqual(999, r.Entry.DlgNode.Value);
    }
    [TestMethod]
    public void Supported_goto_and_local_flag_lower_bounds_and_wrong_focus_are_explicit()
    {
        foreach (var (line, component, type, tag, value, code) in new (int, int, int, byte, int, NpcDialogCode)[]
        { (0,44,3,3,-1,NpcDialogCode.MalformedOperand), (1,0,3,3,-1,NpcDialogCode.MalformedOperand),
            (0,0,18,0,0,NpcDialogCode.UnsupportedOperand), (2,0,39,0,0,NpcDialogCode.UnsupportedOperand) })
        {
            var script = NpcDialogFixture.Scr(); NpcDialogFixture.Operation(script,line,component,type,tag,value);
            using var f = new NpcDialogFixture(script: script); var p = f.Prepare();
            if (code == NpcDialogCode.MalformedOperand) { Assert.IsNull(p.Plan); Assert.AreEqual(code,p.Error!.Code); }
            else { var r = NpcDialogEntryResolver.Resolve(p.Plan!,new EntryQueries(),new()); Assert.AreEqual(code,r.Frontier!.Code); }
        }
    }
    [TestMethod]
    public void Cancellation_thrown_inside_boolean_and_version_ports_is_cooperative_not_failure()
    {
        using var f = new NpcDialogFixture(); var plan = f.Prepare().Plan!;
        foreach (var versions in new[] { false, true })
        {
            using var cts = new CancellationTokenSource(); var q = new EntryQueries();
            if (versions) q.Versions = _ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); };
            else q.Boolean = _ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); };
            Assert.AreEqual(NpcDialogCode.Cancelled, NpcDialogEntryResolver.Resolve(plan,q,new(),cts.Token).Error!.Code);
        }
    }
    [TestMethod]
    public void Failed_preparation_keeps_borrowed_positions_and_source_binding_is_deeply_owned()
    {
        using var f = new NpcDialogFixture(scriptNames: ["scr\\70Other.scr"]);
        foreach (var s in f.Streams.TakeLast(2)) s.Position = 5;
        Assert.AreEqual(NpcDialogCode.MissingResource,f.Prepare().Error!.Code);
        foreach (var s in f.Streams.TakeLast(2)) { Assert.IsTrue(s.CanRead); Assert.AreEqual(5L,s.Position); }
        var bindings = f.Invocation.SourceVersion.Bindings.ToList(); var version = new NpcDialogSourceVersion(Guid.NewGuid(),"explicit-selected-archives-v1",bindings);
        bindings.Clear(); Assert.AreEqual(5,version.Bindings.Count);
        Throws<NotSupportedException>(() => ((IList)version.Bindings).Clear());
        Throws<ArgumentException>(() => new NpcDialogSourceVersion(Guid.NewGuid(),"explicit-selected-archives-v1",[version.Bindings[0],version.Bindings[0]]));
        Throws<ArgumentException>(() => new NpcDialogResourceSource(Guid.NewGuid(),new MemoryStream(),-1,new string('b',64)));
    }
    [TestMethod]
    public void Selecting_entry_never_executes_dialog_fields_or_old_runtime_ports()
    {
        var dialog = Encoding.ASCII.GetBytes("{901}{invented}{}{}{}{}{GF 2 1 IN -7 QU 20 2 RU 15}\n{999}{other}{}{}{}{}{}");
        using var f = new NpcDialogFixture(dialog: dialog);
        var p = f.Prepare(); Assert.AreEqual(NpcDialogAdmission.Admitted,p.Admission);
        var spy = new QueryAndCommandSpy();
        Assert.AreEqual(NpcDialogResolutionKind.Resolved,NpcDialogEntryResolver.Resolve(p.Plan!,spy,new()).Kind);
        Assert.AreEqual(0,spy.OldPortCalls);
        var old = DialogTransitionRuntime.Start(DlgReader.Read(dialog),new(new(901),new(new(5),new(6),new(7)),[]),spy,spy);
        Assert.AreEqual(DialogStartStatus.Failed,old.Status);
        Assert.AreEqual(DialogFailureKind.UnsupportedOperation,old.Failure!.Kind);
        Assert.AreEqual(0,spy.OldPortCalls);
        Assert.IsTrue(typeof(INpcDialogEntryQueries).GetMethods().All(m => m.Name is "ReadVersions" or "Following" or "Jilted" or "LocalFlag"));
        Assert.IsTrue(typeof(NpcDialogResolvedEntry).GetProperties().All(p => p.PropertyType != typeof(Arcanum.Data.DlgDocument)));
    }
    [TestMethod]
    public void Every_remaining_effect_and_six_reply_frontier_stays_in_the_old_runtime()
    {
        foreach (var action in new[] { "GF 2 1", "IN -7", "RE 5" })
        {
            var dialog = Encoding.ASCII.GetBytes($"{{901}}{{invented}}{{}}{{}}{{}}{{}}{{{action}}}\n{{999}}{{other}}{{}}{{}}{{}}{{}}{{}}");
            using var f = new NpcDialogFixture(dialog: dialog);
            var spy = new QueryAndCommandSpy();
            Assert.AreEqual(NpcDialogResolutionKind.Resolved, NpcDialogEntryResolver.Resolve(f.Prepare().Plan!, spy, new()).Kind);
            var old = DialogTransitionRuntime.Start(DlgReader.Read(dialog), new(new(901), new(new(5), new(6), new(7)), []), spy, spy);
            Assert.AreEqual(DialogFailureKind.UnsupportedOperation, old.Failure!.Kind);
            Assert.AreEqual(0, spy.OldPortCalls);
        }
        var replies = string.Concat(new[] { 201, 202, 203, 204, 205, 206 }.Select(id => $"{{{id}}}{{invented}}{{}}{{1}}{{}}{{0}}{{}}"));
        var six = DlgReader.Read(Encoding.ASCII.GetBytes("{901}{invented}{}{}{}{}{}" + replies));
        var untouched = new QueryAndCommandSpy();
        var eligibility = new[] { 201, 202, 203, 204, 205, 206 }.Select(id => new DialogResponseEligibility(new(id), true, true)).ToArray();
        var result = DialogTransitionRuntime.Start(six, new(new(901), new(new(5), new(6), new(7)), eligibility), untouched, untouched);
        Assert.AreEqual(DialogFailureKind.ResponseLimitExceeded, result.Failure!.Kind);
        Assert.AreEqual(0, untouched.OldPortCalls);
    }
    [TestMethod]
    public void Changed_script_id_and_flag_extremes_resolve_from_literal_data()
    {
        var script = NpcDialogFixture.Scr(3);
        NpcDialogFixture.Operation(script, 0, 0, 3, 3, 0);
        NpcDialogFixture.Operation(script, 0, 44, 3, 3, 2);
        NpcDialogFixture.Operation(script, 1, 0, 3, 3, 31);
        NpcDialogFixture.Operation(script, 1, 44, 4, 3, 999);
        NpcDialogFixture.Operation(script, 2, 44, 4, 3, 901);
        using var f = new NpcDialogFixture(script: script, instanceSap: NpcDialogFixture.Sap((9, 95)),
            scriptNames: ["scr\\00095Changed.scr"], dialogNames: ["dlg\\00095Changed.dlg"]);
        var plan = f.Prepare().Plan!;
        Assert.AreEqual(95, plan.Attachment.ScriptId.Value);
        var q = new EntryQueries();
        var r = NpcDialogEntryResolver.Resolve(plan, q, new());
        Assert.AreEqual(901, r.Entry!.DlgNode.Value); Assert.AreEqual(2, r.Entry.ScrLine.Value);
        CollectionAssert.AreEqual(new[] { "V0", "L0:0", "V0", "L31:1", "V1", "V2" }, q.Calls);
        var yes = new EntryQueries(); yes.Answers.Enqueue(NpcDialogQueryStatus.KnownFalse); yes.Answers.Enqueue(NpcDialogQueryStatus.KnownTrue);
        Assert.AreEqual(999, NpcDialogEntryResolver.Resolve(plan, yes, new()).Entry!.DlgNode.Value);
        foreach (var number in new[] { -1, 100000 })
        {
            using var invalid = new NpcDialogFixture(instanceSap: NpcDialogFixture.Sap((9, number)));
            Assert.AreEqual(NpcDialogCode.MalformedAttachment, invalid.Prepare().Error!.Code);
        }
    }
    private static void RejectFixture(Func<NpcDialogFixture> create) => Throws<InvalidOperationException>(() => { using var f = create(); });
    [TestMethod]
    public void Cancellation_during_streamed_authentication_restores_the_borrowed_handle()
    {
        using var f = new NpcDialogFixture(); using var cts = new CancellationTokenSource();
        using var stream = new CancellingReadStream(f.ScriptArchive,cts); stream.Position=7;
        var binding=f.Invocation.SourceVersion.Bindings.Single(b => b.Role=="Script");
        var source=new NpcDialogResourceSource(binding.InputId,stream,binding.Length,binding.Sha256);
        var policy=new NpcDialogResourcePolicy("explicit-selected-archives-v1",f.Invocation.SourceVersion,f.Resources.ScriptSourceId,f.Resources.DialogSourceId,[source,f.Resources.Sources[1]]);
        var p=NpcDialogEntryResolver.Prepare(f.Snapshot,f.Invocation,policy,new(),cts.Token);
        Assert.AreEqual(NpcDialogCode.Cancelled,p.Error!.Code); Assert.IsNull(p.Plan);
        Assert.AreEqual(7L,stream.Position); Assert.IsTrue(stream.CanRead);
    }
    private sealed class CancellingReadStream(byte[] bytes,CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer,int offset,int count)
        { var read=base.Read(buffer,offset,count); cancellation.Cancel(); return read; }
    }
    private sealed class QueryAndCommandSpy : INpcDialogEntryQueries,IDialogStateQueryPort,IDialogCommandPort
    {
        private readonly EntryQueries _queries=new();
        internal int OldPortCalls { get; private set; }
        public NpcDialogVersionReply ReadVersions(NpcDialogQueryContext c) => _queries.ReadVersions(c);
        public NpcDialogQueryReply Following(NpcDialogQueryContext c,NpcDialogNpcId npc,NpcDialogPcId pc) => _queries.Following(c,npc,pc);
        public NpcDialogQueryReply Jilted(NpcDialogQueryContext c,NpcDialogNpcId npc) => _queries.Jilted(c,npc);
        public NpcDialogQueryReply LocalFlag(NpcDialogQueryContext c,NpcDialogAttachmentId attachment,NpcDialogLocalFlag flag) => _queries.LocalFlag(c,attachment,flag);
        public LocalFlagReadResult Read(LocalFlagQuery query) { OldPortCalls++; throw new InvalidOperationException(); }
        public DialogCommandResult Execute(LocalFlagWriteCommand command) { OldPortCalls++; throw new InvalidOperationException(); }
        public DialogCommandResult Execute(QuestStateCommand command) { OldPortCalls++; throw new InvalidOperationException(); }
        public DialogCommandResult Execute(RumorLogCommand command) { OldPortCalls++; throw new InvalidOperationException(); }
    }
    private static void CheckOrReject(byte[] record)
    {
        NpcDialogFixture f;
        try { f = new(record: record); } catch (InvalidOperationException) { return; }
        using (f) Assert.AreNotEqual(NpcDialogAdmission.Admitted,f.Prepare().Admission);
    }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } Assert.Fail($"Expected {typeof(T).Name}."); }
}
