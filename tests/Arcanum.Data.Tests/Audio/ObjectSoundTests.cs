using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Data.Objects;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Tests.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Audio;

// ARC-WORLD-041: sound fields of synthetic v119 prototypes and Area objects, and the tile name sound classes of synthetic
// tables. Every value is invented; each slot gets a distinct value so a wrong slot shows up as a wrong value.
[TestClass]
public sealed class ObjectSoundTests
{
    private const string Source = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly StaticObjectLoaderOptions Limits = new() { MaxPrototypeBytes = 65536, MaxArrayElements = 4096, MaxStringBytes = 4096 };

    // Common slots 0 current art, 18 flags, 29 material, 32 sound; item 38 weight, 47 unknown description; ammunition 60 quantity.
    private static Dictionary<int, object> Values(int type) => new()
    {
        [0] = 0x20300000, [18] = 0x2, [29] = 5, [32] = 7300, [38] = 2100, [39] = 14, [45] = 15, [47] = 9056, [60] = 30, [36] = 11, [46] = 12, [59] = 13,
    };

    private static Func<int, PrototypeFile> Files(params (int Number, int Type)[] prototypes)
    {
        var files = prototypes.ToDictionary(p => p.Number, p => new PrototypeFile(PrototypeFileStatus.Found, PremadeHeroTests.Prototype(p.Number, p.Type, Values(p.Type))));
        return number => files.TryGetValue(number, out var file) ? file : new(PrototypeFileStatus.Missing, null);
    }

    [TestMethod]
    public void A_prototype_gives_its_sound_material_art_and_flags_and_items_their_weight()
    {
        var files = Files((1001, 16), (1002, 5), (1003, 6), (1004, 2), (1005, 17));
        var npc = ObjectSoundReader.ReadPrototype(files, 1001).Fields!;
        Assert.AreEqual((16, 1001, 7300, 5, 0x20300000u, 0x2u), (npc.Type, npc.Prototype, npc.SoundEffect, npc.Material, npc.CurrentArt, npc.Flags));
        Assert.IsNull(npc.Item, "a critter has no item fields");
        var weapon = ObjectSoundReader.ReadPrototype(files, 1002).Fields!.Item!;
        Assert.AreEqual((2100, 9056, (int?)null, 14, 15), (weapon.Weight, weapon.UnknownDescription, weapon.AmmoQuantity, weapon.MagicWeightAdjustment, weapon.MagickTechComplexity));
        Assert.AreEqual(30, ObjectSoundReader.ReadPrototype(files, 1003).Fields!.Item!.AmmoQuantity, "ammunition has its quantity");
        Assert.IsNull(ObjectSoundReader.ReadPrototype(files, 1004).Fields!.Item);
        Assert.AreEqual((0u, 11u), (ObjectSoundReader.ReadPrototype(files, 1004).Fields!.SceneryFlags, ObjectSoundReader.ReadPrototype(Files((1006, 3)), 1006).Fields!.SceneryFlags),
            "only scenery reads its flags (slot 36)");
        Assert.AreEqual(7300, ObjectSoundReader.ReadPrototype(files, 1005).Fields!.SoundEffect, "a trap reads its common fields");
        Assert.AreEqual(ObjectSoundFailure.PrototypeMissing, ObjectSoundReader.ReadPrototype(files, 1009).Failure);
        Assert.AreEqual(ObjectSoundFailure.PrototypeMissing, ObjectSoundReader.ReadPrototype(files, -1).Failure);
        Assert.AreEqual(ObjectSoundFailure.PrototypeAmbiguous, ObjectSoundReader.ReadPrototype(_ => new(PrototypeFileStatus.Ambiguous, null), 7).Failure);
        Assert.AreEqual(ObjectSoundFailure.PrototypeInvalid, ObjectSoundReader.ReadPrototype(_ => new(PrototypeFileStatus.Found, [1, 2, 3]), 7).Failure);
    }

    [TestMethod]
    public void An_area_object_reads_its_instance_fields_over_its_prototype_and_refuses_a_missing_one()
    {
        var row = Row(1002, 5, Instance(32, 8100), Instance(38, 1500));
        var fields = ObjectSoundReader.Read(row).Fields!;
        Assert.AreEqual((8100, 1500), (fields.SoundEffect, fields.Item!.Weight), "the instance's own values win");
        var missing = row with { EffectiveFields = row.EffectiveFields.Where(f => f.Slot != 29).ToArray() };
        Assert.AreEqual((ObjectSoundFailure.FieldUnresolved, 29), (ObjectSoundReader.Read(missing).Failure, ObjectSoundReader.Read(missing).Detail));
        var absent = row with { EffectiveFields = row.EffectiveFields.Select(f => f.Slot == 32 ? f with { Presence = AreaPresence.Absent } : f).ToArray() };
        Assert.AreEqual(32, ObjectSoundReader.Read(absent).Detail);
        var wide = row with { EffectiveFields = row.EffectiveFields.Select(f => f.Slot == 0 ? f with { Number = 1L << 33 } : f).ToArray() };
        Assert.AreEqual(0, ObjectSoundReader.Read(wide).Detail, "a value beyond 32 bits is refused");
        var unsigned = row with { EffectiveFields = row.EffectiveFields.Select(f => f.Slot == 18 ? f with { Number = 0xFFFFFFFFL } : f).ToArray() };
        Assert.AreEqual(0xFFFFFFFFu, ObjectSoundReader.Read(unsigned).Fields!.Flags);
        Assert.AreEqual(ObjectSoundFailure.TypeUnsupported, ObjectSoundReader.Read(row with { Type = 18 }).Failure);
    }

    [TestMethod]
    public void Tile_names_give_each_category_its_sound_numbers_by_position()
    {
        var table = Table(new()
        {
            [0] = "grs/n 0", [1] = "snd/sn 1", [5] = "stn/b3", [100] = "wtr/s  4", [101] = "abc7", [102] = "xyz", [200] = "wod/p 5 junk", [300] = "flr 3",
            [301] = "cav/", [399] = "-2x/n -2", [400] = "ign/n 9",
        });
        CollectionAssert.AreEqual(new[] { 0, 1, 0 }, table.Category(0).ToArray(), "flags up to the first space; a letter run without one reads 0");
        CollectionAssert.AreEqual(new[] { 4, 7, 0 }, table.Category(1).ToArray(), "a bare name reads the number after its first three characters");
        CollectionAssert.AreEqual(new[] { 5 }, table.Category(2).ToArray());
        CollectionAssert.AreEqual(new[] { 3, 0, -2 }, table.Category(3).ToArray(), "records from 400 on are not read");

        uint Tile(bool outdoor, bool flippable, int position) => (uint)(position << 22) | (outdoor ? 0x100u : 0) | (flippable ? 0x80u : 0);
        Assert.AreEqual(1, table.SoundClass(Tile(true, true, 1)));
        Assert.AreEqual(7, table.SoundClass(Tile(true, false, 1)));
        Assert.AreEqual(5, table.SoundClass(Tile(false, true, 0)));
        Assert.AreEqual(-2, table.SoundClass(Tile(false, false, 2)));
        Assert.IsNull(table.SoundClass(Tile(false, true, 1)), "a position beyond its category");
        // ARC-DATA-043: a facade in the tile slot (type 11): outdoor bit 25, flippable bit 26, position bits 11..16.
        uint Facade(bool outdoor, bool flippable, int position) => 0xB0000000u | (uint)(position << 11) | (outdoor ? 1u << 25 : 0) | (flippable ? 1u << 26 : 0);
        Assert.AreEqual(1, table.SoundClass(Facade(true, true, 1)));
        Assert.AreEqual(7, table.SoundClass(Facade(true, false, 1)));
        Assert.AreEqual(-2, table.SoundClass(Facade(false, false, 2)));
        Assert.AreEqual(5, table.SoundClass(Facade(false, true, 0)));
        Assert.AreEqual(1, table.SoundClass(Facade(true, true, 1) | 0x180), "a facade ignores the tile bits 7 and 8");
        Assert.IsNull(table.SoundClass(Facade(true, true, 3)), "a facade position beyond its category");
        // Any other art type reads as indoor, not flippable, position 0 (the original's helpers give 0 for every field).
        Assert.AreEqual(3, table.SoundClass(Tile(true, true, 2) | 0x10000000), "a wall art id");
        var sparse = Table(new() { [0] = "aaa1", [100] = "bbb2", [150] = "ccc3", [200] = "ddd4", [300] = "eee5", [350] = "fff0" });
        Assert.AreEqual(3, sparse.SoundClass(Tile(true, false, 1)), "record 150 is position 1: gaps do not count");
        Assert.AreEqual(0, sparse.SoundClass(Tile(false, false, 1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sparse.Category(4));
    }

    [TestMethod]
    public void Tile_name_flags_and_the_ground_facts_of_the_sky_and_nature_mage_backgrounds()
    {
        // ARC-MAGIC-028: letters after the '/' up to the first space add b 1, s 2, f 5, i 8, n 0x10, p 0x20 (0x4EB2E0); the nature
        // flag is 0x10 (0x4EBBA0) and outdoor is bit 8 of a tile and bit 25 of a facade (0x5038C0).
        var table = Table(new()
        {
            [0] = "grs/n 0", [1] = "snd/sn 1", [5] = "stn/b3", [6] = "all/bsfinp 2", [100] = "wtr/s  4", [101] = "abc7", [200] = "wod/p 5 junk", [300] = "flr 3",
            [301] = "cav/", [302] = "ntr/n -2",
        });
        uint Tile(bool outdoor, bool flippable, int position) => (uint)(position << 22) | (outdoor ? 0x100u : 0) | (flippable ? 0x80u : 0);
        uint Facade(bool outdoor, bool flippable, int position) => 0xB0000000u | (uint)(position << 11) | (outdoor ? 1u << 25 : 0) | (flippable ? 1u << 26 : 0);
        Assert.AreEqual(0x10, table.NameFlags(Tile(true, true, 0)));
        Assert.AreEqual(0x12, table.NameFlags(Tile(true, true, 1)));
        Assert.AreEqual(1, table.NameFlags(Tile(true, true, 2)), "a letter run: b, then the digit adds nothing");
        Assert.AreEqual(0x3F, table.NameFlags(Tile(true, true, 3)), "b 1 | s 2 | f 5 | i 8 | n 0x10 | p 0x20");
        Assert.AreEqual(2, table.NameFlags(Tile(true, false, 0)));
        Assert.AreEqual(0, table.NameFlags(Tile(true, false, 1)), "a bare name has no flags");
        Assert.AreEqual(0x20, table.NameFlags(Tile(false, true, 0)));
        Assert.AreEqual(0, table.NameFlags(Tile(false, false, 1)), "a slash with no letters");
        Assert.AreEqual(0x10, table.NameFlags(Tile(false, false, 2)));
        Assert.IsNull(table.NameFlags(Tile(false, true, 1)), "a position beyond its category");

        Assert.AreEqual((true, true), table.MageGround(Tile(true, true, 0)), "outdoor tile with the nature flag");
        Assert.AreEqual((true, false), table.MageGround(Tile(true, true, 2)), "outdoor, no nature flag");
        Assert.AreEqual((false, true), table.MageGround(Tile(false, false, 2)), "indoor tile with the nature flag");
        Assert.AreEqual((false, false), table.MageGround(Tile(false, true, 0)));
        Assert.AreEqual((false, false), table.MageGround(Tile(false, true, 1)), "beyond its category: no flags");
        Assert.AreEqual((true, true), table.MageGround(Facade(true, true, 0)), "a facade is outdoor by bit 25");
        Assert.AreEqual((false, false), table.MageGround(Facade(false, true, 0)));
        Assert.AreEqual((false, false), table.MageGround(Tile(true, true, 0) | 0x10000000), "any other art type is indoor, position 0, category 3");
        Assert.AreEqual((false, false), table.MageGround(null), "no sector resident: the original reads no tile");
        Assert.AreEqual((false, false), table.MageGround(uint.MaxValue), "art id -1");
        Assert.IsTrue(TileSoundTable.IsOutdoor(Tile(true, false, 0)) && !TileSoundTable.IsOutdoor(Tile(false, false, 0)));
        Assert.IsFalse(TileSoundTable.IsOutdoor(Facade(false, false, 0) | 0x100), "a facade ignores bit 8");
    }

    [TestMethod]
    public void A_tile_name_table_without_a_category_start_or_with_a_short_bare_name_is_refused()
    {
        var full = new Dictionary<int, string> { [0] = "grs", [100] = "dirt", [200] = "wod", [300] = "flr" };
        Assert.AreEqual(TileSoundTableStatus.Ready, TileSoundTable.Parse(full).Status);
        foreach (var start in new[] { 0, 100, 200, 300 })
            Assert.AreEqual(TileSoundTableStatus.Invalid, TileSoundTable.Parse(full.Where(p => p.Key != start).ToDictionary()).Status, $"{start}");
        var shortName = new Dictionary<int, string>(full) { [101] = "ab" };
        Assert.AreEqual((TileSoundTableStatus.Invalid, 101), (TileSoundTable.Parse(shortName).Status, TileSoundTable.Parse(shortName).Record));
        Assert.AreEqual(TileSoundTableStatus.Invalid, TileSoundTable.Parse(new Dictionary<int, string>(full) { [5] = "gr\0s/n 1" }).Status);
    }

    [TestMethod]
    public void The_tile_name_table_is_read_from_the_base_sources_as_at_game_start()
    {
        var root = Path.Combine(Path.GetTempPath(), "arc-tilesound-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "modules", "Mod"));
            File.WriteAllBytes(Path.Combine(root, "tig.dat"), Dat());
            ContentStack Stack()
            {
                var discovery = ContentMountPlan.Discover(root, "Mod");
                Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
                return ContentStack.Open(discovery.Plan!).Stack!;
            }
            void Write(string relative, string text)
            {
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, Encoding.ASCII.GetBytes(text));
            }
            using (var stack = Stack()) Assert.AreEqual(TileSoundTableStatus.MissingFile, TileSoundTable.Open(stack).Status);
            Write("data/art/tile/tilename.mes", "{0}{grs/n 3}\r\n{100}{drt}\r\n{200}{wod/ 5}\r\n{300}{flr/ 3}\r\n");
            Write("modules/Mod/art/tile/tilename.mes", "{0}{grs/n 4}\r\n{100}{drt}\r\n{200}{wod/ 5}\r\n{300}{flr/ 3}\r\n");
            using (var stack = Stack()) Assert.AreEqual(3, TileSoundTable.Open(stack).Table!.Category(0)[0], "the module's copy is never read");
            Write("data/art/tile/tilename.mes", "{0}{grs");
            using (var stack = Stack()) Assert.AreEqual(TileSoundTableStatus.Malformed, TileSoundTable.Open(stack).Status);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static TileSoundTable Table(Dictionary<int, string> records)
    {
        var result = TileSoundTable.Parse(records);
        Assert.AreEqual(TileSoundTableStatus.Ready, result.Status);
        return result.Table!;
    }

    private static AreaObjectRecord Row(int prototype, int type, params AreaFieldValue[] instance)
    {
        var record = ObjectWireReader.Read(PremadeHeroTests.Prototype(prototype, type, Values(type)), Limits, prototype: true);
        var effective = record.Fields.Select(p => instance.FirstOrDefault(f => f.Slot == p.Key) ?? new AreaFieldValue(p.Key,
            (AreaPresence)((int)p.Value.Presence + 1), AreaValueOrigin.Prototype, p.Value.Number,
            p.Value.Elements.Select(e => new AreaSparseElement(e.Key, e.Words)).ToArray(), p.Value.Fingerprint)).ToArray();
        return new(new(Source, 7, AreaSourceKind.Mobile, null, 1, 1000L, Source, 300), 119, type, prototype, Source, Source, Source, Source,
            AreaScope.Core, null, 10, 20, AreaDisposition.BodyEligibleConditionally, instance, effective, true, true, true);
    }

    private static AreaFieldValue Instance(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
}
