using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-007 character sheet over invented values (no original data): what it shows of the session hero, the
/// effect marks, the partial resistances, the refusals, the experience data the hero load brings and keeps over a quick load.</summary>
[TestClass]
public sealed class AreaCharacterSheetTests
{
    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    // Invented thresholds: level n needs 1000 × (n − 1)² experience.
    private static MesFile Levels() => Mes(string.Concat(Enumerable.Range(2, 50).Select(level => $"{{{level}}}{{{1000 * (level - 1) * (level - 1)}}}")));

    private static SessionExperienceRules Rules() =>
        AreaStartingHero.Experience(Levels(), Mes("{1}{100}{2}{300}"), Mes("{1000}{2 0 0}"), out _) ?? throw new AssertFailedException("rules");

    private static SessionHeroDefinition Definition(int alignment = 0) => new(16089, CharacterRace.HalfElf, CharacterGender.Male,
        [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, alignment, new(67, [new(CharacterStat.Dexterity, StatModifierOperator.Add, 1)]), null, 385,
        [new(new(SessionItemType.Weapon, 6049, 4021, 101)), new(new(SessionItemType.Armor, 8188, 4022, 102))]);

    private static GameSessionOwner Start(SessionHeroDefinition? hero, SessionExperienceRules? experience = null)
    {
        var load = hero is null ? null
            : new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic", experience);
        var created = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load);
        return created.Owner ?? throw new InvalidOperationException("SESSION_" + created.Failure);
    }

    private static SessionEffectDefinition Effect(int number, int stat, int value) =>
        new(number, [new EffectChange(EffectAttributeKind.Stat, stat, EffectChangeOperator.Add, value)]);

    private static AreaCharacterSheetView Sheet(GameSessionOwner session)
    {
        var build = AreaCharacterSheet.Build(session);
        Assert.AreEqual(AreaSheetRefusal.None, build.Refusal);
        return build.View!;
    }

    [TestMethod]
    public void The_sheet_shows_the_session_hero_in_the_original_order()
    {
        using var session = Start(Definition(alignment: -255), Rules());
        var view = Sheet(session);
        Assert.AreEqual(("half-elf-male", CharacterRace.HalfElf, CharacterGender.Male), (view.Name, view.Race, view.Gender));
        Assert.AreEqual((1, 0, (int?)1000, 5), (view.Level, view.Experience, view.ToNextLevel, view.UnspentPoints));
        Assert.AreEqual(-25, view.Alignment, "alignment / 10, truncated toward zero as the original prints it");
        var life = session.QueryHeroLife()!.Status;
        Assert.AreEqual((life.HitPoints, life.MaxHitPoints, life.Fatigue, life.MaxFatigue), (view.HitPoints, view.MaxHitPoints, view.Fatigue, view.MaxFatigue));
        CollectionAssert.AreEqual(new[] { "Strength", "Constitution", "Dexterity", "Beauty", "Intelligence", "Willpower", "Perception", "Charisma" },
            view.Stats.Select(value => value.Label).ToArray());
        CollectionAssert.AreEqual(new[] { "Carry weight", "Damage bonus", "AC adjustment", "Speed", "Heal rate", "Poison recovery", "Reaction modifier", "Max followers" },
            view.Secondary.Select(value => value.Label).ToArray());
        CollectionAssert.AreEqual(new[] { "Normal", "Magick", "Fire", "Poison", "Electrical" }, view.Resistances.Select(value => value.Label).ToArray());
        var stats = session.QueryHeroStats(new(0))!.Value.Snapshot!;
        CollectionAssert.AreEqual(new[] { CharacterStat.Strength, CharacterStat.Constitution, CharacterStat.Dexterity, CharacterStat.Beauty,
            CharacterStat.Intelligence, CharacterStat.Willpower, CharacterStat.Perception, CharacterStat.Charisma }.Select(stats.GetEffective).ToArray(),
            view.Stats.Select(value => value.Value).ToArray(), "effective values");
        Assert.AreEqual(9, view.Stats[2].Value, "dexterity 8 + the race effect");
        Assert.IsTrue(view.Stats.All(value => value.Mark == AreaSheetMark.None), "the innate race effect leaves no mark");
        CollectionAssert.AreEqual(new[] { CharacterStat.CarryWeight, CharacterStat.DamageBonus, CharacterStat.AcAdjustment, CharacterStat.Speed,
            CharacterStat.HealRate, CharacterStat.PoisonRecovery, CharacterStat.ReactionModifier, CharacterStat.MaxFollowers }.Select(stats.GetEffective).ToArray(),
            view.Secondary.Select(value => value.Value).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 0, 0, 20, 0 }, view.Resistances.Select(value => value.Value).ToArray(), "poison: 5 × (constitution 8 − 4)");
        Assert.AreEqual((0, 0), (view.MagickAptitude, view.TechAptitude));

        var lines = AreaCharacterSheet.Lines(view);
        Assert.AreEqual("half-elf-male | half-elf, male", lines[0]);
        Assert.AreEqual("Level 1 | experience 0, 1000 to the next level | unspent points 5", lines[1]);
        Assert.AreEqual("Alignment -25 | magick aptitude 0, tech aptitude 0", lines[2]);
        Assert.AreEqual($"Hit points {life.HitPoints}/{life.MaxHitPoints} | fatigue {life.Fatigue}/{life.MaxFatigue}", lines[3]);
        Assert.AreEqual("Strength 8 | Constitution 8 | Dexterity 9 | Beauty 8", lines[4]);
        Assert.AreEqual("Resistances normal 0%, magick 0%, fire 0%, poison 20%, electrical 0% (worn armour, constitution and effect changes)", lines[^1]);
        using var good = Start(Definition(alignment: 255));
        Assert.AreEqual(25, Sheet(good).Alignment);
    }

    /// <summary>ARC-UI-1145 (0x43D6D0): the resistance lines are the worn armour and the constitution bonus under the durable effects' resistance changes (the doll's armour rating reads the same),
    /// the Magick line included (the save still reads the stored value, ARC-MAGIC-033).</summary>
    [TestMethod]
    public void Resistance_effects_and_a_constitution_effect_move_the_resistance_lines()
    {
        using var session = Start(Definition(), Rules());
        EffectChange Resist(int attribute, int value) => new(EffectAttributeKind.Resistance, attribute, EffectChangeOperator.Add, value);
        Assert.AreEqual(GameSessionFailure.None, session.AddHeroEffect(session.Version, new(130, [Resist(0, 30), Resist(3, 10), Resist(4, 25), Resist(2, -5)]), EffectCause.Spell).Failure);
        Assert.AreEqual(GameSessionFailure.None, session.AddHeroEffect(session.Version, Effect(131, 2, 2), EffectCause.Item).Failure);
        var view = Sheet(session);
        // Constitution 8 + 2 gives the poison bonus 5 x (10 - 4) = 30, the effect adds 10; the electrical change cannot go below 0.
        CollectionAssert.AreEqual(new[] { 30, 25, 0, 40, 0 }, view.Resistances.Select(value => value.Value).ToArray());
        Assert.AreEqual("Resistances normal 30%, magick 25%, fire 0%, poison 40%, electrical 0% (worn armour, constitution and effect changes)", AreaCharacterSheet.Lines(view)[^1]);
        Assert.AreEqual(GameSessionFailure.None, session.RemoveHeroEffect(session.Version, 130).Failure);
        CollectionAssert.AreEqual(new[] { 0, 0, 0, 30, 0 }, Sheet(session).Resistances.Select(value => value.Value).ToArray(), "the resistance effect gone, the constitution bonus stays");
    }

    [TestMethod]
    public void Effects_that_are_not_innate_mark_a_stat_raised_or_lowered()
    {
        using var session = Start(Definition(), Rules());
        Assert.AreEqual(GameSessionFailure.None, session.AddHeroEffect(session.Version, Effect(120, 0, 2), EffectCause.Item).Failure);
        Assert.AreEqual(GameSessionFailure.None, session.AddHeroEffect(session.Version, Effect(121, 7, -1), EffectCause.Curse).Failure);
        Assert.AreEqual(GameSessionFailure.None, session.AddHeroEffect(session.Version, Effect(122, 6, 1), EffectCause.Background).Failure);
        Assert.AreEqual(GameSessionFailure.None, session.AddHeroEffect(session.Version, Effect(123, 2, 1), EffectCause.Spell).Failure);
        Assert.AreEqual(GameSessionFailure.None, session.AddHeroEffect(session.Version, Effect(124, 2, -1), EffectCause.Tech).Failure);
        var stats = Sheet(session).Stats.ToDictionary(value => value.Label);
        Assert.AreEqual((10, AreaSheetMark.Raised), (stats["Strength"].Value, stats["Strength"].Mark), "an item's +2");
        Assert.AreEqual((7, AreaSheetMark.Lowered), (stats["Charisma"].Value, stats["Charisma"].Mark), "a curse's −1");
        Assert.AreEqual((9, AreaSheetMark.None), (stats["Willpower"].Value, stats["Willpower"].Mark), "a background effect counts as innate");
        Assert.AreEqual((9, AreaSheetMark.None), (stats["Dexterity"].Value, stats["Dexterity"].Mark), "+1 and −1 cancel; the race effect stays unmarked");
        var lines = AreaCharacterSheet.Lines(Sheet(session));
        Assert.AreEqual("Strength 10 (raised by effects) | Constitution 8 | Dexterity 9 | Beauty 8", lines[4]);
        Assert.AreEqual("Intelligence 8 | Willpower 9 | Perception 8 | Charisma 7 (lowered by effects)", lines[5]);
    }

    [TestMethod]
    public void Worn_armour_and_constitution_give_the_resistances_clamped_to_ninety_five()
    {
        using var session = Start(Definition(), Rules());
        var mail = new EquipmentItem
        {
            Kind = EquipmentItemKind.Armor,
            Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium, Resistances = new(120, 15, 5, 80, 10) },
        };
        var worn = session.Equip(session.Version, "kit:2", EquipmentSlot.Armor, new(session.QueryHeroKit()[1].Kind, [], false, mail),
            (_, _) => true, _ => SessionInsertVerdict.Accepted);
        Assert.AreEqual(GameSessionFailure.None, worn.Failure);
        var resistances = Sheet(session).Resistances.ToDictionary(value => value.Label, value => value.Value);
        Assert.AreEqual((95, 10, 15, 95, 5), (resistances["Normal"], resistances["Magick"], resistances["Fire"], resistances["Poison"], resistances["Electrical"]),
            "normal 120 and poison 80 + 20 clamp at 95");
    }

    [TestMethod]
    public void No_hero_and_a_dead_hero_are_refused()
    {
        using var plain = Start(null);
        Assert.AreEqual(new AreaCharacterSheetBuild(AreaSheetRefusal.NoHero, null), AreaCharacterSheet.Build(plain));
        using var session = Start(Definition(), Rules());
        Assert.AreEqual(AreaSheetRefusal.None, AreaCharacterSheet.Refusal(session));
        Assert.AreEqual(GameSessionDisposition.Changed, session.Kill(session.Version, SessionLifeTarget.Hero).Disposition);
        Assert.AreEqual(new AreaCharacterSheetBuild(AreaSheetRefusal.Dead, null), AreaCharacterSheet.Build(session));
    }

    [TestMethod]
    public void Without_experience_data_the_next_level_is_unknown_and_a_level_up_shows_its_points()
    {
        using var bare = Start(Definition());
        Assert.IsNull(Sheet(bare).ToNextLevel);
        StringAssert.Contains(AreaCharacterSheet.Lines(Sheet(bare))[1], "next level unknown (no experience data)");

        using var session = Start(Definition(), Rules());
        Assert.AreEqual(GameSessionDisposition.Changed, session.AwardHeroExperience(session.Version, 4500).Disposition);
        var view = Sheet(session);
        Assert.AreEqual((3, 4500, (int?)4500, 7), (view.Level, view.Experience, view.ToNextLevel, view.UnspentPoints),
            "level 3 at 4000, level 4 needs 9000; one point per level");
    }

    [TestMethod]
    public void The_experience_tables_are_read_or_refused_with_their_reason()
    {
        var rules = Rules();
        Assert.AreEqual(1000, rules.Levels.Threshold(2));
        Assert.AreEqual(300, rules.QuestExperience(rules.QuestLevel(1000)));
        Assert.IsNull(AreaStartingHero.Experience(Mes("{2}{lots}"), Mes(""), Mes(""), out var detail));
        Assert.AreEqual("rules/xp_level.mes InvalidNumber 2", detail);
        Assert.IsNull(AreaStartingHero.Experience(Levels(), Mes("{1}{1x}"), Mes(""), out detail));
        Assert.AreEqual("rules/xp_quest.mes InvalidNumber 1", detail);
        Assert.IsNull(AreaStartingHero.Experience(Mes("{2}{1000}{3}{900}"), Mes(""), Mes(""), out detail));
        StringAssert.StartsWith(detail, "rules/xp_level.mes not strictly increasing");
    }

    [TestMethod]
    public void A_quick_load_keeps_the_experience_data()
    {
        var directory = Directory.CreateTempSubdirectory("arc-ui-007-").FullName;
        try
        {
            using var session = Start(Definition(), Rules());
            var slot = GameSessionSaveSlot.QuickSave(directory);
            var placement = new ActorPlacement(new(1), new(checked((int)AreaGridSession.Cell(AreaGridSession.Spawn).X),
                checked((int)AreaGridSession.Cell(AreaGridSession.Spawn).Y)), NeighborDirection.XPlus);
            Assert.IsTrue(AreaQuickSave.Save(session, placement, slot).Saved);
            var loaded = AreaQuickSave.Load(slot, session, _ => null);
            using var owner = loaded.Owner ?? throw new AssertFailedException(loaded.Status);
            Assert.AreSame(session.Experience, owner.Experience);
            Assert.AreEqual((int?)1000, Sheet(owner).ToNextLevel);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
