using System.IO.Compression;
using System.Text;
using Arcanum.Scripting.NpcDialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.NpcDialog;

[TestClass, TestCategory("NpcDialogEntrySynthetic")]
public sealed class NpcDialogEntryResourceTests
{
    [TestMethod]
    public void Unsupported_dlg_syntax_is_a_container_frontier_before_any_query()
    {
        // Literal invented payloads exercise DlgReader through authenticated
        // selected DAT entries, not a mocked reader or resolver exception.
        foreach (var text in new[]
        {
            "{901} {}{}{}{}{}{}", "{901}// invented-private-dlg\n{}{}{}{}{}{}",
            "{901}{invented-private-dlg{}}{}{}{}{}{}", "{901}{}{}{}{}{}{}\t",
            "{901}{}{}{}{}{}{}\r", "{901}{}{}{}{}{}{}\x80", "//\x80\n{901}{}{}{}{}{}{}",
        })
        {
            using var f = new NpcDialogFixture(dialog: Encoding.Latin1.GetBytes(text));
            f.Invocation = f.Invocation with { CallerLine = new(2) };
            AssertContainerFailure(f, NpcDialogCode.UnsupportedRepresentation, frontier: true);
        }
    }
    [TestMethod]
    public void Malformed_dlg_structure_remains_a_container_error_before_any_query()
    {
        foreach (var text in new[]
        {
            "", "{901}{}{}{}{}{}", "{901}{invented-private-dlg", "}",
            "{901}{invented-private-dlg\0}{}{}{}{}{}", "{901}{}{}{0}{}{}{}",
            "{901}{}{}{}{}{}{}{901}{}{}{}{}{}{}", "{2147483648}{}{}{}{}{}{}",
        })
        {
            using var f = new NpcDialogFixture(dialog: Encoding.Latin1.GetBytes(text));
            AssertContainerFailure(f, NpcDialogCode.MalformedContainer, frontier: false);
        }
    }
    [TestMethod]
    public void Dlg_field_and_record_limits_remain_container_budget_errors()
    {
        using var field = new NpcDialogFixture(dialog: "{901}{invented-private-dlg}{}{}{}{}{}"u8.ToArray());
        AssertContainerFailure(field, NpcDialogCode.Budget, frontier: false, new() { MaxDialogFieldBytes = 19 });
        using var records = new NpcDialogFixture(dialog: "{901}{}{}{}{}{}{}\n{999}{}{}{}{}{}{}"u8.ToArray());
        AssertContainerFailure(records, NpcDialogCode.Budget, frontier: false, new() { MaxDialogRecords = 1 });
    }
    [TestMethod]
    public void Valid_adjacent_dlg_fields_admit_and_resolve_without_leaking_borrowed_handles()
    {
        using var f = new NpcDialogFixture(dialog: " // invented-private-dlg\r\n{901}{}{}{}{}{}{}\n{999}{}{}{}{}{}{}"u8.ToArray());
        var scripts = f.Streams[^2]; var dialogs = f.Streams[^1];
        scripts.Position = 7; dialogs.Position = 11;
        var queries = new EntryQueries(); var p = f.Prepare();
        Assert.AreEqual(NpcDialogAdmission.Admitted, p.Admission); Assert.IsNotNull(p.Plan);
        Assert.IsNull(p.Error); Assert.IsNull(p.Frontier); Assert.AreEqual(0, queries.Calls.Count);
        Assert.AreEqual(7L, scripts.Position); Assert.AreEqual(11L, dialogs.Position);
        Assert.IsTrue(scripts.CanRead); Assert.IsTrue(dialogs.CanRead);
        var r = NpcDialogEntryResolver.Resolve(p.Plan, queries, new());
        Assert.AreEqual(NpcDialogResolutionKind.Resolved, r.Kind); Assert.AreEqual(901, r.Entry!.DlgNode.Value);
        CollectionAssert.AreEqual(new[] { "V0", "F0", "V0", "L4:1", "V1", "J2", "V2", "L11:3", "V3", "V4" }, queries.Calls);
        Assert.AreEqual(7L, scripts.Position); Assert.AreEqual(11L, dialogs.Position);
        Assert.IsTrue(scripts.CanRead); Assert.IsTrue(dialogs.CanRead);
    }
    private static void AssertContainerFailure(NpcDialogFixture f, NpcDialogCode code, bool frontier, NpcDialogEntryLimits? limits = null)
    {
        var scripts = f.Streams[^2]; var dialogs = f.Streams[^1];
        scripts.Position = 7; dialogs.Position = 11;
        var queries = new EntryQueries(); var p = f.Prepare(limits);
        // Even an accidentally admitted plan must not reach a query unnoticed.
        if (p.Plan is not null) NpcDialogEntryResolver.Resolve(p.Plan, queries, new());
        Assert.AreEqual(0, queries.Calls.Count); Assert.IsNull(p.Plan);
        Assert.AreEqual(NpcDialogAdmission.Rejected, p.Admission);
        if (frontier)
        {
            Assert.IsNull(p.Error); Assert.IsNotNull(p.Frontier);
            Assert.AreEqual(code, p.Frontier.Code); Assert.AreEqual(NpcDialogStage.Container, p.Frontier.Stage);
            Assert.AreEqual(f.Invocation, p.Frontier.Invocation); Assert.IsNull(p.Frontier.Line);
            Assert.IsFalse(p.Frontier.ToString().Contains("invented-private-dlg", StringComparison.Ordinal));
        }
        else
        {
            Assert.IsNull(p.Frontier); Assert.IsNotNull(p.Error);
            Assert.AreEqual(code, p.Error.Code); Assert.AreEqual(NpcDialogStage.Container, p.Error.Stage);
            Assert.AreEqual(f.Invocation, p.Error.Invocation); Assert.IsNull(p.Error.Line);
            Assert.IsFalse(p.Error.ToString().Contains("invented-private-dlg", StringComparison.Ordinal));
        }
        // Invocation equality above includes participants, caller line and all
        // source/state/attachment versions. Container failures have no decoded line.
        Assert.AreEqual(7L, scripts.Position); Assert.AreEqual(11L, dialogs.Position);
        Assert.IsTrue(scripts.CanRead); Assert.IsTrue(dialogs.CanRead);
    }
    [TestMethod]
    public void Invalid_raw_catalogue_bytes_and_uint_metadata_fail_statically()
    {
        using var f = new NpcDialogFixture();
        // The invented index begins after its 1252-byte payload: count, name
        // length, 22 raw name bytes, then opaque/flags/output/stored/offset.
        foreach (var (offset, value, code) in new[]
        {
            (1252, -1, NpcDialogCode.Budget), (1256, -1, NpcDialogCode.Budget),
            (1290, -1, NpcDialogCode.Budget), (1294, -1, NpcDialogCode.MalformedContainer),
            (1298, -1, NpcDialogCode.MalformedContainer),
        })
        {
            var bytes = (byte[])f.ScriptArchive.Clone(); NpcDialogFixture.Set(bytes, offset, value);
            var p = PrepareScript(f, bytes); Assert.IsNull(p.Plan); Assert.AreEqual(code, p.Error!.Code);
        }
        foreach (var (offset, value, code) in new (int, byte, NpcDialogCode)[]
        {
            (1265, 255, NpcDialogCode.InvalidCatalogueKey), // non-ASCII digit
            (1269, 0, NpcDialogCode.InvalidCatalogueKey), // embedded NUL
            (1281, 33, NpcDialogCode.MalformedContainer), // missing terminal NUL
        })
        {
            var bytes = (byte[])f.ScriptArchive.Clone(); bytes[offset] = value;
            var p = PrepareScript(f, bytes); Assert.IsNull(p.Plan); Assert.AreEqual(code, p.Error!.Code);
        }
        foreach (var marker in new[] { false, true })
        {
            var bytes = (byte[])f.ScriptArchive.Clone();
            if (marker) bytes[^12] = (byte)'2'; else NpcDialogFixture.Set(bytes, 1286, 8);
            var p = PrepareScript(f, bytes); Assert.IsNull(p.Plan);
            Assert.AreEqual(NpcDialogCode.UnsupportedRepresentation, p.Frontier!.Code);
        }
    }
    [TestMethod]
    public void Inflate_overshoot_and_declared_caps_use_real_dat_read_path()
    {
        using var f = new NpcDialogFixture();
        foreach (var declared in new[] { 195, 1048577 })
        {
            var payload = NpcDialogFixture.Scr(1);
            using var zipped = new MemoryStream();
            using (var z = new ZLibStream(zipped, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(payload);
            var stored = zipped.ToArray();
            var archive = NpcDialogFixture.Dat(("scr\\00071Invented.scr", stored));
            var fixedFields = stored.Length + 4 + 4 + 22;
            NpcDialogFixture.Set(archive, fixedFields + 4, 2);
            NpcDialogFixture.Set(archive, fixedFields + 8, declared);
            var p = PrepareScript(f, archive); Assert.IsNull(p.Plan);
            if (declared == 1048577) Assert.AreEqual(NpcDialogCode.Budget, p.Error!.Code);
            else Assert.AreEqual(NpcDialogCode.MalformedContainer, p.Error!.Code);
        }
    }
    [TestMethod]
    public void One_explicit_archive_can_serve_both_roles_without_alias_winners()
    {
        using var f = new NpcDialogFixture();
        var bytes = NpcDialogFixture.Dat(("dlg\\00071Invented.dlg", NpcDialogFixture.Dlg()), ("scr\\00071Invented.scr", NpcDialogFixture.Scr()));
        using var stream = new MemoryStream(bytes); stream.Position = 11;
        var id = Guid.NewGuid();
        var bindings = f.Invocation.SourceVersion.Bindings.Where(b => b.Role is not "Script" and not "Dialog").Concat(
            new[] { new NpcDialogSourceBinding("Script", id, bytes.Length, NpcDialogFixture.Hash(bytes)), new("Dialog", id, bytes.Length, NpcDialogFixture.Hash(bytes)) });
        var version = new NpcDialogSourceVersion(Guid.NewGuid(), "explicit-selected-archives-v1", bindings);
        var source = new NpcDialogResourceSource(id, stream, bytes.Length, NpcDialogFixture.Hash(bytes));
        var policy = new NpcDialogResourcePolicy("explicit-selected-archives-v1", version, id, id, [source]);
        var p = NpcDialogEntryResolver.Prepare(f.Snapshot, f.Invocation with { SourceVersion = version }, policy, new());
        Assert.AreEqual(NpcDialogAdmission.Admitted, p.Admission);
        Assert.AreEqual(1, p.Plan!.ScriptResource.Ordinal); Assert.AreEqual(0, p.Plan.DialogResource.Ordinal);
        Assert.AreEqual(11L, stream.Position); Assert.IsTrue(stream.CanRead);
        Assert.AreEqual(901, NpcDialogEntryResolver.Resolve(p.Plan, new EntryQueries(), new()).Entry!.DlgNode.Value);
        var alias = new NpcDialogResourceSource(Guid.NewGuid(), stream, bytes.Length, NpcDialogFixture.Hash(bytes));
        Throws<ArgumentException>(() => new NpcDialogResourcePolicy("explicit-selected-archives-v1", version, id, alias.SourceId, [source, alias]));
        Throws<ArgumentException>(() => new NpcDialogResourcePolicy("explicit-selected-archives-v1", version, id, id, [source, source]));
        Throws<ArgumentException>(() => new NpcDialogResourcePolicy("first-winner", version, id, id, [source]));
    }
    [TestMethod]
    public void Source_changes_at_final_authentication_never_publish_a_plan()
    {
        using var f = new NpcDialogFixture();
        using var stream = new FinalDriftStream((byte[])f.ScriptArchive.Clone()); stream.Position = 7;
        var p = PrepareScript(f, f.ScriptArchive, stream);
        Assert.AreEqual(NpcDialogCode.SourceChanged, p.Error!.Code); Assert.IsNull(p.Plan);
        Assert.AreEqual(7L, stream.Position); Assert.IsTrue(stream.CanRead);
    }
    [TestMethod]
    public void Participant_context_mismatches_and_final_cancellation_never_publish_an_entry()
    {
        using var f = new NpcDialogFixture(); var plan = f.Prepare().Plan!;
        foreach (var changed in new[]
        {
            plan.Invocation with { PcId = new(plan.Invocation.WorldId, plan.Invocation.MapId, Guid.NewGuid()) },
            plan.Invocation with { Event = (NpcDialogEvent)8 },
            plan.Invocation with { MapId = new(99, plan.Invocation.MapId.ModuleSha256) },
            plan.Invocation with { AttachmentStateVersion = new(Guid.NewGuid(), 29) },
        })
        {
            var q = new EntryQueries { Boolean = c => new(c with { Invocation = changed }, NpcDialogQueryStatus.KnownFalse) };
            var r = NpcDialogEntryResolver.Resolve(plan, q, new()); Assert.AreEqual(NpcDialogCode.WrongContext, r.Error!.Code);
            CollectionAssert.AreEqual(new[] { "V0", "F0" }, q.Calls);
        }
        var thrown = new EntryQueries { Versions = _ => throw new InvalidOperationException("private-detail") };
        Assert.AreEqual(NpcDialogCode.QueryFailed, NpcDialogEntryResolver.Resolve(plan, thrown, new()).Error!.Code);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var untouched = new EntryQueries();
        Assert.AreEqual(NpcDialogCode.Cancelled, NpcDialogEntryResolver.Resolve(plan, untouched, new(), cancelled.Token).Error!.Code);
        Assert.AreEqual(0, untouched.Calls.Count);
        using var final = new CancellationTokenSource(); var last = new EntryQueries();
        last.Versions = c => { if (c.RequestOrdinal == 10) final.Cancel(); return last.Version(c); };
        var result = NpcDialogEntryResolver.Resolve(plan, last, new(), final.Token);
        Assert.AreEqual(NpcDialogCode.Cancelled, result.Error!.Code); Assert.IsNull(result.Entry); Assert.AreEqual(10, last.Calls.Count);
    }
    private static NpcDialogPreparation PrepareScript(NpcDialogFixture f, byte[] bytes, Stream? borrowed = null)
    {
        using var owned = borrowed is null ? new MemoryStream(bytes) : null;
        var id = f.Resources.ScriptSourceId;
        var binding = new NpcDialogSourceBinding("Script", id, bytes.Length, NpcDialogFixture.Hash(bytes));
        var version = new NpcDialogSourceVersion(Guid.NewGuid(), "explicit-selected-archives-v1", f.Invocation.SourceVersion.Bindings.Select(b => b.Role == "Script" ? binding : b));
        var source = new NpcDialogResourceSource(id, borrowed ?? owned!, bytes.Length, binding.Sha256);
        var policy = new NpcDialogResourcePolicy("explicit-selected-archives-v1", version, id, f.Resources.DialogSourceId, [source, f.Resources.Sources[1]]);
        return NpcDialogEntryResolver.Prepare(f.Snapshot, f.Invocation with { SourceVersion = version }, policy, new());
    }
    private sealed class FinalDriftStream : MemoryStream
    {
        private readonly byte[] _bytes;
        private int _authenticationCount;
        internal FinalDriftStream(byte[] bytes) : base(bytes) => _bytes = bytes;
        public override long Position
        {
            get => base.Position;
            set { if (value == 0 && ++_authenticationCount == 2) _bytes[0] ^= 1; base.Position = value; }
        }
    }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } Assert.Fail($"Expected {typeof(T).Name}."); }
}
