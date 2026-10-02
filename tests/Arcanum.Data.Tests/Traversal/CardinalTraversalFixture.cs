using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;

namespace Arcanum.Data.Tests.Traversal;

// Independent literal byte writers, never production schema constants or permission rows.
internal sealed class CardinalTraversalFixture : IDisposable
{
    internal const string SectorPath = "maps\\Arcanum1-024-fixed\\86570436012.sec";
    internal const string NamePath = "art\\tile\\tilename.mes";
    internal readonly byte[] ModuleBytes, ResourceBytes;
    internal readonly ReadTrap ModuleStream, ResourceStream;
    internal readonly DatArchive Module, Resource;
    internal readonly string PrototypeRoot;
    internal readonly StaticObjectSource Source;
    internal readonly StaticObjectPrototypeSource Prototypes;
    internal TerrainSectorSelector Selector => new(SectorPath, "BASE_SECTOR_1452_1290", 1452, 1290, 86570436012, 92928, 82560, 64, 64);
    internal CardinalTraversalFixture(byte[][]? records = null, uint? destination = null, bool mask = true,
        int blockedOrdinal = -1, string? names = "{0}{aaa}{300}{aaa}", bool duplicateNames = false, byte[]? sector = null)
    {
        PrototypeRoot = Path.Combine(Path.GetTempPath(), "cardinal-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PrototypeRoot);
        ModuleBytes = Dat((SectorPath, sector ?? Sector(records ?? [], destination, mask, blockedOrdinal)));
        var entries = names is null ? new List<(string, byte[])>() : [(NamePath, Encoding.ASCII.GetBytes(names))];
        if (duplicateNames) entries.Add((NamePath, Encoding.ASCII.GetBytes(names!)));
        ResourceBytes = Dat(entries.ToArray());
        ModuleStream = new(ModuleBytes); ResourceStream = new(ResourceBytes);
        Module = DatArchive.Open(ModuleStream, leaveOpen: true); Resource = DatArchive.Open(ResourceStream, leaveOpen: true);
        Source = new(Module, SectorPath, Resource); Prototypes = new(PrototypeRoot);
    }
    internal CardinalTraversalSnapshot Inspect(bool nameSource = true, long epoch = 7, TerrainLoaderOptions? terrain = null, StaticObjectLoaderOptions? objects = null) =>
        CardinalTraversalInspector.InspectSector(Source, Prototypes, "MODULE", "RESOURCE", "PROTOTYPES", Selector,
            nameSource ? new(Resource, "RESOURCE", NamePath, TerrainPathProfile.CeNonWindowsMacOs) : null, epoch, terrain, objects);
    internal void PutPrototype(int aid, uint flags, byte[]? bytes = null, string? filename = null) =>
        File.WriteAllBytes(Path.Combine(PrototypeRoot, filename ?? $"{aid:000000} - fixture.pro"), bytes ?? StaticObjects.StaticObjectLoaderTests.Prototype(aid, 123, flags));
    internal static long World(int x = 92933, int y = 82561) => unchecked((long)((ulong)(uint)x | ((ulong)(uint)y << 32)));
    internal static byte[] Record(uint? flags = 1024, long? location = null, int type = 3, int aid = 7001, uint slot19 = 0)
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(119); Id(w, 1, aid); Id(w, 1, 1); w.Write(type); w.Write((short)(flags.HasValue ? 4 : 3));
        w.Write(1u | 2u | (flags.HasValue ? 1u << 18 : 0u) | 1u << 19);
        for (var i = 1; i < (type is >= 5 and <= 14 ? 4 : type is 15 or 16 ? 5 : 3); i++) w.Write(0u);
        w.Write(123); w.Write((byte)1); w.Write(location ?? World()); if (flags.HasValue) w.Write(flags.Value); w.Write(slot19);
        return s.ToArray();
    }
    internal static byte[] AbsentLocation(uint? flags = 1024) => StaticObjects.StaticObjectLoaderTests.Record(7001, 123, flags, 0, location: null);
    private static void Id(BinaryWriter w, short kind, int aid) { w.Write(kind); w.Write(new byte[6]); w.Write(aid); w.Write(new byte[12]); }
    internal static byte[] Sector(byte[][] records, uint? destination = null, bool mask = true, int blockedOrdinal = -1)
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(0); for (var i = 0; i < 4096; i++) w.Write(i == 69 ? destination ?? 0x180u : 0x180u);
        w.Write(1); w.Write(mask ? 0xAA0004 : 0xAA0002); w.Write(2); w.Write(new byte[48]);
        w.Write(new byte[12]); if (mask) { w.Write(new byte[24]); for (var i = 0; i < 128; i++) w.Write(blockedOrdinal >= 0 && i == blockedOrdinal / 32 ? 1u << (blockedOrdinal % 32) : 0u); }
        foreach (var r in records) w.Write(r); w.Write(records.Length); return s.ToArray();
    }
    internal static byte[] Dat(params (string Name, byte[] Payload)[] entries)
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s); var offsets = new List<uint>();
        foreach (var e in entries) { offsets.Add((uint)s.Position); w.Write(e.Payload); }
        using var index = new MemoryStream(); using var iw = new BinaryWriter(index); iw.Write((uint)entries.Length);
        for (var i = 0; i < entries.Length; i++) { var text = Encoding.ASCII.GetBytes(entries[i].Name + "\0"); iw.Write((uint)text.Length); iw.Write(text); iw.Write(0u); iw.Write(1u); iw.Write((uint)entries[i].Payload.Length); iw.Write((uint)entries[i].Payload.Length); iw.Write(offsets[i]); }
        w.Write(index.ToArray()); w.Write(new byte[16]); w.Write("1TAD"u8); w.Write(0u); w.Write((uint)(index.Length + 28)); return s.ToArray();
    }
    internal static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    public void Dispose() { Module.Dispose(); Resource.Dispose(); ModuleStream.Dispose(); ResourceStream.Dispose(); if (Directory.Exists(PrototypeRoot)) Directory.Delete(PrototypeRoot, true); }
    internal sealed class ReadTrap(byte[] bytes) : MemoryStream(bytes)
    {
        internal bool Armed; internal int ForbiddenReads;
        private void Guard() { if (Armed) { ForbiddenReads++; throw new IOException("private stream read forbidden"); } }
        public override int Read(byte[] b, int o, int c) { Guard(); return base.Read(b, o, c); }
        public override int Read(Span<byte> b) { Guard(); return base.Read(b); }
        public override int ReadByte() { Guard(); return base.ReadByte(); }
    }
}
