using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer.Tests;

// Independently authored public wire fixtures. All three domains run real
// DAT/ART/sector/static-chain decoders in ONE production load operation.
internal sealed class SyntheticWorldSourceFixture : IDisposable
{
    internal const string SectorPath = "maps\\fixture\\world.sec";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "world-wire-" + Guid.NewGuid().ToString("N"));
    internal string ModulePath { get; }
    internal string ResourcePath { get; }
    internal string ActorPath { get; }
    internal string PrototypesPath { get; }
    internal bool Human { get; }
    internal bool AuthenticateHumanMetadata { get; }
    internal Admission Identity { get; }
    internal SyntheticWorldSourceFixture(bool corruptActor = false, bool corruptStatic = false, bool human = false,
        Func<byte[],byte[]>? humanIdleTransform = null, bool moveHumanIdle = false, bool authenticateHumanMetadata = false)
    {
        Human = human;
        AuthenticateHumanMetadata = authenticateHumanMetadata;
        Directory.CreateDirectory(_root);
        ModulePath = Path.Combine(_root, "module.dat"); ResourcePath = Path.Combine(_root, "terrain.dat");
        ActorPath = Path.Combine(_root, "actor.dat"); PrototypesPath = Path.Combine(_root, "prototypes");
        Directory.CreateDirectory(PrototypesPath);
        var module = Enumerable.Range(0, 644).Select(i => ($"f{i}", Array.Empty<byte>())).ToArray();
        module[0] = (SectorPath, Sector());
        File.WriteAllBytes(ModulePath, Dat(module));
        var terrain = Enumerable.Range(0, 4755).Select(i => ($"f{i}", Array.Empty<byte>())).ToArray();
        terrain[0] = ("art\\tile\\tilename.mes", Encoding.ASCII.GetBytes("{0}{aaa}{100}{bbb}{200}{ccc}{300}{ddd}{400}{000000}"));
        terrain[1] = ("art\\facade\\facadename.mes", Encoding.ASCII.GetBytes("{0}{zero}"));
        terrain[2] = ("art\\scenery\\scenery.mes", Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(1,5).Select(i => $"{{{1000*i+1}}}{{s{i}.art}}"))));
        for (var i = 1; i <= 5; i++) terrain[i+2] = ($"art\\scenery\\s{i}.art", corruptStatic && i == 5 ? [1] : Art(1,1,8,0));
        // These ordinals deliberately coincide with the actor role. No union
        // deduplication by ordinal may remove either terrain frame.
        terrain[4753] = ("ART/TILE/AAABSE0A.ART", Art(1,1,8,0));
        terrain[4754] = ("ART/FACADE/ZERO.ART", Art(1,1,8,0));
        File.WriteAllBytes(ResourcePath, Dat(terrain));
        var actor = Enumerable.Range(0, 4755).Select(i => ($"f{i}", Array.Empty<byte>())).ToArray();
        actor[human ? 2667 : 4753] = ("idle.art", corruptActor ? [1] : Art(8,human ? 1 : 15,human ? 6 : 8,human ? 0 : 1));
        actor[human ? 2668 : 4754] = ("walk.art", Art(8,human ? 10 : 8,human ? 17 : 15,human ? 0 : 1));
        if (human && humanIdleTransform is not null) actor[2667] = ("idle.art", humanIdleTransform(actor[2667].Item2));
        if (human && moveHumanIdle) { actor[2670] = actor[2667]; actor[2667] = ("empty",Array.Empty<byte>()); }
        File.WriteAllBytes(ActorPath, Dat(actor));
        Identity = new Admission(this);
    }
    internal TerrainViewerArguments Arguments => TerrainViewerArguments.Parse(["--mode", "world-diagnostic", "--module", ModulePath,
        "--resource", ResourcePath, "--actor-resource", ActorPath, "--prototypes", PrototypesPath, "--source-id", "public-fixture"]);
    internal TerrainLoadCoordinator.ProductionTerrainLoadOperation Operation(TerrainLoadBudget? budget = null, Action<TerrainLoadCheckpoint>? checkpoint = null)
        => new(Identity, budget, checkpoint);
    internal void AssertDisposed(int expectedArchives)
    {
        Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expectedArchives, Identity.Streams.Count);
        foreach (var stream in Identity.Streams) Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(1, stream.DisposeCount);
    }
    internal sealed class Admission : TerrainLoadAdmission
    {
        private readonly SyntheticWorldSourceFixture _fixture;
        internal readonly List<TrackedStream> Streams = new();
        private readonly InputFingerprint _module, _resource, _actor;
        internal Admission(SyntheticWorldSourceFixture fixture)
        {
            _fixture = fixture; _module = Fingerprint(fixture.ModulePath); _resource = Fingerprint(fixture.ResourcePath); _actor = Fingerprint(fixture.ActorPath);
        }
        internal override InputFingerprint Module => _module;
        internal override InputFingerprint Resource => _resource;
        internal override InputFingerprint ActorResource => _actor;
        // Only the admitted identity/metadata oracle is simulated. Parsing,
        // source selection, checked retention, planning and disposal are real.
        internal override ActorDiagnosticProfile ActorProfile(TerrainViewerMode mode) => _fixture.Human
            ? ActorDiagnosticProfile.HumanFixed : ActorDiagnosticProfile.MonsterRegression;
        internal override bool AuthenticateOriginalActorMetadata => _fixture.AuthenticateHumanMetadata;
        internal override InputFingerprint Prototypes => new(0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        internal override TerrainSectorSelector Selector => new(SectorPath, "fixture", 0,0,1,0,0,64,64);
        internal override bool AcceptCensus(int tiles, int facades, int recovered, int lookups) => (tiles,facades,recovered,lookups) == (3033,1063,0,0);
        internal override StaticObjectInspection InspectStatics(TerrainViewerArguments args, DatArchive module, DatArchive resource)
            => StaticObjectLoader.InspectSelectedResources(new(module, SectorPath, resource), new(_fixture.PrototypesPath));
        internal override DatArchive OpenArchive(string path)
        {
            var stream = new TrackedStream(File.ReadAllBytes(path)); Streams.Add(stream); return DatArchive.Open(stream);
        }
    }
    internal sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes, false)
    {
        internal int DisposeCount { get; private set; }
        protected override void Dispose(bool disposing) { if (disposing) DisposeCount++; base.Dispose(disposing); }
    }
    private static InputFingerprint Fingerprint(string path)
    {
        var bytes = File.ReadAllBytes(path); return new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
    private static byte[] Sector()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(0); for (var i = 0; i < 4096; i++) writer.Write(i < 3033 ? 0x000001c0u : 0xb0000000u);
        writer.Write(1); writer.Write(0xaa0000);
        for (var i = 0; i < 317; i++)
        {
            writer.Write(119); ObjectId(writer, 7000+i); ObjectId(writer, 1); writer.Write(3);
            writer.Write((short)5); writer.Write(0x0004000fu); writer.Write(0); writer.Write(0);
            writer.Write(unchecked((int)(0x40080000u + ((uint)(i%5+1)<<6))));
            writer.Write((byte)1); writer.Write((long)i); writer.Write(i); writer.Write(-i); writer.Write(0u);
        }
        writer.Write(317); return stream.ToArray();
    }
    private static void ObjectId(BinaryWriter writer, int id) { writer.Write((short)1); writer.Write(new byte[6]); writer.Write(id); writer.Write(new byte[12]); }
    private static byte[] Art(int groups, int frames, int fps, int palette)
    {
        var total = groups*frames; var bytes = new byte[132+1024+total*29];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, groups == 1 ? 1u : 0u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),fps); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8),8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12+palette*4),1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32),frames);
        bytes[136]=255; bytes[137]=127; bytes[138]=63;
        for (var i=0; i<total; i++)
        {
            var p=1156+i*28; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(p),1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(p+4),1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(p+8),1);
            bytes[1156+total*28+i]=1;
        }
        return bytes;
    }
    private static byte[] Dat((string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        var offsets = new uint[entries.Length];
        for (var i=0; i<entries.Length; i++) { offsets[i]=(uint)stream.Position; writer.Write(entries[i].Bytes); }
        var start=stream.Position; writer.Write(entries.Length);
        for (var i=0; i<entries.Length; i++)
        {
            var name=Encoding.ASCII.GetBytes(entries[i].Name+"\0"); writer.Write(name.Length); writer.Write(name);
            writer.Write(0u); writer.Write(1u); writer.Write((uint)entries[i].Bytes.Length); writer.Write((uint)entries[i].Bytes.Length); writer.Write(offsets[i]);
        }
        var indexLength=stream.Position-start; writer.Write(new byte[16]); writer.Write("1TAD"u8); writer.Write(0u); writer.Write((uint)(indexLength+28)); return stream.ToArray();
    }
    public void Dispose() => Directory.Delete(_root, true); // this fixture's own unique synthetic directory only
}
