using System.IO.Compression;
using System.Text;
using Arcanum.Scripting.NpcDialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.NpcDialog;

[TestClass, TestCategory("NpcDialogEntrySynthetic")]
public sealed class NpcDialogEntryResourceTests
{
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
