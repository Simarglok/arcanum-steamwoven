using System.Buffers.Binary;
using System.Text;
using Arcanum.Data;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-039: the hero is drawn with its race and gender's body, not the fixed human woman.</summary>
[TestClass]
public sealed class HeroBodyArtTests
{
    // The twelve pre-made heroes (main-menu order) and the body label their prototypes' art ids name (ARC-RES-027 SH-04).
    private static readonly (string Name, CharacterRace Race, CharacterGender Gender, string Label)[] Premade =
    [
        ("halfling-male", CharacterRace.Halfling, CharacterGender.Male, "GHM"), ("gnome-male", CharacterRace.Gnome, CharacterGender.Male, "GHM"),
        ("half-elf-male", CharacterRace.HalfElf, CharacterGender.Male, "HMM"), ("half-ogre-male", CharacterRace.HalfOgre, CharacterGender.Male, "HGM"),
        ("elf-male", CharacterRace.Elf, CharacterGender.Male, "EFM"), ("human-male", CharacterRace.Human, CharacterGender.Male, "HMM"),
        ("half-orc-male", CharacterRace.HalfOrc, CharacterGender.Male, "HMM"), ("dwarf-male", CharacterRace.Dwarf, CharacterGender.Male, "DFM"),
        ("human-female", CharacterRace.Human, CharacterGender.Female, "HMF"), ("elf-female", CharacterRace.Elf, CharacterGender.Female, "HMF"),
        ("half-orc-female", CharacterRace.HalfOrc, CharacterGender.Female, "HMF"), ("half-elf-female", CharacterRace.HalfElf, CharacterGender.Female, "HMF"),
    ];

    [TestMethod]
    public void EveryPremadeHeroHasItsPrototypesBody()
    {
        CollectionAssert.AreEqual(AreaStartingHero.Premade.Select(hero => hero.Name).ToArray(), Premade.Select(hero => hero.Name).ToArray());
        foreach (var (name, race, gender, label) in Premade)
            Assert.AreEqual(label, HeroBody.For(race, gender)!.Value.Label, name);
        // The elf man is no longer the fixed human woman (the reported bug).
        Assert.AreNotEqual(HeroBody.HumanFemale, HeroBody.For(CharacterRace.Elf, CharacterGender.Male));
        Assert.AreEqual(HeroBody.HumanFemale, HeroBody.For(CharacterRace.Human, CharacterGender.Female));
    }

    [TestMethod]
    public void TheBodyRewritesOnlyTheBodyAndGenderBitsOfTheHumanSelectors()
    {
        var elf = HeroBody.For(CharacterRace.Elf, CharacterGender.Male)!.Value;
        var human = ActorDiagnosticProfile.HumanFixedArea;
        // SH-04: the elf man's art id is 0x2C002000 (body 4, male); the same animation and weapon bits as the human's.
        Assert.AreEqual(0x2C000000u, elf.Selector(human.Idle.Selector));
        Assert.AreEqual(0x2C000501u, elf.Selector(human.Attack!.Selector));
        Assert.AreEqual(human.Walk.Selector, HeroBody.HumanFemale.Selector(human.Walk.Selector));
        Assert.AreEqual("art/critter/EFM/EFMUWXAa.art", elf.ArtPath(elf.Selector(human.Idle.Selector)));
        Assert.AreEqual("art/critter/EFM/EFMUWXBu.art", elf.ArtPath(elf.Selector(human.Attack.Selector)));
        Assert.AreEqual("art/critter/DFM/DFMUWXAg.art", HeroBody.For(CharacterRace.Dwarf, CharacterGender.Male)!.Value.ArtPath(0x29000180));
        // The armour part (for the worn-armour slice): leather on an elf man, plate on a halfling man (the dwarf's sexless art, N1).
        var leather = HeroBody.For(CharacterRace.Elf, CharacterGender.Male, 2)!.Value;
        Assert.AreEqual(("art/critter/EFM/EFMLAXAb.art", 0x2C200040u), (leather.ArtPath(leather.Selector(human.Walk.Selector)), leather.Selector(human.Walk.Selector)));
        var plate = HeroBody.For(CharacterRace.Halfling, CharacterGender.Male, 4)!.Value;
        Assert.AreEqual(("DFX", 4), (plate.Label, plate.GaitRateBonus));
        Assert.IsNull(HeroBody.For(CharacterRace.Human, CharacterGender.Male, 9));
        // The palette (bits 4..5) the worn body armour's use art sets (0x465AE0, for ARC-WORLD-112).
        Assert.AreEqual(0x2C200060u, HeroBody.For(CharacterRace.Elf, CharacterGender.Male, 2, 2)!.Value.Selector(human.Walk.Selector));
        Assert.IsNull(HeroBody.For(CharacterRace.Human, CharacterGender.Male, 0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => elf.ArtPath(0x2C00000F));
        // Races outside the creation table have no body here.
        Assert.IsNull(HeroBody.For(CharacterRace.Ogre, CharacterGender.Male));
    }

    [TestMethod]
    public void WornBodyArmourSetsTheArmourPartAndPalette()
    {
        // ARC-WORLD-112 (BA2): the worn item's use art (type 6) gives its subtype (bits 6..9) and its palette (bits 4..5).
        static uint Use(int subtype, int palette = 0) => 0x60000000u | (uint)subtype << 6 | (uint)palette << 4;
        var elf = HeroBody.For(CharacterRace.Elf, CharacterGender.Male)!.Value;
        Assert.AreEqual(HeroBody.For(CharacterRace.Elf, CharacterGender.Male, 8, 1), elf.Wearing(Use(8, 1)), "clothes in palette 1");
        Assert.AreEqual("art/critter/EFM/EFMCDXAa.art", elf.Wearing(Use(8))!.Value.ArtPath(0x2C800000));
        // Plate on an elf man draws the human sexless art (N1).
        Assert.AreEqual("HMX", elf.Wearing(Use(4))!.Value.Label);
        // Taking it off: the underwear in the base palette, whatever was worn.
        var dressed = HeroBody.For(CharacterRace.Dwarf, CharacterGender.Male, 3, 2)!.Value;
        Assert.AreEqual(HeroBody.For(CharacterRace.Dwarf, CharacterGender.Male), dressed.Wearing(null));
        Assert.AreEqual(HeroBody.For(CharacterRace.Dwarf, CharacterGender.Male, 0, 1), dressed.Wearing(null, basePalette: 1));
        // A subtype the critter art cannot hold: no body (the original refuses the item, BA6); a use art of another type gives part 0.
        Assert.IsNull(elf.Wearing(Use(9)));
        Assert.AreEqual(elf, elf.Wearing(0x20000840u));
        Assert.IsNull(elf.Wearing(null, basePalette: 4));
    }

    [TestMethod]
    public void DwarfAndHalflingBodiesWalkAndRunFourFramesFaster()
    {
        Assert.AreEqual(4, HeroBody.For(CharacterRace.Dwarf, CharacterGender.Male)!.Value.GaitRateBonus);
        Assert.AreEqual(4, HeroBody.For(CharacterRace.Gnome, CharacterGender.Male)!.Value.GaitRateBonus);
        Assert.AreEqual(4, HeroBody.For(CharacterRace.Halfling, CharacterGender.Male)!.Value.GaitRateBonus);
        Assert.AreEqual(0, HeroBody.For(CharacterRace.HalfOgre, CharacterGender.Male)!.Value.GaitRateBonus);
        Assert.AreEqual(0, HeroBody.For(CharacterRace.Elf, CharacterGender.Male)!.Value.GaitRateBonus);
        // Research L6: walk 17+4 at Speed 8, run 8+4..30+4.
        Assert.AreEqual(21, GaitTiming.FramesPerSecond(MovementGait.Walk, 8, 4));
        Assert.AreEqual(12, GaitTiming.FramesPerSecond(MovementGait.Run, 0, 4));
        Assert.AreEqual(34, GaitTiming.FramesPerSecond(MovementGait.Run, 40, 4));
        Assert.AreEqual(17, GaitTiming.FramesPerSecond(MovementGait.Walk, 8));
        var human = ActorDiagnosticProfile.HumanFixedArea;
        var fast = new GaitTiming(human.Walk.Stride!, human.Run!.Stride!, GaitTiming.BaseSpeed, rateBonus: 4);
        Assert.IsTrue(fast.For(MovementGait.Walk, NeighborDirection.XPlus).Value < GaitTiming.HumanFixed.For(MovementGait.Walk, NeighborDirection.XPlus).Value);
        Assert.Throws<ArgumentException>(() => new GaitTiming(human.Walk.Stride!, human.Run.Stride!, 8, rateBonus: 3));
    }

    /// <summary>ARC-RENDER-037: in combat mode the body shows the wielded weapon and the shield (WA1); each animation's file keeps or
    /// clears them as the original names it (MA2, MA3), and an armed walk falls back to the walk without the weapon (MA1).</summary>
    [TestMethod]
    public void TheArmedBodyShowsItsWeaponAndShieldInTheirAnimations()
    {
        var human = ActorDiagnosticProfile.HumanFixedArea;
        var man = HeroBody.For(CharacterRace.Human, CharacterGender.Male)!.Value;
        const uint dagger = 0x60000000u | 2u << 6, twoHanded = 0x60000000u | 7u << 6;
        // Out of combat mode nothing shows; in it the dagger (2), bare hands without a weapon or for a subtype of 0.
        Assert.AreEqual(man, man.Armed(false, dagger, 7));
        var armed = man.Armed(true, dagger, null)!.Value;
        Assert.AreEqual((2, 0), (armed.Weapon, armed.Shield));
        Assert.AreEqual((1, 1), (man.Armed(true, null, 7)!.Value.Weapon, man.Armed(true, null, 7)!.Value.Shield));
        Assert.AreEqual(1, man.Armed(true, 0x60000000u, null)!.Value.Weapon);
        Assert.AreEqual(0, man.Armed(true, null, 5)!.Value.Shield, "only an armour item shows as a shield");
        Assert.IsNull(man.Armed(true, 0x60000000u | 15u << 6, null), "subtype 15 has no weapon part");
        // The stand and the attack show the weapon; the walk without the shield; run, vault and the falls show neither.
        var shielded = man.Armed(true, dagger, 7)!.Value;
        Assert.AreEqual("art/critter/HMM/HMMUWSCa.art", shielded.ArtPath(shielded.Selector(human.Idle.Selector)));
        Assert.AreEqual("art/critter/HMM/HMMUWSCu.art", shielded.ArtPath(shielded.Selector(human.Attack!.Selector)));
        Assert.AreEqual("art/critter/HMM/HMMUWXCb.art", shielded.ArtPath(shielded.Selector(human.Walk.Selector)));
        Assert.AreEqual("art/critter/HMM/HMMUWXAg.art", shielded.ArtPath(shielded.Selector(human.Run!.Selector)));
        Assert.AreEqual("art/critter/HMM/HMMUWXAn.art", shielded.ArtPath(shielded.Selector(human.Vault!.Selector)));
        Assert.AreEqual("art/critter/HMM/HMMUWXAh.art", shielded.ArtPath(shielded.Selector(ActorDiagnosticProfile.HumanFallDownSelector)));
        // Animations outside 6..19 (stealth walk 3 here) keep both (MA2).
        Assert.AreEqual("art/critter/HMM/HMMUWSCd.art", shielded.ArtPath(shielded.Selector(0x200000C0u)));
        Assert.AreEqual(shielded.BodyBits, shielded.Selector(human.Idle.Selector) & 0x0FF00030u);
        // MA3: a two-handed sword with a shield is written as the sword.
        var both = man.Armed(true, twoHanded, 7)!.Value;
        Assert.AreEqual("art/critter/HMM/HMMUWSDu.art", both.ArtPath(both.Selector(human.Attack.Selector)));
        // MA1: an armed walk is tried with the weapon, then without it; a stand without a weapon then with bare hands.
        var walk = armed.Selector(human.Walk.Selector);
        CollectionAssert.AreEqual(new[] { walk, walk & ~15u }, armed.Candidates(human.Walk.Selector).ToArray());
        var stand = man.Selector(human.Idle.Selector);
        CollectionAssert.AreEqual(new[] { stand, stand | 1u }, man.Candidates(human.Idle.Selector).ToArray());
        CollectionAssert.AreEqual(new[] { armed.Selector(human.Attack.Selector) }, armed.Candidates(human.Attack.Selector).ToArray());
        // Unarmed out of combat mode the attack keeps the human's bare hands (nothing attacks out of combat mode).
        Assert.AreEqual(1u, man.Selector(human.Attack.Selector) & 15u);
    }

    // ARC-MAGIC-052 (TR4): the monster body of the hero is the art id the Trait component builds: class 12, the species, the animation, the palette.
    [TestMethod]
    public void TheMonsterBodyIsAClassTwelveArtIdOfTheSpeciesAndPalette()
    {
        var sheep = HeroBody.Monster(20, 1);
        Assert.IsTrue(sheep.IsMonster);
        Assert.IsFalse(HeroBody.HumanFemale.IsMonster);
        Assert.AreEqual("monster20", sheep.Label);
        Assert.AreEqual(0xCA000010u, sheep.MonsterSelector(0));
        Assert.AreEqual(0xCA000050u, sheep.MonsterSelector(1));
        Assert.AreEqual(0xCA000190u, sheep.MonsterSelector(6));
        Assert.AreEqual(0xCF8000B0u, HeroBody.Monster(31, 3).MonsterSelector(2), "species 31 (bits 23 to 27), palette 3 and animation 2 fill their fields");
        Assert.AreNotEqual(sheep, HeroBody.Monster(20, 2), "the palette is part of the body");
    }

    // ARC-MAGIC-1219 (PR1, PR2, PR4): the weapon part follows combat mode, and the original's missing-art repair picks the file.
    [TestMethod]
    public void TheMonsterBodysWeaponPartFollowsCombatModeAndTheRepairPicksTheFile()
    {
        var calm = HeroBody.Monster(26, 1); var fighting = calm.MonsterInCombat(true);
        Assert.AreEqual(0, calm.Weapon); Assert.AreEqual(1, fighting.Weapon);
        Assert.AreNotEqual(calm, fighting, "the weapon part is part of the body (a combat-mode change swaps it)");
        Assert.AreEqual(calm, fighting.MonsterInCombat(false));
        // The stand and the walk keep the part, the run, fall, get up, vault and throw clear it, the attacks always carry 1.
        Assert.AreEqual((0, 1), ((int)(calm.MonsterSelector(0) & 15), (int)(fighting.MonsterSelector(0) & 15)));
        Assert.AreEqual((0, 1), ((int)(calm.MonsterSelector(1) & 15), (int)(fighting.MonsterSelector(1) & 15)));
        foreach (var animation in new[] { 6, 7, 8, 13, 14 }) Assert.AreEqual(0, (int)(fighting.MonsterSelector(animation) & 15), $"animation {animation} clears the weapon");
        foreach (var animation in new[] { 20, 21 }) Assert.AreEqual((1, 1), ((int)(calm.MonsterSelector(animation) & 15), (int)(fighting.MonsterSelector(animation) & 15)), $"animation {animation}");
        Assert.AreEqual(26, (int)(fighting.MonsterSelector(20) >> 23 & 31)); Assert.AreEqual(1, (int)(fighting.MonsterSelector(20) >> 4 & 3));
        // PR4: stand 0 -> stand 1; walk 1 -> walk 0; run -> walk 0; the others have no repair of their own here.
        CollectionAssert.AreEqual(new[] { calm.MonsterSelector(0), fighting.MonsterSelector(0) }, calm.MonsterCandidates(0).ToArray());
        CollectionAssert.AreEqual(new[] { fighting.MonsterSelector(0) }, fighting.MonsterCandidates(0).ToArray());
        CollectionAssert.AreEqual(new[] { fighting.MonsterSelector(1), calm.MonsterSelector(1) }, fighting.MonsterCandidates(1).ToArray());
        CollectionAssert.AreEqual(new[] { calm.MonsterSelector(1) }, calm.MonsterCandidates(1).ToArray());
        CollectionAssert.AreEqual(new[] { fighting.MonsterSelector(6), calm.MonsterSelector(1) }, fighting.MonsterCandidates(6).ToArray());
        CollectionAssert.AreEqual(new[] { fighting.MonsterSelector(20) }, fighting.MonsterCandidates(20).ToArray());
        Assert.AreEqual(0xCA000010u, HeroBody.Monster(20, 1).MonsterSelector(0), "weapon part 0 changes nothing for the sheep body");
    }

    // ARC-MAGIC-1219 (L6): the profile's provenance names a monster body, not human fields.
    [TestMethod]
    public void AMonsterProfilesProvenanceNamesTheSpeciesAndPalette()
    {
        using var archive = MonsterArchive(new() { ["ba"] = [0, 1], ["ab"] = [0, 1], ["bu"] = [0, 1], ["ag"] = [0, 1], ["an"] = [0, 1] });
        var load = HeroBodyArt.LoadMonster([archive], HeroBody.Monster(28, 1, 1), Name);
        Assert.AreEqual("hero monster body species28 palette1 single-layer", load.Profile.Provenance);
    }

    // The synthetic species "air": stands and runs the air elemental's way (no weapon-0 stand, no run), palette 0 only.
    // ARC-MAGIC-1219: every species and palette of the monster ARTs, out of and in combat mode, reads from the installed archives (opt-in: DAT_ROOT = the installed game folder with arcanum1.dat to Arcanum4.dat).
    [TestMethod, TestCategory("RealData")]
    public void EveryMonsterSpeciesLoadsInBothModesFromTheInstalledArt()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrEmpty(root)) { Assert.Inconclusive("DAT_ROOT runs the installed monster body check."); return; }
        var options = new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 };
        var archives = Directory.EnumerateFiles(root, "*.dat").Where(f => Path.GetFileName(f).StartsWith("arcanum", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(f => DatArchive.Open(f, options)).ToList();
        try
        {
            var (holder, entry) = archives.SelectMany(a => a.Entries.Select(e => (Archive: a, Entry: e)))
                .First(x => Encoding.ASCII.GetString(x.Entry.RawNameBytes.ToArray()).TrimEnd('\0').Replace('\\', '/').Equals("art/monster/monster.mes", StringComparison.OrdinalIgnoreCase));
            var names = Arcanum.Data.Area.AreaObjectArt.ReadNameTable(holder.ReadEntry(entry)) ?? throw new AssertFailedException("monster.mes did not read");
            string? Name(Arcanum.Data.Area.AreaMappingRole role, int number) => names.TryGetValue(number, out var name) ? name : null;
            var report = new List<string>(); var failures = new List<string>();
            for (var species = 0; species < 32; species++)
            foreach (var combat in new[] { false, true })
            foreach (var palette in new[] { 0, 1 })
            {
                var body = HeroBody.Monster(species, palette, combat ? 1 : 0);
                try
                {
                    var load = HeroBodyArt.LoadMonster(archives, body, Name);
                    var p = load.Profile;
                    Assert.IsNull(p.Throw, $"species {species}: the throw is not loaded");
                    Assert.IsTrue(p.Actions.All(a => p.Resource(a).Palette is 0 or 1), $"species {species}");
                    report.Add($"species={species} {Name(Arcanum.Data.Area.AreaMappingRole.MonsterNames, species)} combat={combat} palette={palette} keys={load.Textures.Count} bytes={load.RgbaBytes} "
                        + $"actions={string.Join(",", p.Actions.Select(a => a + ":" + p.Resource(a).Palette))} low={(p.LowAttack is null ? "none" : "own")}");
                }
                catch (TerrainViewerException error) { failures.Add($"species={species} combat={combat} palette={palette} {error.Error.Code}"); }
            }
            Console.WriteLine(string.Join("\n", report)); Console.WriteLine("FAILURES\n" + string.Join("\n", failures));
            // The spider queen (30) does not decode in any palette (no installed spell makes it); everything else reads.
            Assert.AreEqual(0, failures.Count(f => !f.StartsWith("species=30 ")), string.Join("; ", failures));
        }
        finally { foreach (var archive in archives) archive.Dispose(); }
    }

    private static string? Name(Arcanum.Data.Area.AreaMappingRole role, int number) => "air";

    [TestMethod]
    public void TheAirElementalsMissingStandAndRunAreRepairedAndItsMissingPaletteReadsAsPaletteZero()
    {
        // Files: the stand with weapon 1 (ba), the walk (ab), the attack with weapon 1 (bu); no stand with weapon 0 (aa), no run (ag).
        using var archive = MonsterArchive(new() { ["ba"] = [0], ["ab"] = [0], ["bu"] = [0] });
        foreach (var combat in new[] { false, true })
        {
            var body = HeroBody.Monster(28, 1, combat ? 1 : 0);
            var load = HeroBodyArt.LoadMonster([archive], body, Name);
            var profile = load.Profile;
            // The stand is the repaired one (weapon part 1) in both modes; the walk is the part the mode names or, with 1 missing, 0.
            Assert.AreEqual(body.MonsterSelector(0) | 1u, profile.Idle.Selector, $"stand combat={combat}");
            Assert.AreEqual(body.MonsterSelector(1) & ~15u, profile.Walk.Selector, $"walk combat={combat}");
            // The run is the walk's frames under the run's own selector with the weapon part 15; the vault is the stand's.
            Assert.AreEqual(body.MonsterSelector(6) | 0xFu, profile.Run!.Selector);
            Assert.AreEqual(body.MonsterSelector(13) | 0xFu, profile.Vault!.Selector);
            Assert.AreEqual(body.MonsterSelector(20), profile.Attack!.Selector, "the attack file with weapon part 1 (the human body's rule: nothing attacks unarmed out of combat mode, the ART is the same)");
            Assert.IsNull(profile.LowAttack, "no species file: the attack plays it"); Assert.IsNull(profile.Throw, "the throw is not loaded (owner D2)");
            // PR5: palette 1 asked, the ARTs hold palette 0 only: the frames are palette 0's, the art id keeps palette 1.
            Assert.IsTrue(profile.Actions.All(a => profile.Resource(a).Palette == 0), "read as palette 0");
            Assert.IsTrue(load.Textures.Keys.All(k => k.Palette == 0));
            Assert.AreEqual(1, (int)(profile.Idle.Selector >> 4 & 3));
        }
    }

    [TestMethod]
    public void AnArtWithNoPaletteAtAllIsRefusedAndAWeaponOneWalkIsKeptWhenTheSpeciesHasIt()
    {
        string? Lizard(Arcanum.Data.Area.AreaMappingRole role, int number) => "air";
        // The species has its own walk with weapon 1 (the lizard man's bb): in combat mode it is the walk, out of it the weapon-0 walk.
        using var own = MonsterArchive(new() { ["ba"] = [0], ["ab"] = [0], ["bb"] = [0], ["bu"] = [0] });
        Assert.AreEqual(HeroBody.Monster(28, 0, 1).MonsterSelector(1), HeroBodyArt.LoadMonster([own], HeroBody.Monster(28, 0, 1), Lizard).Profile.Walk.Selector);
        Assert.AreEqual(HeroBody.Monster(28, 0, 0).MonsterSelector(1), HeroBodyArt.LoadMonster([own], HeroBody.Monster(28, 0, 0), Lizard).Profile.Walk.Selector);
        // A palette 0 that is absent too has nothing to fall back to.
        using var none = MonsterArchive(new() { ["ba"] = [], ["ab"] = [], ["bu"] = [] });
        Assert.AreEqual(TerrainViewerErrorCode.ActorMetadata, Assert.Throws<TerrainViewerException>(() => HeroBodyArt.LoadMonster([none], HeroBody.Monster(28, 1, 1), Lizard)).Error.Code);
    }

    [TestMethod]
    public void TheLowAttackOfAWeaponOneFileIsReadWhenTheSpeciesHasItAndTheThrowNever()
    {
        using var archive = MonsterArchive(new() { ["ba"] = [0], ["ab"] = [0], ["bu"] = [0], ["bv"] = [0], ["ao"] = [0], ["bo"] = [0] });
        var load = HeroBodyArt.LoadMonster([archive], HeroBody.Monster(26, 1, 1), Name);
        Assert.IsNotNull(load.Profile.LowAttack, "the fire elemental's own low attack");
        Assert.AreEqual(HeroBody.Monster(26, 1, 1).MonsterSelector(21), load.Profile.LowAttack.Selector);
        Assert.IsNull(load.Profile.Throw, "the throw files are there, and not read");
    }

    // A DAT holding the synthetic species "air": each key is the letters after "airuwX" (weapon letter, animation letter), each value the palettes (0..3) the ART holds.
    private static DatArchive MonsterArchive(Dictionary<string, int[]> files)
    {
        var entries = files.Select(f => (Name: $"art/monster/air/airuwX{f.Key}.art", Bytes: SyntheticArt(f.Value))).ToList();
        entries.Add(("art/monster/monster.mes", []));
        using var output = new MemoryStream();
        var offsets = new List<uint>();
        foreach (var (_, bytes) in entries) { offsets.Add((uint)output.Position); output.Write(bytes); }
        using var table = new MemoryStream();
        WriteU32(table, (uint)entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var name = Encoding.ASCII.GetBytes(entries[i].Name + "\0");
            WriteU32(table, (uint)name.Length); table.Write(name); table.Write(new byte[4]);
            WriteU32(table, 1); WriteU32(table, (uint)entries[i].Bytes.Length); WriteU32(table, (uint)entries[i].Bytes.Length); WriteU32(table, offsets[i]);
        }
        table.Position = 0; table.CopyTo(output);
        output.Write(new byte[16]); output.Write("1TAD"u8);
        WriteU32(output, (uint)entries.Count); WriteU32(output, (uint)(table.Length + 28));
        return DatArchive.Open(new MemoryStream(output.ToArray(), writable: false), new DatReaderOptions { MaxArchiveBytes = 1 << 20, MaxStoredEntryBytes = 1 << 20, MaxDecodedEntryBytes = 1 << 20 });
    }

    // An ART of eight rotations of two one-pixel frames holding the given palettes.
    private static byte[] SyntheticArt(int[] palettes)
    {
        const int headerBytes = 132, paletteBytes = 1024, frameHeaderBytes = 28, groups = 8, frames = 2;
        var total = groups * frames;
        var table = headerBytes + palettes.Length * paletteBytes;
        var payload = table + total * frameHeaderBytes;
        var bytes = new byte[payload + total];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        foreach (var palette in palettes) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12 + 4 * palette), 1u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), frames);
        var at = headerBytes;
        foreach (var _ in palettes) { bytes[at + 4] = bytes[at + 5] = bytes[at + 6] = bytes[at + 7] = 255; at += paletteBytes; }
        for (var index = 0; index < total; index++)
        {
            var frame = table + index * frameHeaderBytes;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 8), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 20), 4); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 24), 2);
            bytes[payload + index] = 1;
        }
        return bytes;
    }

    private static void WriteU32(Stream stream, uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); stream.Write(bytes); }

    [TestMethod]
    public void AMonsterBodyThatCannotBeReadIsRefusedWithATypedFailure()
    {
        string? Name(Arcanum.Data.Area.AreaMappingRole role, int number) => "shp";
        // No archive holds the species' ARTs: the stand is required.
        var none = Assert.Throws<TerrainViewerException>(() => HeroBodyArt.LoadMonster([], HeroBody.Monster(20, 0), Name));
        Assert.AreEqual(TerrainViewerErrorCode.ActorMetadata, none.Error.Code);
        // A body that is no monster, a species or palette the art id cannot hold.
        foreach (var body in new[] { HeroBody.HumanFemale, HeroBody.Monster(32, 0), HeroBody.Monster(20, 4) })
            Assert.AreEqual(TerrainViewerErrorCode.ActorInput, Assert.Throws<TerrainViewerException>(() => HeroBodyArt.LoadMonster([], body, Name)).Error.Code, body.ToString());
    }

    [TestMethod, TestCategory("RealData")]
    public void EveryCreatableBodyLoadsFromTheInstalledArt()
    {
        // Opt-in: ARC_AREA_ORIGINAL=1 and ARC_AREA_RESOURCE1 (arcanum1.dat).
        if (Environment.GetEnvironmentVariable("ARC_AREA_ORIGINAL") != "1") { Assert.Inconclusive("ARC_AREA_ORIGINAL=1 runs the original hero body check."); return; }
        var path = Environment.GetEnvironmentVariable("ARC_AREA_RESOURCE1") ?? throw new AssertFailedException("ARC_AREA_RESOURCE1 is required");
        using var archive = DatArchive.Open(path, new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 });
        var human = ActorDiagnosticProfile.HumanFixedArea;
        // The human woman read by name is the pinned fixed human: same files, frames, rates, strides and every frame's bytes.
        var woman = HeroBodyArt.Load(archive, HeroBody.HumanFemale);
        foreach (var action in human.Actions)
        {
            var (pinned, read) = (human.Resource(action), woman.Profile.Resource(action));
            Assert.AreEqual((pinned.Ordinal, pinned.Selector, pinned.FramesPerGroup, pinned.RawFramesPerSecond, pinned.ActionFrame, pinned.Stride),
                (read.Ordinal, read.Selector, read.FramesPerGroup, read.RawFramesPerSecond, read.ActionFrame, read.Stride), action.ToString());
        }
        // ARC-COMBAT-021: the five pinned actions' frames are the fixed human's; the fall-down and get-up frames come on top.
        var fiveSelectors = human.Actions.Select(action => human.Resource(action).Selector).ToHashSet();
        var five = woman.Textures.Values.Where(texture => fiveSelectors.Contains(texture.Key.Selector)).ToArray();
        Assert.AreEqual((human.DistinctKeys, human.OriginalRgbaBytes!.Value), (five.Length, five.Sum(texture => (long)texture.Width * texture.Height * 4)));
        Assert.AreEqual(GaitTiming.HumanFixed.For(MovementGait.Run, NeighborDirection.XPlusYPlus), woman.Timing.For(MovementGait.Run, NeighborDirection.XPlusYPlus));
        var report = new List<string>();
        foreach (var body in Premade.Select(hero => HeroBody.For(hero.Race, hero.Gender)!.Value)
            .Concat(Enumerable.Range(0, CharacterCreationRules.RaceCount).SelectMany(race => new[] { CharacterGender.Male, CharacterGender.Female }
                .Where(gender => CharacterCreationRules.Allows((CharacterRace)race, gender)).Select(gender => HeroBody.For((CharacterRace)race, gender)!.Value))).Distinct())
        {
            var load = HeroBodyArt.Load(archive, body);
            // HB4: the human's frame counts and 195 keys for every creatable body; ARC-COMBAT-021 (UC5): every body also has its
            // fall-down and get-up ARTs, five used groups of each.
            Assert.IsNotNull(load.Profile.FallDown, body.Label); Assert.IsNotNull(load.Profile.GetUp, body.Label);
            // The installed fall is 11 frames at 11 fps, the get-up 19 at 14, on every body.
            Assert.AreEqual((11, 11, 19, 14), (load.Profile.FallDown.FramesPerGroup, load.Profile.FallDown.RawFramesPerSecond,
                load.Profile.GetUp.FramesPerGroup, load.Profile.GetUp.RawFramesPerSecond), body.Label);
            var downKeys = 5 * (load.Profile.FallDown.FramesPerGroup + load.Profile.GetUp.FramesPerGroup);
            // ARC-RENDER-039: every body also has its bare-handed low attack and its throw (installed in underwear: 7 frames at 13 fps,
            // action frame 5, and 11 frames at 12 fps, action frame 4; 435 keys a body).
            Assert.IsNotNull(load.Profile.LowAttack, body.Label); Assert.IsNotNull(load.Profile.Throw, body.Label);
            var attackKeys = 5 * (load.Profile.LowAttack.FramesPerGroup + load.Profile.Throw.FramesPerGroup);
            Assert.AreEqual((195 + downKeys + attackKeys, 195 + downKeys + attackKeys), (load.Profile.DistinctKeys, load.Textures.Count), body.Label);
            CollectionAssert.AreEqual(new[] { 1, 10, 10, 9, 9 }, load.Profile.Actions.Take(5).Select(a => load.Profile.Resource(a).FramesPerGroup).ToArray(), body.Label);
            // HB3: the installed strides (axis dx, dy, diagonal) of walk and run.
            var (walk, run) = body.Label switch
            {
                "DFM" or "GHM" => ((27, 14, 40), (78, 40, 112)),
                "HGM" => ((55, 28, 80), (137, 69, 196)),
                _ => ((42, 22, 60), (119, 60, 170)),
            };
            Assert.AreEqual(walk, (load.Profile.Walk.Stride!.AxisDx, load.Profile.Walk.Stride.AxisDy, load.Profile.Walk.Stride.DiagonalPixels), body.Label);
            Assert.AreEqual(run, (load.Profile.Run!.Stride!.AxisDx, load.Profile.Run.Stride.AxisDy, load.Profile.Run.Stride.DiagonalPixels), body.Label);
            Assert.AreEqual(body.GaitRateBonus, load.Timing.RateBonus, body.Label);
            Assert.AreEqual(ActorDiagnosticProfileId.HeroBody, load.Profile.Id);
            var plan = load.Plan(new ActorDiagnosticPlacementInput(6, 1, 0, 0, 0, 0, ActorDiagnosticRenderBranch.NormalUncached,
                100, false, ActorDiagnosticAction.Idle, 5, 0, 0, 0, 0, 0, 0, 0));
            Assert.AreEqual(load.Profile.Idle.Selector, plan.Current.TextureKey.Selector, body.Label);
            report.Add($"{body.Label} keys={load.Textures.Count} bytes={load.RgbaBytes} " + string.Join(" ", load.Profile.Actions.Select(a =>
                $"{a}:{load.Profile.Resource(a).FramesPerGroup}f/{load.Profile.Resource(a).RawFramesPerSecond}fps")) + $" walk={load.Profile.Walk.Stride} run={load.Profile.Run!.Stride}");
        }
        Console.WriteLine(string.Join("\n", report));
    }

    /// <summary>ARC-WORLD-112: every pre-made hero's worn body armour draws a body that loads from the installed art (prints the
    /// body, armour part and palette per hero; no original text).</summary>
    [TestMethod, TestCategory("RealData")]
    public void EveryPremadeHerosBodyArmourLoadsFromTheInstalledArt()
    {
        // Opt-in: ARC_AREA_ORIGINAL=1, ARC_AREA_RESOURCE1 (arcanum1.dat) and ARC_AREA_CONVERSATION_GAME_ROOT.
        if (Environment.GetEnvironmentVariable("ARC_AREA_ORIGINAL") != "1") { Assert.Inconclusive("ARC_AREA_ORIGINAL=1 runs the original hero body check."); return; }
        var path = Environment.GetEnvironmentVariable("ARC_AREA_RESOURCE1") ?? throw new AssertFailedException("ARC_AREA_RESOURCE1 is required");
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT") ?? throw new AssertFailedException("ARC_AREA_CONVERSATION_GAME_ROOT is required");
        using var archive = DatArchive.Open(path, new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 });
        var module = Path.Combine(root, "modules", "Arcanum.dat");
        var report = new List<string>();
        foreach (var (name, race, gender, _) in Premade)
        {
            var load = AreaStartingHero.Load(module, AreaStartingHero.Find(name)!);
            Assert.AreEqual(AreaHeroStatus.Ready, load.Status, name);
            var bare = HeroBody.For(race, gender)!.Value;
            // The pre-made start wears the kit's best body armour (ARC-WORLD-034); the human woman's dress stays carried.
            var armour = load.KitWear!.Where(item => item is not null && Arcanum.Simulation.Equipment.EquipmentRules.WearSlot(item.Item)
                == Arcanum.Simulation.Equipment.EquipmentSlot.Armor).Select(item => item!).ToArray();
            Assert.IsTrue(armour.Length > 0, $"{name}: the kit holds body armour");
            foreach (var item in armour)
            {
                var worn = bare.Wearing(item.UseArt);
                Assert.IsNotNull(worn, $"{name} {item.Kind.Prototype}: subtype has no art");
                var body = HeroBodyArt.Load(archive, worn.Value);
                Assert.AreEqual(195 + 5 * (body.Profile.FallDown!.FramesPerGroup + body.Profile.GetUp!.FramesPerGroup
                    + body.Profile.LowAttack!.FramesPerGroup + body.Profile.Throw!.FramesPerGroup), body.Textures.Count,
                    $"{name} {item.Kind.Prototype} {worn.Value.Label}");
                report.Add($"{name} {item.Kind.Prototype}: {worn.Value.Label} armor={worn.Value.Armor} palette={worn.Value.Palette}");
            }
            // ARC-RENDER-037: in combat mode each kit weapon (and bare hands) draws an armed body that loads: its stand and attack
            // show the weapon, its walk is the walk without it (no creatable body has an armed walk, MA1).
            var dressed = bare.Wearing(armour[0].UseArt)!.Value;
            var weapons = load.KitWear!.Where(item => item is not null && item.Item.Kind == Arcanum.Simulation.Equipment.EquipmentItemKind.Weapon)
                .Select(item => (uint?)item!.UseArt).Append(null);
            foreach (var use in weapons)
            {
                var armed = dressed.Armed(true, use, null)!.Value;
                var body = HeroBodyArt.Load(archive, armed);
                Assert.AreEqual((armed.Weapon, armed.Weapon, 0), ((int)(body.Profile.Idle.Selector & 15), (int)(body.Profile.Attack!.Selector & 15),
                    (int)(body.Profile.Walk.Selector & 15)), $"{name} weapon {armed.Weapon}");
                // ARC-RENDER-039: bare hands and the bow have their own low attack; every other kit weapon's is repaired to the attack
                // (MA1) and not read; the throw drops the weapon (MA2).
                Assert.AreEqual(armed.Weapon is 1 or 8, body.Profile.LowAttack is not null, $"{name} weapon {armed.Weapon} low attack");
                Assert.AreEqual(0u, body.Profile.Throw!.Selector & 15, $"{name} throw");
                report.Add($"{name} combat: {armed.Label} weapon={armed.Weapon} attack={body.Profile.Attack.FramesPerGroup}f/{body.Profile.Attack.RawFramesPerSecond}fps action={body.Profile.Attack.ActionFrame}"
                    + $" low={(body.Profile.LowAttack is { } low ? $"{low.FramesPerGroup}f/{low.RawFramesPerSecond}fps action={low.ActionFrame}" : "attack")}"
                    + $" throw={body.Profile.Throw.FramesPerGroup}f/{body.Profile.Throw.RawFramesPerSecond}fps action={body.Profile.Throw.ActionFrame}");
            }
            // With a shield (no kit holds one): the dagger's stand and attack are the shield files, the walk the plain walk.
            var shielded = dressed.Armed(true, 0x60000000u | 2u << 6, Arcanum.Simulation.Equipment.WeaponArtRules.ArmorItemType)!.Value;
            var withShield = HeroBodyArt.Load(archive, shielded);
            Assert.AreEqual((1, 1, 0), ((int)(withShield.Profile.Idle.Selector >> 19 & 1), (int)(withShield.Profile.Attack!.Selector >> 19 & 1),
                (int)(withShield.Profile.Walk.Selector >> 19 & 1)), $"{name} shield");
        }
        Console.WriteLine(string.Join("\n", report));
    }
}
