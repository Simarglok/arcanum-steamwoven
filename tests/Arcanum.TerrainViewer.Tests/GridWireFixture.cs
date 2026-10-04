using System.Text;
using Arcanum.Data;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;

namespace Arcanum.TerrainViewer.Tests;

// Public authored wire data, not a fabricated snapshot or permission list.
internal sealed class GridWireFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "grid-wire-" + Guid.NewGuid().ToString("N"));
    private const string SectorPath = "maps\\Arcanum1-024-fixed\\86570436012.sec";
    internal GridWireFixture(bool names = true, int uncertainRecords = 0, bool tileBlocker = false,
        CardinalTraversalAdmissionPolicy policy = CardinalTraversalAdmissionPolicy.CardinalOnly)
    {
        Directory.CreateDirectory(_root);
        using var sector = new MemoryStream(); using var w = new BinaryWriter(sector);
        w.Write(0);
        for (var i = 0; i < 4096; i++) w.Write(0x180u);
        w.Write(1); w.Write(0xaa0004); w.Write(2); w.Write(new byte[48]);
        w.Write(new byte[12]); w.Write(new byte[24]);
        for (var i = 0; i < 128; i++) w.Write(tileBlocker && i == 2 ? 1u << 7 : 0u);
        // Visible blocker destination5,1, then two nonblocking rows at6,1.
        Record(w, 5, 1, 0, 3); Record(w, 6, 1, 1024, 3); Record(w, 6, 1, 1024, 3);
        for (var i = 0; i < uncertainRecords; i++) Record(w, 20+i%20, 20+i/20, null, 3);
        w.Write(3+uncertainRecords);
        using var module = DatArchive.Open(new MemoryStream(Dat((SectorPath, sector.ToArray()))));
        using var resource = DatArchive.Open(new MemoryStream(Dat(names ? [("art\\tile\\tilename.mes", Encoding.ASCII.GetBytes("{0}{aaa}{300}{aaa}"))] : [])));
        Snapshot = CardinalTraversalInspector.InspectSector(new(module, SectorPath, resource), new(_root),
            "MODULE", "RESOURCE", "PROTOTYPES", new(SectorPath, "BASE_SECTOR_1452_1290", 1452,1290,86570436012,92928,82560,64,64),
            names ? new(resource,"RESOURCE","art\\tile\\tilename.mes",TerrainPathProfile.CeNonWindowsMacOs) : null, 7, policy: policy);
        // All sources are disposed before any runtime tests query the snapshot.
    }
    internal CardinalTraversalSnapshot Snapshot { get; }
    private static void Record(BinaryWriter w, int x, int y, uint? flags, int type)
    {
        w.Write(119); Id(w,7001); Id(w,1); w.Write(type); w.Write((short)(flags is null ? 2 : 3));
        w.Write(3u | (flags is null ? 0u : 1u << 18)); w.Write(0u); w.Write(0u);
        w.Write(123); w.Write((byte)1); w.Write(unchecked((long)((ulong)(uint)(92928+x) | (ulong)(uint)(82560+y)<<32)));
        if (flags is uint f) w.Write(f);
    }
    private static void Id(BinaryWriter w, int aid) { w.Write((short)1); w.Write(new byte[6]); w.Write(aid); w.Write(new byte[12]); }
    private static byte[] Dat(params (string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        var offsets = new uint[entries.Length];
        for (var i=0; i<entries.Length; i++) { offsets[i]=(uint)stream.Position; w.Write(entries[i].Bytes); }
        var start=stream.Position; w.Write(entries.Length);
        for (var i=0; i<entries.Length; i++)
        {
            var name=Encoding.ASCII.GetBytes(entries[i].Name+"\0"); w.Write(name.Length); w.Write(name);
            w.Write(0u); w.Write(1u); w.Write((uint)entries[i].Bytes.Length); w.Write((uint)entries[i].Bytes.Length); w.Write(offsets[i]);
        }
        var length=stream.Position-start; w.Write(new byte[16]); w.Write("1TAD"u8); w.Write(0u); w.Write((uint)(length+28)); return stream.ToArray();
    }
    public void Dispose() => Directory.Delete(_root, true);
}
