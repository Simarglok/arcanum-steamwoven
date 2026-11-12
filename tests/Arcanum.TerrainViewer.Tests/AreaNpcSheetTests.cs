using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-088: the NPC's character sheet of the dialog action c-e (ledger WN1, WN2, CHW24) over an invented follower: human, every primary stat 8,
/// level 1, effect 300 (strength +1, intelligence −1), stored resistances, fire rank 1.</summary>
[TestClass]
public sealed class AreaNpcSheetTests
{
    private static GameSessionOwner Session() => ConversationSessionFixture.CreateWithHero();

    private static CombatFields Npc(uint flags = 0, int gender = 0, int experience = 40, int alignment = -250)
    {
        var stats = new int[28];
        for (var i = 0; i < 8; i++) stats[i] = 8;
        stats[17] = 1; stats[18] = experience; stats[19] = alignment; stats[26] = gender; stats[27] = 0;
        var spellTech = new int[25];
        spellTech[12] = 1; spellTech[16] = -1;
        // The stored resistances: normal, fire, electrical, poison, magic.
        return new(CombatFieldOrigin.AreaObject, new CombatObjectFields(16, 17102, 0, 0, 0, 0, 0, 1, 2, 0, 4, [10, 5, 0, 0, 20], 0),
            null, null, null, null, null,
            new CombatCritterFields(flags, 0, stats, new int[12], new int[4], spellTech, 0, 0, 0, 1, 0, 62, [new(300, 1)]), null);
    }

    private static EffectRule? Effect(int number) => number == 300
        ? new(300, true, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 1), new(EffectAttributeKind.Stat, 4, EffectChangeOperator.Add, -1)]) : null;

    private static AreaNpcSheetPages Build(GameSessionOwner session, CombatFields fields, AreaLife? life = null)
    {
        var loaded = AreaAdvancement.Npc("virgil", fields, Effect)!.Value;
        return AreaNpcSheet.Build(session, "Virgil", fields, loaded, life, null, AreaHoverIndex.TextProfile)!;
    }

    [TestMethod]
    public void The_main_page_shows_the_npcs_own_values()
    {
        using var session = Session();
        var pages = Build(session, Npc(), new AreaLife(12, 15, 9, 10, 0));
        var view = pages.View;
        Assert.AreEqual(("Virgil", 1, 0, -25), (view.Name, view.Level, view.UnspentPoints, view.Alignment), "alignment 1/10 as the sheet prints it");
        Assert.AreEqual((12, 15, 9, 10), (view.HitPoints, view.MaxHitPoints, view.Fatigue, view.MaxFatigue), "the life the host reads");
        // Strength 8 + 1 and intelligence 8 − 1 by the effect, in the sheet's order (strength, constitution, dexterity, beauty, intelligence, willpower, perception, charisma).
        CollectionAssert.AreEqual(new[] { 9, 8, 8, 8, 7, 8, 8, 8 }, view.Stats.Select(stat => stat.Value).ToArray());
        Assert.AreEqual((AreaSheetMark.Raised, AreaSheetMark.None, AreaSheetMark.Lowered), (view.Stats[0].Mark, view.Stats[1].Mark, view.Stats[4].Mark));
        Assert.AreEqual(4500, view.Secondary[0].Value, "carry weight 500 × effective strength");
        Assert.IsTrue(view.ShowRaceGender);
        Assert.IsNull(view.ToNextLevel, "no experience table in this session");
    }

    private static int Speed(AreaNpcSheetPages pages) => pages.View.Secondary.Single(value => value.Label == AreaCharacterSheet.Label(CharacterStat.Speed)).Value;

    [TestMethod]
    public void The_speed_line_shows_the_tempus_fugit_change_the_combat_reads()
    {
        // ARC-MAGIC-1190 (TF2): the NPC's speed is dexterity 8 on the sheet; Tempus Fugit's +10 for a holder and -10 for everyone else (the stat range 1..100 clamps) show at once.
        using var session = Session();
        const string entity = "oid:virgil";
        var fields = Npc();
        var loaded = AreaAdvancement.Npc(entity, fields, Effect)!.Value;
        int Shown() => Speed(AreaNpcSheet.Build(session, "Virgil", fields, loaded, null, null, AreaHoverIndex.TextProfile)!);
        Assert.AreEqual(8, Shown(), "nobody holds the spell: no shift");
        const int flag = 0x4000000;
        Assert.AreNotEqual(GameSessionDisposition.Rejected, session.WriteObjectFlag(session.Version, "pc", SessionObjectFlagField.Spell, flag, true).Disposition);
        Assert.AreEqual(1, Shown(), "the hero holds it, the NPC does not: 8 - 10 is clamped to 1");
        Assert.AreNotEqual(GameSessionDisposition.Rejected, session.WriteObjectFlag(session.Version, entity, SessionObjectFlagField.Spell, flag, true).Disposition);
        Assert.AreEqual(18, Shown(), "the NPC holds it (it joined the hero): +10");
        Assert.AreNotEqual(GameSessionDisposition.Rejected, session.WriteObjectFlag(session.Version, "pc", SessionObjectFlagField.Spell, flag, false).Disposition);
        Assert.AreNotEqual(GameSessionDisposition.Rejected, session.WriteObjectFlag(session.Version, entity, SessionObjectFlagField.Spell, flag, false).Disposition);
        Assert.AreEqual(8, Shown(), "the spell is over");
    }

    [TestMethod]
    public void The_resistances_are_the_stored_ones_with_the_constitution_bonus_and_no_worn_armour()
    {
        using var session = Session();
        var resist = Build(session, Npc()).View.Resistances;
        CollectionAssert.AreEqual(new[] { "Normal", "Magick", "Fire", "Poison", "Electrical" }, resist.Select(value => value.Label).ToArray(), "the sheet's order");
        CollectionAssert.AreEqual(new[] { 10, 20, 5, 20, 0 }, resist.Select(value => value.Value).ToArray(), "poison has 5 × (8 − 4) from the constitution");
    }

    /// <summary>ARC-UI-1145 (0x43D6D0, RE2): the lines read the NPC under the effect list the session holds for it, as the combat does: the resistance changes, the Magick line
    /// among them, and a constitution change in the poison bonus. An entity the session knows no mobile NPC for has no list to read.</summary>
    [TestMethod]
    public void The_resistance_lines_follow_the_session_effect_list_of_the_npc()
    {
        using var session = Session();
        // The combat reads an NPC row's NPC record too.
        var fields = Npc() with { Npc = new CombatNpcFields(0, 0, 0, 0, 0, 0, 0, [new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)]) };
        var loaded = AreaAdvancement.Npc("oid:virgil", fields, Effect)!.Value;
        List<int> Lines() => [.. AreaNpcSheet.Build(session, "Virgil", fields, loaded, null, null, AreaHoverIndex.TextProfile)!.View.Resistances.Select(value => value.Value)];
        CollectionAssert.AreEqual(new[] { 10, 20, 5, 20, 0 }, Lines());
        EffectChange Resist(int attribute, int value) => new(EffectAttributeKind.Resistance, attribute, EffectChangeOperator.Add, value);
        var target = SessionLifeTarget.Critter("oid:virgil", new(), default, [new(300, EffectCause.Spell)]);
        var rule = new SessionEffectDefinition(150, [Resist(0, 30), Resist(3, 10), Resist(4, 25), new(EffectAttributeKind.Stat, 2, EffectChangeOperator.Add, 2)]);
        Assert.AreEqual(GameSessionFailure.None, session.AddNpcEffect(session.Version, target, rule, EffectCause.Spell).Failure);
        // Normal 10 + 30, magick 20 + 25, fire and electrical as stored, poison 5 x (10 - 4) + 10.
        CollectionAssert.AreEqual(new[] { 40, 45, 5, 40, 0 }, Lines());
        Assert.AreEqual(GameSessionFailure.None, session.RemoveNpcEffect(session.Version, target, 150).Failure);
        CollectionAssert.AreEqual(new[] { 10, 20, 5, 20, 0 }, Lines(), "the list gone: the stored lines again");
    }

    [TestMethod]
    public void A_monstrous_npc_shows_neither_race_nor_gender()
    {
        using var session = Session();
        Assert.IsFalse(Build(session, Npc(flags: 0x8000)).View.ShowRaceGender);
        var texts = AreaCharacterSheetArtLayout.Texts(Build(session, Npc(flags: 0x8000)).View, AreaCharacterSheetTexts.Project("t"), (_, text) => text.Length);
        var plain = AreaCharacterSheetArtLayout.Texts(Build(session, Npc()).View, AreaCharacterSheetTexts.Project("t"), (_, text) => text.Length);
        Assert.AreEqual(plain.Count - 2, texts.Count, "the race and the gender lines are left out");
    }

    [TestMethod]
    public void The_pages_are_read_only_views_of_the_npcs_sheet()
    {
        using var session = Session();
        var pages = Build(session, Npc());
        Assert.AreEqual((16, 0, false), (pages.Skills.Rows.Count, pages.Skills.UnspentPoints, pages.Skills.EditorOpen));
        Assert.AreEqual((10, false), (pages.Stats.Rows.Count, pages.Stats.EditorOpen));
        Assert.AreEqual((CharacterStat.Strength, 8, 9), (pages.Stats.Rows[2].Stat!.Value, pages.Stats.Rows[2].Value, pages.Stats.Rows[2].Effective));
        Assert.AreEqual((8, false), (pages.Tech.Rows.Count, pages.Tech.EditorOpen));
        Assert.IsTrue(pages.Tech.Rows.All(row => row.Degree == 0 && row.FloorDegree == row.Degree));
        var spells = pages.Spells(null, 12, AutoLevelRules.DefaultSpellWillpower);
        Assert.AreEqual((1, 1, false, false), (spells.Rank, spells.FloorRank, spells.EditorOpen, spells.CanLearn), "a college of the NPC's spell book, no editor");
        Assert.AreEqual(AreaSpellState.Next, spells.Spells[1].State);
    }

    [TestMethod]
    public void The_read_only_spells_page_greys_the_next_spell_and_has_no_buttons()
    {
        using var session = Session();
        var spells = Build(session, Npc()).Spells(null, 12, AutoLevelRules.DefaultSpellWillpower);
        Assert.AreEqual(AreaSheetColour.Gold, AreaCharacterSheetSpellsLayout.NameColour(1, 1), "the player's sheet: the next spell is gold");
        Assert.AreEqual(AreaSheetColour.Grey, AreaCharacterSheetSpellsLayout.NameColour(1, 1, readOnly: true), "the NPC's: grey (CHW24)");
        Assert.AreEqual(AreaSheetColour.Grey, AreaCharacterSheetSpellsLayout.LevelColour(1, 1, readOnly: true));
        Assert.AreEqual(AreaSheetColour.White, AreaCharacterSheetSpellsLayout.NameColour(0, 1, readOnly: true), "the known spell stays white");
        var (x, y) = AreaCharacterSheetSpellsLayout.Icon(1);
        static (int, int)? Size(int art) => (32, 32);
        Assert.AreEqual(AreaCharacterSheetSpellsLayout.Part.Plus, AreaCharacterSheetSpellsLayout.HitAt(x + 5, y + 5, spells, Size)!.Value.Part);
        Assert.AreEqual(AreaCharacterSheetSpellsLayout.Part.Row, AreaCharacterSheetSpellsLayout.HitAt(x + 5, y + 5, spells, Size, readOnly: true)!.Value.Part, "no + over the icon");
    }
}
