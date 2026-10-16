using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;

namespace Arcanum.TerrainViewer.Tests;

// Independently authored FILE/DAT/MES/ART bytes. The layouts follow the public
// Data synthetic wire fixtures, not an encoder or renderer-produced oracle.
internal sealed class AreaOwnerFixture : IDisposable
{
    internal static readonly long[] Sectors = [86570436012,86570436013,86637544876,86637544877];
    internal readonly Dictionary<AreaInputKey, TrackedStream> Streams = new();
    internal AreaDataOwner Owner { get; }
    internal AreaOwnerFixture(AreaLoaderOptions? limits = null, bool seamMarkers = false, bool motionBodies = false)
    {
        var a = Record(63,63,-7,5); var b = Record(0,63,9,-13);
        // motionBodies: the canopy at cell6 must cover the actor witnesses in
        // cells 6 and 7 (tools/terrain-viewer/Godot/tools/area_motion_oracle.py).
        // Whole-core movement: the body at cell5 blocks; the canopy carries OF_NO_BLOCK so the actor can
        // stand and walk under it, as the moving-overlap witnesses require.
        var baseRecords = motionBodies ? new[] { Record(5,1,0,0), Record(6,1,-74,40,flags:0x400) } : new[] { a,a };
        var module = Dat(("maps/test/86570436012.sec",Sector(seamMarkers ? 4095 : -1,baseRecords)),
            ("maps/test/86570436013.sec",Sector(seamMarkers ? 4032 : -1,b)),
            ("maps/test/86637544876.sec",Sector(seamMarkers ? 63 : -1,Record(63,0,-7,5))),
            ("maps/test/86637544877.sec",Sector(seamMarkers ? 0 : -1,Record(0,0,9,-13))));
        (string Name, byte[] Bytes)[] entries = [
            ("tile.mes",Encoding.ASCII.GetBytes("{0}{stone}{100}{stone}{200}{stone}{300}{stone}{400}{end}")),
            ("facade.mes",Encoding.ASCII.GetBytes("{0}{fixture}")),
            ("scenery.mes",Encoding.ASCII.GetBytes("{0}{fixture.art}")),
            ("art/tile/stonebse0a.art",Art(80,40,0,0,false,seamMarkers)),
            ("art/scenery/fixture.art",Art(128,160,motionBodies ? 20 : -3,motionBodies ? 80 : 11,true))];
        var resource = Dat(entries);
        (AreaInputKey Key, byte[] Bytes)[] files = [
            (new(AreaSourceRole.Module),module), (new(AreaSourceRole.Resource1),resource),
            (new(AreaSourceRole.Prototype,7001),Prototype())];
        var inputs = files.Select(file => {
            var stream = new TrackedStream(file.Bytes); Streams.Add(file.Key,stream);
            return new AreaInput(file.Key,stream);
        }).ToArray();
        using var archive = DatArchive.Open(new MemoryStream(resource));
        var mappings = new[] { AreaMappingRole.Tile,AreaMappingRole.Facade,AreaMappingRole.Scenery }
            .Select((role,index) => {
                var entry=archive.Entries[index];
                return new AreaMappingBinding(role,new(new(AreaSourceRole.Resource1),index,1,
                    entry.StoredSize,entry.DeclaredOutputSize,entry.Offset,Hash(entry.RawNameBytes.ToArray())),Hash(entries[index].Bytes));
            });
        var plan = new AreaSourcePlan(1,5051,new(1452,1290),2,"maps/test",
            files.Select(file=>new AreaInputIdentity(file.Key,file.Bytes.Length,Hash(file.Bytes))),mappings);
        Owner = AreaDataOwner.Open(new(inputs),plan,limits ?? new());
    }
    internal sealed class TrackedStream : MemoryStream
    {
        internal TrackedStream(byte[] bytes) : base(bytes,false) { Bytes=bytes; }
        internal byte[] Bytes { get; }
        internal int DisposeCount { get; private set; }
        internal Action<long,int>? BeforeRead { get; set; }
        public override int Read(byte[] buffer,int offset,int count)
        { BeforeRead?.Invoke(Position,count); return base.Read(buffer,offset,count); }
        public override int Read(Span<byte> buffer)
        { BeforeRead?.Invoke(Position,buffer.Length); return base.Read(buffer); }
        protected override void Dispose(bool disposing)
        { if(disposing) DisposeCount++; base.Dispose(disposing); }
    }
    private static byte[] Id(short type,int number=0)
    {
        var bytes=new byte[24]; BinaryPrimitives.WriteInt16LittleEndian(bytes,type);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8),number); return bytes;
    }
    private static byte[] Record(int x,int y,int ox,int oy,int? flags=null)
    {
        using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream);
        writer.Write(119); writer.Write(Id(1,7001)); writer.Write(Id(0)); writer.Write(3);
        // Field mask word0: location 1, offsets 2/3 and, optionally, direct OBJ_F_FLAGS slot 18.
        writer.Write((short)(flags is null ? 3 : 4)); writer.Write(14 | (flags is null ? 0 : 1 << 18)); writer.Write(0); writer.Write(0);
        writer.Write((byte)1); writer.Write((long)x|((long)y<<32)); writer.Write(ox); writer.Write(oy);
        if (flags is { } value) writer.Write(value);
        return stream.ToArray();
    }
    /// <summary>Whole-core step evidence over this fixture's strict snapshot with public synthetic rule archives.
    /// A table without the 300 anchor leaves every class-0 tile Unknown.</summary>
    internal static Arcanum.Data.Traversal.AreaStepSnapshot Steps(AreaSnapshot area, byte[] module, long epoch, bool tileNamesResolved = true)
    {
        using var moduleArchive = DatArchive.Open(new MemoryStream(module));
        using var names = DatArchive.Open(new MemoryStream(Dat(("art/tile/tilename.mes", Encoding.ASCII.GetBytes(
            tileNamesResolved ? "{0}{grs}{100}{rck}{200}{flr}{300}{stn/s/n 4}" : "{0}{grs}{100}{rck}{200}{flr}")))));
        using var effects = DatArchive.Open(new MemoryStream(Dat(("rules/effect.mes", Encoding.ASCII.GetBytes("{50}{st +1}")))));
        return Arcanum.Data.Traversal.AreaStepInspector.Inspect(area, moduleArchive, names, effects, null, ReadOnlyMemory<byte>.Empty, epoch);
    }
    private static byte[] Prototype()
    {
        using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream);
        writer.Write(119); writer.Write(Id(-1)); writer.Write(Id(1,7001)); writer.Write(3);
        writer.Write(0); writer.Write(0); writer.Write(0);
        int[] optional=[1,5,6,7,15,16,17,30,31,34,35,37,40,41];
        for(var field=0;field<42;field++)
        {
            if(optional.Contains(field)) writer.Write((byte)0);
            else writer.Write(field switch { 0=>0x40000000,9=>0xFFFFFF,10=>255,11=>100,_=>0 });
        }
        return stream.ToArray();
    }
    private static byte[] Sector(int mirroredCell,params byte[][] records)
    {
        using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream);
        writer.Write(0);
        for(var i=0;i<4096;i++) writer.Write(i==mirroredCell ? 0xC1u : 0u);
        writer.Write(1); writer.Write(0xAA0003);
        writer.Write(0); writer.Write(new byte[12]); writer.Write(2); writer.Write(new byte[20]);
        foreach(var record in records) writer.Write(record);
        writer.Write(records.Length); return stream.ToArray();
    }
    private static byte[] Art(int width,int height,int hotX,int hotY,bool body,bool seamMarkers=false)
    {
        var bytes=new byte[1184+width*height];
        void I(int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at),value);
        I(0,1); I(8,8); I(12,1); I(32,1);
        // Palette entries: B,G,R,unused. Index zero is transparent.
        if(body) { bytes[136]=19; bytes[137]=37; bytes[138]=231; bytes[140]=71; bytes[141]=197; bytes[142]=18; }
        else { bytes[136]=200; bytes[137]=20; bytes[138]=10; }
        if(seamMarkers)
        {
            bytes[140]=30; bytes[141]=100; bytes[142]=240;
            bytes[144]=255; bytes[145]=0; bytes[146]=255; // cropped columns78/79 sentinel
        }
        I(1156,width); I(1160,height); I(1164,width*height); I(1168,hotX); I(1172,hotY);
        I(1176,777); I(1180,-999); // ART offsets must not become object offsets.
        for(var y=0;y<height;y++) for(var x=0;x<width;x++)
            bytes[1184+y*width+x]=seamMarkers ? x>=78 ? (byte)3 : x>=39 ? (byte)2 : (byte)1
                : body && (x==0||y==0||x==width-1||y==height-1) ? (byte)0
                : body && x>=width/2 ? (byte)2 : (byte)1;
        return bytes;
    }
    internal static byte[] Dat(params (string Name,byte[] Bytes)[] entries)
    {
        using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream);
        var offsets=new int[entries.Length];
        for(var i=0;i<entries.Length;i++) { offsets[i]=(int)stream.Position; writer.Write(entries[i].Bytes); }
        var start=stream.Position; writer.Write(entries.Length);
        for(var i=0;i<entries.Length;i++)
        {
            var name=Encoding.ASCII.GetBytes(entries[i].Name+"\0"); writer.Write(name.Length); writer.Write(name);
            writer.Write(0); writer.Write(1); writer.Write(entries[i].Bytes.Length); writer.Write(entries[i].Bytes.Length); writer.Write(offsets[i]);
        }
        var length=stream.Position-start; writer.Write(new byte[16]); writer.Write("1TAD"u8); writer.Write(0); writer.Write((int)length+28);
        return stream.ToArray();
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public void Dispose()=>Owner.Dispose();
}
