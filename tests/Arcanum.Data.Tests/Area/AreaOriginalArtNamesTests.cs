using System.Buffers.Binary;
using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

/// <summary>ARC-DATA-023 on synthetic fixtures: whole-map plans that read ART as the original
/// does name critter art with the original's rules and missing-art repair, name damaged walls
/// and tile-art bodies, and draw <c>art/badart.art</c> for a name no archive holds
/// (docs/research/whole-map-art-gaps.md).</summary>
[TestClass]
public sealed class AreaOriginalArtNamesTests
{
    private const long Start = 67108865;
    private const uint Monster = 12u << 28, Unique = 13u << 28, Critter = 2u << 28;

    [TestMethod]
    public void A_concealed_monster_is_named_with_the_conceal_letter()
    {
        // Monster 1 ("lz1"), animation 5 (conceal fidget), no weapon.
        var snapshot = Complete(Monster | (1u << 23) | (5u << 6), [("art/monster/lz1/lz1uwxaf.art", Art())], MissingDat(1, new uint[CritterMasks + 4]));
        Assert.AreEqual(Named("art/monster/lz1/lz1uwxaf.art"), Visual(snapshot).Name);
    }

    [TestMethod]
    public void A_unique_npc_without_standing_art_stands_unarmed_as_the_missing_art_table_says()
    {
        // Unique NPC 1 ("dgn") stands with no weapon; its mask marks stand-without-weapon (bit 3)
        // missing, so the unarmed file is drawn although the no-weapon file exists too (a
        // rebuilt table would keep the no-weapon file: the table itself is read).
        var masks = new uint[CritterMasks + 2 + 2]; masks[CritterMasks + 2 + 1] = 8;
        var snapshot = Complete(Unique | (1u << 20), [("art/unique_npc/dgn/dgnxaa.art", Art()), ("art/unique_npc/dgn/dgnxba.art", Art())], MissingDat(2, masks));
        Assert.AreEqual(Named("art/unique_npc/dgn/dgnxba.art"), Visual(snapshot).Name);
    }

    [TestMethod]
    public void A_stale_or_absent_missing_art_table_is_rebuilt_from_the_archives()
    {
        var art = new (string, byte[])[] { ("art/unique_npc/dgn/dgnxba.art", Art()) };
        // Header 7 does not match the one critter file: the masks are rebuilt (all zero here would keep XAa).
        Assert.AreEqual(Named("art/unique_npc/dgn/dgnxba.art"), Visual(Complete(Unique | (1u << 20), art, MissingDat(7, new uint[CritterMasks + 4]))).Name);
        // A current header but too few masks: rebuilt the same way.
        Assert.AreEqual(Named("art/unique_npc/dgn/dgnxba.art"), Visual(Complete(Unique | (1u << 20), art, MissingDat(1, new uint[CritterMasks + 3]))).Name);
        // No missing-art binding at all: rebuilt the same way.
        Assert.AreEqual(Named("art/unique_npc/dgn/dgnxba.art"), Visual(Complete(Unique | (1u << 20), art, missing: null)).Name);
    }

    [TestMethod]
    public void A_current_table_is_used_as_it_is()
    {
        // A current header with an all-clear mask: no repair, so the absent standing file
        // falls back to bad art rather than being repaired.
        var snapshot = Complete(Unique | (1u << 20), [("art/unique_npc/dgn/dgnxba.art", Art()), ("art/badart.art", Art())], MissingDat(1, new uint[CritterMasks + 4]));
        var visual = Visual(snapshot);
        Assert.AreEqual(Named("art/badart.art"), visual.Name);
        Assert.AreEqual("original-badart-v1", visual.Visual.Policy);
    }

    [TestMethod]
    public void Critter_and_monster_masks_are_found_by_their_original_index()
    {
        // A halfling (body 2) male (gender 1) is critter mask 1 + 2 × 2 = 5; stand-without-weapon is marked missing.
        var masks = new uint[CritterMasks + 4]; masks[5] = 8;
        (string, byte[])[] halfling = [("art/critter/GHM/GHMUWXAa.art", Art()), ("art/critter/GHM/GHMUWXBa.art", Art())];
        Assert.AreEqual(Named("art/critter/GHM/GHMUWXBa.art"), Visual(Complete(Critter | (1u << 27) | (2u << 24), halfling, MissingDat(2, masks))).Name);
        // Monster 1 is mask 10 + 1; its stunned file (bit 2) is marked missing, so stunned becomes stand with no weapon.
        masks = new uint[CritterMasks + 4]; masks[CritterMasks + 1] = 4;
        (string, byte[])[] lizard = [("art/monster/lz1/lz1uwxaa.art", Art()), ("art/monster/lz1/lz1uwxbx.art", Art())];
        Assert.AreEqual(Named("art/monster/lz1/lz1uwxaa.art"), Visual(Complete(Monster | (1u << 23) | (23u << 6), lizard, MissingDat(2, masks))).Name);
        // Without the mark the stunned name is unarmed without a shield.
        Assert.AreEqual(Named("art/monster/lz1/lz1uwxbx.art"), Visual(Complete(Monster | (1u << 23) | (1u << 19) | (23u << 6), lizard, MissingDat(2, new uint[CritterMasks + 4]))).Name);
    }

    [TestMethod]
    public void An_exploding_critter_is_named_without_armour_gender_shield_or_weapon()
    {
        const uint explode = Critter | (2u << 20) | (1u << 19) | 3u | (24u << 6);
        Assert.AreEqual(Named("art/critter/HMX/HMXXXXAy.art"), Visual(Complete(explode, [("art/critter/HMX/HMXXXXAy.art", Art())], MissingDat(1, new uint[CritterMasks + 4]))).Name);
    }

    [TestMethod]
    public void Only_files_one_folder_below_the_critter_roots_count_for_the_header()
    {
        Assert.IsTrue(AreaDataOwner.CritterFile("art/monster/lz1/lz1uwxaa.art"));
        Assert.IsTrue(AreaDataOwner.CritterFile("art/unique_npc/dgn/dgnxba.art"));
        Assert.IsFalse(AreaDataOwner.CritterFile("art/critter/hmfuwxaa.art"));
        Assert.IsFalse(AreaDataOwner.CritterFile("art/critter/hmf/old/hmfuwxaa.art"));
        Assert.IsFalse(AreaDataOwner.CritterFile("art/critter/hmf/readme.txt"));
        Assert.IsFalse(AreaDataOwner.CritterFile("art/scenery/hmf/hmfuwxaa.art"));
    }

    [TestMethod]
    public void Running_drops_weapon_and_shield_and_walking_drops_the_shield()
    {
        // Human male (body 0, gender 1), armour 2 (LA), shield, weapon 3 (sword).
        const uint human = Critter | (1u << 27) | (2u << 20) | (1u << 19) | 3u;
        Assert.AreEqual(Named("art/critter/HMM/HMMLAXAg.art"), Visual(Complete(human | (6u << 6), [("art/critter/HMM/HMMLAXAg.art", Art())], MissingDat(1, new uint[CritterMasks + 4]))).Name);
        Assert.AreEqual(Named("art/critter/HMM/HMMLAXDb.art"), Visual(Complete(human | (1u << 6), [("art/critter/HMM/HMMLAXDb.art", Art())], MissingDat(1, new uint[CritterMasks + 4]))).Name);
        // Plate armour (4) draws an elf female as a human male with the X gender code.
        const uint plateElf = Critter | (4u << 24) | (4u << 20);
        Assert.AreEqual(Named("art/critter/HMX/HMXPMXAa.art"), Visual(Complete(plateElf, [("art/critter/HMX/HMXPMXAa.art", Art())], MissingDat(1, new uint[CritterMasks + 4]))).Name);
    }

    [TestMethod]
    public void Critter_art_outside_the_original_ranges_is_refused()
    {
        foreach (var aid in new[] { Unique | (1u << 20) | (25u << 6), Critter | (9u << 20), Critter | (5u << 24) })
        {
            using var f = Owner(aid, [("art/badart.art", Art())], MissingDat(0, new uint[CritterMasks + 4]));
            Assert.IsTrue(f.Owner.Inspect().Issues.Any(i => i.Code == AreaErrorCode.UnsupportedArtClassOrState), aid.ToString("x8"));
        }
    }

    [TestMethod]
    public void Damaged_walls_take_the_original_letters()
    {
        Assert.AreEqual(('l', 0), AreaDataOwner.WallDamage(0x400, rotation: 0, piece: 7));
        Assert.AreEqual(('r', 0), AreaDataOwner.WallDamage(0x400, rotation: 7, piece: 0)); // swapped to 0x80
        Assert.AreEqual(('l', 1), AreaDataOwner.WallDamage(0x80, rotation: 0, piece: 1)); // piece 1 as the original
        Assert.AreEqual(('r', 0), AreaDataOwner.WallDamage(0x80, rotation: 1, piece: 8));
        Assert.AreEqual(('l', 3), AreaDataOwner.WallDamage(0x80, rotation: 2, piece: 3)); // swapped to 0x400
        Assert.AreEqual(('l', 5), AreaDataOwner.WallDamage(0x480, rotation: 0, piece: 5)); // 0x400 wins
        Assert.AreEqual(('u', 7), AreaDataOwner.WallDamage(0, rotation: 0, piece: 7));
    }

    [TestMethod]
    public void A_tile_id_on_an_object_names_the_tile_file()
    {
        // Tile id 0x20: tile 0, palette slot 2 (bits 4-5); the fixture's terrain tile file.
        var snapshot = Complete(0x20, [], MissingDat(0, new uint[CritterMasks + 4]));
        var visual = Visual(snapshot);
        Assert.AreEqual(Named("art/tile/stonebse0a.art"), visual.Name);
        Assert.AreEqual(2, visual.Visual.RequestedPalette);
        Assert.AreEqual(0x0U, visual.Visual.LookupArt);
        Assert.IsFalse(visual.Visual.Mirrored);
        // A flippable tile (bits 6, 7) with flag bit 0 is mirrored, as a terrain cell is.
        Assert.IsTrue(Visual(Complete(0xC1, [], MissingDat(0, new uint[CritterMasks + 4]))).Visual.Mirrored);
        // ARC-DATA-036: one rule for every plan; a townmap-component plan names it the same way.
        using var townmap = Owner(0x20, [], MissingDat(0, new uint[CritterMasks + 4]), AreaSectorSelection.TownmapComponent);
        Assert.AreEqual(visual.Visual.Entry, townmap.Owner.LoadStrict().Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static).Entry);
    }

    [TestMethod]
    public void A_name_no_archive_holds_is_drawn_from_bad_art_on_every_plan()
    {
        // Scenery key 1 maps to a file the archives do not hold.
        const uint scenery = (4u << 28) | (1u << 19) | (3u << 14);
        var snapshot = Complete(scenery, [("art/badart.art", Art())], MissingDat(0, new uint[CritterMasks + 4]));
        var visual = Visual(snapshot);
        Assert.AreEqual(Named("art/badart.art"), visual.Name);
        Assert.AreEqual(0, visual.Visual.Frame); // frame 3 asked; bad art has only frame 0
        Assert.AreEqual(scenery, visual.Visual.RawArt);
        // Without bad art the name stays missing.
        using (var none = Owner(scenery, [], MissingDat(0, new uint[CritterMasks + 4])))
            Assert.IsTrue(none.Owner.Inspect().Issues.Any(i => i.Code == AreaErrorCode.MissingSource));
        // ARC-DATA-036: a townmap-component plan draws it from bad art too.
        using var townmap = Owner(scenery, [("art/badart.art", Art())], MissingDat(0, new uint[CritterMasks + 4]), AreaSectorSelection.TownmapComponent);
        Assert.AreEqual("original-badart-v1", townmap.Owner.LoadStrict().Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static).Policy);
    }

    [TestMethod]
    public void An_ambiguous_name_is_refused_not_replaced_by_bad_art()
    {
        const uint scenery = (4u << 28) | (1u << 19);
        using var f = Owner(scenery, [("art/scenery/gone.art", Art()), ("art/badart.art", Art())], MissingDat(0, new uint[CritterMasks + 4]),
            secondResource: Dat(("art/scenery/gone.art", Art())));
        var report = f.Owner.Inspect();
        Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.AmbiguousSource));
        Assert.IsFalse(report.Snapshot!.Visuals.Any(v => v.Policy == "original-badart-v1"));
    }

    [TestMethod]
    public void A_missing_name_list_entry_is_refused_not_replaced_by_bad_art()
    {
        // Monster 5 has no name: the original builds no path, so nothing is drawn.
        using var f = Owner(Monster | (5u << 23), [("art/badart.art", Art())], MissingDat(0, new uint[CritterMasks + 4]));
        var report = f.Owner.Inspect();
        Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.MissingSource && i.Stage == AreaStage.Mapping));
        Assert.IsFalse(report.Snapshot!.Visuals.Any(v => v.Policy == "original-badart-v1"));
    }

    private const int CritterMasks = 10;
    private static readonly byte[] MonsterNames = "{0}{wlf}{1}{lz1}"u8.ToArray();
    private static readonly byte[] UniqueNames = "{0}{abc}{1}{dgn}"u8.ToArray();

    /// <summary>The missing-art table: header (critter, monster and unique NPC ART file count)
    /// and the masks, 10 critters then 2 monsters and 2 unique NPCs.</summary>
    private static byte[] MissingDat(int header, uint[] masks)
    {
        var bytes = new byte[4 * (1 + masks.Length)]; BinaryPrimitives.WriteInt32LittleEndian(bytes, header);
        for (var index = 0; index < masks.Length; index++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4 * (index + 1)), masks[index]);
        return bytes;
    }

    private static Fixture Owner(uint aid, (string, byte[])[] art, byte[]? missing, AreaSectorSelection selection = AreaSectorSelection.AllSectors,
        byte[]? secondResource = null)
    {
        var resources = new List<(string, byte[])>
        {
            ("tile.mes", "{0}{stone/}{100}{stone/}{200}{stone/}{300}{stone/}{400}{end/}"u8.ToArray()), ("facade.mes", "{0}{fixture}"u8.ToArray()),
            ("scenery.mes", "{0}{fixture.art}{1}{gone.art}"u8.ToArray()), ("monster.mes", MonsterNames), ("unique_npc.mes", UniqueNames),
        };
        var roles = new List<AreaMappingRole> { AreaMappingRole.Tile, AreaMappingRole.Facade, AreaMappingRole.Scenery, AreaMappingRole.MonsterNames, AreaMappingRole.UniqueNpcNames };
        if (missing is not null) { resources.Add(("art/missing.dat", missing)); roles.Add(AreaMappingRole.MissingPolicy); }
        resources.Add(("art/tile/stonebse0a.art", Art())); resources.Add(("art/scenery/fixture.art", Art()));
        resources.AddRange(art);
        return new Fixture([(Start, [TypedRecord()])], extraPrototype: SceneryPrototype(aid), resourceEntries: [.. resources], mappingRoles: [.. roles],
            selection: selection, secondResource: secondResource);
    }

    private static AreaSnapshot Complete(uint aid, (string, byte[])[] art, byte[]? missing)
    {
        using var f = Owner(aid, art, missing);
        var report = f.Owner.Inspect();
        Assert.IsTrue(report.IsComplete, string.Join("; ", report.Issues));
        return report.Snapshot!;
    }

    /// <summary>The hash the fixture's archive gives an entry name, as written (with its NUL).</summary>
    private static string Named(string name) => Hash(System.Text.Encoding.ASCII.GetBytes(name + "\0"));

    /// <summary>The visual of the object made from prototype 7002 and its entry's name hash.</summary>
    private static (AreaVisualRecord Visual, string Name) Visual(AreaSnapshot snapshot)
    {
        var item = snapshot.Objects.Single(o => o.PrototypeNumber == 7002);
        var visual = snapshot.Visuals.Single(v => v.Key.Object == item.Key);
        return (visual, visual.Entry.NameSha256);
    }

    /// <summary>An instance of prototype 7002 that overrides only its location (slot 1).</summary>
    private static byte[] TypedRecord()
    {
        using var s = new MemoryStream(); I32(s, 119); s.Write(Id(1, 7002)); s.Write(Id(2, 11)); I32(s, 3);
        s.Write(new byte[] { 1, 0 }); I32(s, 2); I32(s, 0); I32(s, 0); s.WriteByte(1); I64(s, 0); return s.ToArray();
    }

    /// <summary>A scenery (type 3) prototype with the given art id, as the fixture's own prototype.</summary>
    private static byte[] SceneryPrototype(uint aid)
    {
        using var s = new MemoryStream(); I32(s, 119); s.Write(Id(-1)); s.Write(Id(1, 7002)); I32(s, 3);
        I32(s, 0); I32(s, 0); I32(s, 0);
        int[] optional = [1, 5, 6, 7, 15, 16, 17, 30, 31, 34, 35, 37, 40, 41];
        for (var field = 0; field < 42; field++)
        {
            if (optional.Contains(field)) s.WriteByte(0);
            else I32(s, field == 0 ? unchecked((int)aid) : field == 11 ? 100 : 0);
        }
        return s.ToArray();
    }
}
