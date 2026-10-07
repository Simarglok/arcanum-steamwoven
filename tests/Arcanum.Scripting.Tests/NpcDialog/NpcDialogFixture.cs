using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Scripting.NpcDialog;

namespace Arcanum.Scripting.Tests.NpcDialog;

// Independently authored literal v119/DAT/ART/SCR fixtures. No production encoder,
// opcode constants, schema reflection, research Layouts or expected-result resolver.
internal sealed class NpcDialogFixture : IDisposable
{
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal readonly List<MemoryStream> Streams = [];
    internal AreaDataOwner Owner { get; }
    internal AreaSnapshot Snapshot { get; }
    internal NpcDialogInvocation Invocation { get; set; }
    internal NpcDialogResourcePolicy Resources { get; }
    internal byte[] ScriptArchive { get; }
    internal byte[] DialogArchive { get; }
    internal NpcDialogFixture(byte[]? script = null, byte[]? dialog = null, byte[]? instanceSap = null, byte[]? prototypeSap = null,
        bool inherit = false, string[]? scriptNames = null, string[]? dialogNames = null, byte[]? record = null, byte[]? prototype = null, bool duplicateNpc = false)
    {
        var npc = record ?? Record(inherit ? null : instanceSap ?? Sap((9, 71)), parentPadding: true);
        var proto = prototype ?? Prototype(prototypeSap ?? Sap((9, 82)));
        var module = Dat(("maps/test/67108865.sec", Sector()), ("maps/test/67108866.sec", duplicateNpc ? Sector(npc, npc) : Sector(npc)));
        var resource = Dat(("tile.mes", Encoding.ASCII.GetBytes("{0}{stone}{100}{stone}{200}{stone}{300}{stone}{400}{end}")),
            ("facade.mes", "{0}{fixture}"u8.ToArray()), ("scenery.mes", "{0}{fixture.art}"u8.ToArray()), ("art/tile/stonebse0a.art", Art()));
        var files = new[] { (Key: new AreaInputKey(AreaSourceRole.Module), Bytes: module), (Key: new AreaInputKey(AreaSourceRole.Resource1), Bytes: resource), (Key: new AreaInputKey(AreaSourceRole.Prototype, 7001), Bytes: proto) };
        var inputs = files.Select(f => new AreaInput(f.Key, Stream(f.Bytes), leaveOpen: true)).ToArray();
        using var archive = DatArchive.Open(new MemoryStream(resource));
        var mappings = new[] { AreaMappingRole.Tile, AreaMappingRole.Facade, AreaMappingRole.Scenery }.Select((role, index) =>
        {
            var e = archive.Entries[index];
            return new AreaMappingBinding(role, new(new(AreaSourceRole.Resource1), index, 1, e.StoredSize, e.DeclaredOutputSize, e.Offset, Hash(e.RawNameBytes.ToArray())), Hash(archive.ReadEntry(e)));
        });
        var plan = new AreaSourcePlan(1, 23, new(1, 1), 2, "maps/test", files.Select(f => new AreaInputIdentity(f.Key, f.Bytes.Length, Hash(f.Bytes))), mappings);
        Owner = AreaDataOwner.Open(new(inputs), plan, new());
        var inspection = Owner.Inspect();
        if (!inspection.IsComplete)
        {
            Owner.Dispose(); foreach (var stream in Streams) stream.Dispose();
            throw new InvalidOperationException(string.Join(',', inspection.Issues));
        }
        Snapshot = inspection.Snapshot!;
        var row = Snapshot.Objects.Single();
        var world = new NpcDialogWorldId(new Guid("00000000-0000-0000-0000-000000000001"));
        var map = new NpcDialogMapId(23, Hash(module));
        ScriptArchive = Dat((scriptNames ?? [inherit ? "scr\\00082Invented.scr" : "scr\\00071Invented.scr"]).Select(name => (name, script ?? Scr())).ToArray());
        DialogArchive = Dat((dialogNames ?? [inherit ? "dlg\\00082Invented.dlg" : "dlg\\00071Invented.dlg"]).Select(name => (name, dialog ?? Dlg())).ToArray());
        var scriptId = new Guid("00000000-0000-0000-0000-000000000002"); var dialogId = new Guid("00000000-0000-0000-0000-000000000003");
        var bindings = files.Select(f => new NpcDialogSourceBinding($"Area:{f.Key.Role}:{f.Key.Number}", Guid.NewGuid(), f.Bytes.Length, Hash(f.Bytes))).ToList();
        bindings.Add(new("Script", scriptId, ScriptArchive.Length, Hash(ScriptArchive))); bindings.Add(new("Dialog", dialogId, DialogArchive.Length, Hash(DialogArchive)));
        var version = new NpcDialogSourceVersion(Guid.NewGuid(), "explicit-selected-archives-v1", bindings);
        Invocation = new(world, map, new(world, map, row.Key, row.RawSha256, row.SemanticSha256), new(7001, row.PrototypeOwnSha256, Hash(proto)), new(world, map, Guid.NewGuid()),
            (NpcDialogEvent)9, new(0), version, new(Guid.NewGuid(), 17), new(Guid.NewGuid(), 29));
        Resources = new("explicit-selected-archives-v1", version, scriptId, dialogId,
            [new(scriptId, Stream(ScriptArchive), ScriptArchive.Length, Hash(ScriptArchive)), new(dialogId, Stream(DialogArchive), DialogArchive.Length, Hash(DialogArchive))]);
    }
    private MemoryStream Stream(byte[] bytes) { var stream = new MemoryStream(bytes, writable: true); Streams.Add(stream); return stream; }
    internal NpcDialogPreparation Prepare(NpcDialogEntryLimits? limits = null, CancellationToken token = default) => NpcDialogEntryResolver.Prepare(Snapshot, Invocation, Resources, limits ?? new(), token);
    public void Dispose() { Owner.Dispose(); foreach (var stream in Streams) stream.Dispose(); }

    internal static byte[] Id(short kind, int number)
    { var b = new byte[24]; BinaryPrimitives.WriteInt16LittleEndian(b, kind); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), number); return b; }
    internal static byte[] Record(byte[]? sap, bool parentPadding = false, int type = 16, bool location = true)
    {
        using var s = new MemoryStream(); I32(s, 119); var parent = Id(1, 7001); if (parentPadding) parent[3] = 91;
        s.Write(parent); s.Write(Id(1, 431)); I32(s, type);
        s.WriteByte((byte)((sap is null ? 0 : 1) + (location ? 1 : 0))); s.WriteByte(0);
        I32(s, (location ? 2 : 0) | (sap is null ? 0 : int.MinValue)); I32(s, 0); I32(s, 0); I32(s, 0); I32(s, 0);
        if (location) { s.WriteByte(1); I64(s, 0); }
        if (sap is not null) s.Write(sap);
        return s.ToArray();
    }
    internal static byte[] Prototype(byte[] sap)
    {
        // Explicit test-local field kinds in three independent mask groups.
        string[] groups = ["i,p,i,i,i,a,a,a,i,i,i,i,i,i,i,a,a,a,i,i,i,i,i,i,i,i,i,i,i,i,a,a,i,i,a,a",
            "i,i,a,a,a,a,i,i,i,i,a,a,p,i,p,p,p,p,p,i,a,i,i,a,p,i,i,i,i,i,i,a,a",
            "i,p,i,p,p,i,i,a,i,p,p,i,i,i,p,i,i,a,a,a,i,i,i,a,a"];
        using var s = new MemoryStream(); I32(s, 119); s.Write(Id(-1, 0)); s.Write(Id(1, 7001)); I32(s, 16);
        for (var mask = 0; mask < 5; mask++) I32(s, 0);
        var slot = 0;
        foreach (var kind in groups.SelectMany(g => g.Split(',')))
        {
            if (slot == 31) s.Write(sap);
            else if (kind != "i") s.WriteByte(0);
            else I32(s, slot == 11 ? 100 : 0);
            slot++;
        }
        return s.ToArray();
    }
    internal static byte[] Sap(params (int Key, int Script)[] elements)
    {
        using var s = new MemoryStream(); s.WriteByte(1); I32(s, 12); I32(s, elements.Length); I32(s, 0);
        foreach (var e in elements.OrderBy(e => e.Key)) { I32(s, 0x12); I32(s, 0x34); I32(s, e.Script); }
        I32(s, 2); I32(s, elements.Where(e => e.Key < 32).Aggregate(0, (m, e) => m | (1 << e.Key))); I32(s, elements.Where(e => e.Key >= 32).Aggregate(0, (m, e) => m | (1 << (e.Key - 32))));
        return s.ToArray();
    }
    internal static byte[] Sector(params byte[][] records)
    {
        using var s = new MemoryStream(); I32(s, 0); s.Write(new byte[16384]); I32(s, 1); I32(s, 0xAA0003); I32(s, 0); s.Write(new byte[12]); I32(s, records.Length == 0 ? 2 : 3); s.Write(new byte[20]);
        foreach (var record in records) s.Write(record); I32(s, records.Length); return s.ToArray();
    }
    private static byte[] Art()
    {
        var b = new byte[1185]; Set(b, 0, 1); Set(b, 8, 8); Set(b, 12, 1); Set(b, 32, 1); b[136] = 3; b[137] = 2; b[138] = 1;
        Set(b, 1156, 1); Set(b, 1160, 1); Set(b, 1164, 1); b[1184] = 1; return b;
    }
    internal static byte[] Dat(params (string Name, byte[] Bytes)[] entries)
    {
        using var s = new MemoryStream(); var offsets = new List<int>();
        foreach (var e in entries) { offsets.Add((int)s.Position); s.Write(e.Bytes); }
        var start = s.Position; I32(s, entries.Length);
        for (var i = 0; i < entries.Length; i++) { var name = Encoding.ASCII.GetBytes(entries[i].Name + '\0'); I32(s, name.Length); s.Write(name); I32(s, 0); I32(s, 1); I32(s, entries[i].Bytes.Length); I32(s, entries[i].Bytes.Length); I32(s, offsets[i]); }
        var indexSize = s.Position - start; s.Write(new byte[16]); s.Write("1TAD"u8); I32(s, 0); I32(s, checked((int)indexSize + 28)); return s.ToArray();
    }
    internal static byte[] Dlg() => Encoding.ASCII.GetBytes("{901}{invented}{}{}{}{}{}\n{999}{other}{}{}{}{}{}\n{777}{player}{}{1}{}{901}{}");
    internal static byte[] Scr(int count = 9)
    {
        var b = new byte[64 + 132 * count]; Set(b, 52, count); Set(b, 56, count + 1);
        if (count < 9) { Operation(b, 0, 44, 4, 3, 901); return b; }
        Operation(b, 0, 0, 18, 1, 0); b[64 + 5] = 0; Operation(b, 0, 44, 3, 3, 5);
        Operation(b, 1, 0, 3, 3, 4); Operation(b, 1, 44, 3, 3, 6);
        Operation(b, 2, 0, 39, 1, 0); Operation(b, 2, 44, 3, 3, 7);
        Operation(b, 3, 0, 3, 3, 11); Operation(b, 3, 44, 3, 3, 8);
        Operation(b, 4, 44, 4, 3, 901);
        for (var i = 5; i < 9; i++) Operation(b, i, 44, 4, 3, 999);
        return b;
    }
    internal static void Operation(byte[] b, int line, int component, int type, byte tag, int value)
    { var o = 64 + 132 * line + component; Set(b, o, type); b[o + 4] = tag; Set(b, o + 12, value); }
    internal static void Set(byte[] b, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(offset), value);
    internal static void I32(Stream s, int value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, value); s.Write(b); }
    internal static void I64(Stream s, long value) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(b, value); s.Write(b); }
}

internal sealed class EntryQueries : INpcDialogEntryQueries
{
    internal List<string> Calls { get; } = [];
    internal List<NpcDialogQueryContext> Contexts { get; } = [];
    internal Queue<NpcDialogQueryStatus> Answers { get; } = new();
    internal Func<NpcDialogQueryContext, NpcDialogVersionReply>? Versions { get; set; }
    internal Func<NpcDialogQueryContext, NpcDialogQueryReply>? Boolean { get; set; }
    internal NpcDialogVersionReply Version(NpcDialogQueryContext c) => new(c, NpcDialogVersionStatus.Known, c.Invocation.SourceVersion, c.Invocation.StateVersion, c.Invocation.AttachmentStateVersion);
    public NpcDialogVersionReply ReadVersions(NpcDialogQueryContext c) { Calls.Add($"V{c.Line.Value}"); Contexts.Add(c); return Versions?.Invoke(c) ?? Version(c); }
    private NpcDialogQueryReply Read(NpcDialogQueryContext c, string name) { Calls.Add(name + c.Line.Value); Contexts.Add(c); return Boolean?.Invoke(c) ?? new(c, Answers.Count == 0 ? NpcDialogQueryStatus.KnownFalse : Answers.Dequeue()); }
    public NpcDialogQueryReply Following(NpcDialogQueryContext c, NpcDialogNpcId npc, NpcDialogPcId pc)
    { if (npc != c.Invocation.NpcId || pc != c.Invocation.PcId) throw new InvalidOperationException(); return Read(c, "F"); }
    public NpcDialogQueryReply Jilted(NpcDialogQueryContext c, NpcDialogNpcId npc)
    { if (npc != c.Invocation.NpcId) throw new InvalidOperationException(); return Read(c, "J"); }
    public NpcDialogQueryReply LocalFlag(NpcDialogQueryContext c, NpcDialogAttachmentId attachment, NpcDialogLocalFlag flag)
    { if (!ReferenceEquals(attachment, c.Attachment)) throw new InvalidOperationException(); return Read(c, $"L{flag.Value}:"); }
}
