using Arcanum.Data;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
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
        Assert.AreEqual((human.DistinctKeys, human.OriginalRgbaBytes!.Value), (woman.Textures.Count, woman.RgbaBytes));
        Assert.AreEqual(GaitTiming.HumanFixed.For(MovementGait.Run, NeighborDirection.XPlusYPlus), woman.Timing.For(MovementGait.Run, NeighborDirection.XPlusYPlus));
        var report = new List<string>();
        foreach (var body in Premade.Select(hero => HeroBody.For(hero.Race, hero.Gender)!.Value)
            .Concat(Enumerable.Range(0, CharacterCreationRules.RaceCount).SelectMany(race => new[] { CharacterGender.Male, CharacterGender.Female }
                .Where(gender => CharacterCreationRules.Allows((CharacterRace)race, gender)).Select(gender => HeroBody.For((CharacterRace)race, gender)!.Value))).Distinct())
        {
            var load = HeroBodyArt.Load(archive, body);
            // HB4: the human's frame counts and 195 keys for every creatable body.
            Assert.AreEqual((195, 195), (load.Profile.DistinctKeys, load.Textures.Count), body.Label);
            CollectionAssert.AreEqual(new[] { 1, 10, 10, 9, 9 }, load.Profile.Actions.Select(a => load.Profile.Resource(a).FramesPerGroup).ToArray(), body.Label);
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
                Assert.AreEqual(195, body.Textures.Count, $"{name} {item.Kind.Prototype} {worn.Value.Label}");
                report.Add($"{name} {item.Kind.Prototype}: {worn.Value.Label} armor={worn.Value.Armor} palette={worn.Value.Palette}");
            }
        }
        Console.WriteLine(string.Join("\n", report));
    }
}
