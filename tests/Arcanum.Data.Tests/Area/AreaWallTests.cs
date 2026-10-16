using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

// Synthetic wall objects (type 0) through a structural plan: literal packed wall
// selectors, synthetic structure/name tables and tiny single-group ART files.
[TestClass]
public sealed class AreaWallTests
{
    private const string Structure = "{0}{Aa10 Bb10 nul nul}{1}{Zz10 Bb10 nul nul /nowindows}";
    private const string Names = "{0}{Aa10}{1}{Bb10}";

    private static uint Wall(int number, int piece, int rotation, int variation = 0, int palette = 0, int flags = 0, uint damage = 0) =>
        (1u << 28) | ((uint)number << 20) | ((uint)piece << 14) | ((uint)rotation << 11) | ((uint)variation << 8) | ((uint)palette << 4) | (uint)flags | damage;

    [TestMethod]
    public void Wall_art_uses_interior_or_exterior_name_piece_damage_letter_and_variation()
    {
        foreach (var (raw, file) in new[]
        {
            (Wall(0, 0, 1), "art/wall/aa1bseu0.art"), // rotation 1: interior side
            (Wall(0, 3, 7, variation: 2), "art/wall/aa1bclu2.art"), // rotation 7: interior side
            (Wall(0, 34, 3, palette: 1, flags: 1), "art/wall/bb1p3lu0.art"), // rotation 3: exterior side, flipped
            (Wall(0, 1, 5), "art/wall/bb1lfcu0.art"), // rotation 5: exterior side
        })
        {
            using var f = Make(raw, file);
            var snapshot = f.Owner.LoadStrict();
            var wall = snapshot.Objects.Single(o => o.Key.Kind == AreaSourceKind.Static);
            Assert.AreEqual(0, wall.Type);
            Assert.IsTrue(wall.SchemaReady && wall.PlacementReady && wall.RequiredLayersReady);
            var visual = snapshot.Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static);
            Assert.AreEqual(raw, visual.RawArt); Assert.AreEqual(raw, visual.LookupArt);
            Assert.AreEqual((int)((raw >> 11) & 7), visual.NominalFacing);
            Assert.AreEqual(0, visual.SourceGroup); Assert.AreEqual(0, visual.PhysicalGroup); Assert.AreEqual(0, visual.Frame);
            Assert.AreEqual((raw & 1) != 0, visual.Mirrored);
            Assert.AreEqual((int)((raw >> 4) & 3), visual.RequestedPalette);
            Assert.AreEqual("strict-full-entry", visual.Policy);
            Assert.AreEqual("structural-file-effective-v1", snapshot.Profile);
        }
    }

    [TestMethod]
    public void Damaged_unknown_piece_and_missing_or_ambiguous_names_never_publish()
    {
        using (var damaged = Make(Wall(0, 0, 1, damage: 0x80), "art/wall/aa1bseu0.art")) Reject(damaged, AreaErrorCode.UnsupportedArtClassOrState);
        using (var piece = Make(Wall(0, 46, 1), "art/wall/aa1bseu0.art")) Reject(piece, AreaErrorCode.UnsupportedArtClassOrState);
        using (var row = Make(Wall(7, 0, 1), "art/wall/aa1bseu0.art")) Reject(row, AreaErrorCode.MissingSource);
        using (var missingName = Make(Wall(1, 0, 1), "art/wall/aa1bseu0.art")) Reject(missingName, AreaErrorCode.MissingSource);
        using (var ambiguous = Make(Wall(0, 0, 1), "art/wall/aa1bseu0.art", names: "{0}{Aa10}{1}{aa12}")) Reject(ambiguous, AreaErrorCode.AmbiguousSource);
        using (var file = Make(Wall(0, 0, 1), "art/wall/aa1bseu1.art")) Reject(file, AreaErrorCode.MissingSource);
        using (var heavy = Make(Wall(0, 0, 1, damage: 0x400), "art/wall/aa1bseu0.art")) Reject(heavy, AreaErrorCode.UnsupportedArtClassOrState);
        using (var flags = Make(Wall(0, 0, 1, flags: 2), "art/wall/aa1bseu0.art")) Reject(flags, AreaErrorCode.UnsupportedArtClassOrState);
        using (var oneToken = Make(Wall(0, 0, 1), "art/wall/aa1bseu0.art", structure: "{0}{Aa10}")) Reject(oneToken, AreaErrorCode.MalformedRecord);
        using (var shortSide = Make(Wall(0, 0, 1), "art/wall/aa1bseu0.art", structure: "{0}{Aa Bb10 nul nul}")) Reject(shortSide, AreaErrorCode.MalformedRecord);
        // Rows are positional in the source: a gap below key 1000 is refused, not looked up by key.
        using (var gap = Make(Wall(0, 0, 1), "art/wall/aa1bseu0.art", structure: "{0}{Aa10 Bb10 nul nul}{2}{Aa10 Bb10 nul nul}")) Reject(gap, AreaErrorCode.MalformedRecord);
    }

    [TestMethod]
    public void Eight_rotation_wall_art_is_never_read_as_group_zero()
    {
        var art = Art(); BinaryPrimitives.WriteInt32LittleEndian(art, 0); // header bit 0 clear: eight rotations
        using var f = Make(Wall(0, 0, 1), "art/wall/aa1bseu0.art", art: art);
        Reject(f, AreaErrorCode.UnsupportedPhysicalSelection);
    }

    [TestMethod]
    public void A_plan_binding_only_one_wall_table_refuses_walls()
    {
        foreach (var roles in new[] { new[] { AreaMappingRole.WallStructure }, new[] { AreaMappingRole.WallName } })
        {
            using var f = Make(Wall(0, 0, 1), "art/wall/aa1bseu0.art", extraRoles: roles);
            var report = f.Owner.Inspect();
            Assert.IsFalse(report.IsComplete);
            Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.UnsupportedVersionOrSchema), string.Join(',', report.Issues));
        }
    }

    [TestMethod]
    public void Walls_are_refused_unless_the_plan_binds_both_wall_tables()
    {
        using var f = Make(Wall(0, 0, 1), "art/wall/aa1bseu0.art", wallTables: false);
        var report = f.Owner.Inspect();
        Assert.IsFalse(report.IsComplete);
        Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.UnsupportedVersionOrSchema), string.Join(',', report.Issues));
        Assert.IsNull(f.Owner.PublishedSnapshot);
    }

    private static void Reject(Fixture f, AreaErrorCode code)
    {
        var report = f.Owner.Inspect(); Assert.IsFalse(report.IsComplete);
        Assert.IsTrue(report.Issues.Any(i => i.Code == code), string.Join(',', report.Issues));
        Assert.IsNull(f.Owner.PublishedSnapshot); Throws<AreaIncompleteException>(() => f.Owner.LoadStrict());
    }

    private static Fixture Make(uint raw, string file, string names = Names, bool wallTables = true, string structure = Structure, byte[]? art = null,
        AreaMappingRole[]? extraRoles = null)
    {
        var entries = new List<(string Name, byte[] Payload)>
        {
            ("tile.mes", Encoding.ASCII.GetBytes("{0}{stone}{100}{stone}{200}{stone}{300}{stone}{400}{end}")),
            ("facade.mes", Encoding.ASCII.GetBytes("{0}{fixture}")), ("scenery.mes", Encoding.ASCII.GetBytes("{0}{fixture.art}")),
            ("structure.mes", Encoding.ASCII.GetBytes(structure)), ("wallname.mes", Encoding.ASCII.GetBytes(names)),
            ("art/tile/stonebse0a.art", Art()), (file, art ?? Art()),
        };
        if (extraRoles is { Length: 1 } && extraRoles[0] == AreaMappingRole.WallName)
            (entries[3], entries[4]) = (entries[4], entries[3]); // the single bound table must sit at mapping index 3
        AreaMappingRole[] roles = extraRoles is not null ? [AreaMappingRole.Tile, AreaMappingRole.Facade, AreaMappingRole.Scenery, .. extraRoles]
            : wallTables ? [AreaMappingRole.Tile, AreaMappingRole.Facade, AreaMappingRole.Scenery, AreaMappingRole.WallStructure, AreaMappingRole.WallName]
            : [AreaMappingRole.Tile, AreaMappingRole.Facade, AreaMappingRole.Scenery];
        return new([(67108865, [WallRecord(Id(0))])], prototype: WallPrototype(raw), resourceEntries: entries.ToArray(), mappingRoles: roles);
    }

    // Literal type-0 layout: version, parent, own ID, type, three mask words, then the
    // 36 common and 5 wall fields; arrays and pointers carry a one-byte absent marker.
    private static byte[] WallPrototype(uint raw)
    {
        using var s = new MemoryStream(); I32(s, 119); s.Write(Id(-1)); s.Write(Id(1, 7001)); I32(s, 0);
        I32(s, 0); I32(s, 0); I32(s, 0);
        int[] optional = [1, 5, 6, 7, 15, 16, 17, 30, 31, 34, 35, 39, 40];
        for (var field = 0; field < 41; field++)
        {
            if (optional.Contains(field)) s.WriteByte(0);
            else I32(s, field == 0 ? unchecked((int)raw) : field == 11 ? 100 : 0);
        }
        return s.ToArray();
    }

    private static byte[] WallRecord(byte[] id)
    {
        using var s = new MemoryStream(); I32(s, 119); s.Write(Id(1, 7001)); s.Write(id); I32(s, 0);
        s.Write(new byte[] { 1, 0 }); I32(s, 2); I32(s, 0); I32(s, 0); s.WriteByte(1); I64(s, 0); return s.ToArray();
    }
}
