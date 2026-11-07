using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Magic;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-MAGIC-008: the spells page of the character sheet (ledger SG1–SG8) over synthetic heroes and synthetic spell data
/// (invented names, costs and values).</summary>
[TestClass]
public sealed class AreaSpellsPageTests
{
    private static GameSessionOwner Hero(int unspent = 5, int intelligence = 8, int willpower = 8, int level = 1, CharacterRace race = CharacterRace.HalfElf)
    {
        int[] stats = [8, 8, 8, 8, intelligence, 8, willpower, 8, 0, 0];
        var hero = new SessionHeroDefinition(16089, race, CharacterGender.Male, stats, level, unspent, 0, null, null, 385, []);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        return AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
    }

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;

    // Every record's two required lines, with college 0's spells given costs: spell 0 maintained every second, spell 1 every
    // five seconds, spell 2 not maintained.
    private static SpellData Data()
    {
        var list = new SortedDictionary<int, string>();
        for (var spell = 0; spell < SpellRecord.Count; spell++)
        {
            list[SpellRecordTable.FirstMessage(spell)] = "AoE: Tgt_Self";
            list[SpellRecordTable.FirstMessage(spell) + 1] = "";
        }
        list[SpellRecordTable.FirstMessage(0) + 1] = "Cost: 4, Maintain: (2@1)";
        list[SpellRecordTable.FirstMessage(1) + 1] = "IQ: 7, Cost: 6, Maintain: (3@5)";
        list[SpellRecordTable.FirstMessage(2) + 1] = "Cost: 9";
        var texts = new StringBuilder();
        for (var n = 0; n < SpellRecord.Count; n++) texts.Append($"{{{n}}}{{Spell {n}}}\n");
        for (var c = 0; c < 16; c++) texts.Append($"{{{500 + c}}}{{School {c}}}{{{8000 + c}}}{{About {c}}}{{{9000 + c}}}{{Master of {c}}}\n");
        for (var n = 0; n < 80; n++) texts.Append($"{{{700 + n}}}{{Does {n}}}\n");
        var components = Mes(string.Concat(Enumerable.Range(10, 25).Select(n => $"{{{n}}}{{type {n}}}\n")));
        var load = SpellData.Create(components, Mes(texts.ToString()), Mes(string.Concat(list.Select(p => $"{{{p.Key}}}{{{p.Value}}}\n"))),
            SpellRecordMode.LikeOriginal);
        Assert.AreEqual(SpellDataStatus.Read, load.Status, $"{load.Refusal}");
        return load.Data!;
    }

    [TestMethod]
    public void The_page_shows_a_college_its_ranks_and_the_hints_from_the_data_or_the_project()
    {
        using var session = Hero();
        var plain = AreaSpellsPage.Build(session, null, 3)!;
        Assert.AreEqual((16, 3, 0, 0, 5, false, false, false), (plain.Colleges.Count, plain.College, plain.Rank, plain.FloorRank, plain.UnspentPoints,
            plain.EditorOpen, plain.CanLearn, plain.CanUnlearn));
        Assert.AreEqual(("College 4", "College 4 spell 1", 15), (plain.Colleges[3].Name, plain.Spells[0].Name, plain.Spells[0].Spell));
        CollectionAssert.AreEqual(new[] { 1, 1, 5, 10, 15 }, plain.Spells.Select(s => s.MinimumLevel).ToArray(), "SB4 minimum levels");
        CollectionAssert.AreEqual(new[] { 6, 9, 12, 15, 18 }, plain.Spells.Select(s => s.MinimumWillpower).ToArray(), "the default willpower minima");
        CollectionAssert.AreEqual(new[] { AreaSpellState.Next, AreaSpellState.Locked, AreaSpellState.Locked, AreaSpellState.Locked, AreaSpellState.Locked },
            plain.Spells.Select(s => s.State).ToArray());
        Assert.AreEqual(("1. College 4 spell 1 (next) | minimum level 1", "College 4 spell 1\nMinimum willpower: 6"),
            (AreaSpellsPage.Line(plain.Spells[0]), AreaSpellsPage.Hint(plain.Spells[0])), "no cost without the data");
        Assert.AreEqual((plain.Spells[0], (AreaSpellRow?)null), (plain.NextSpell, plain.HighestSpell));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaSpellsPage.Build(session, null, 16));

        var data = Data();
        var named = AreaSpellsPage.Build(session, data, 0)!;
        Assert.AreEqual(("School 0", "About 0", false), (named.Colleges[0].Name, named.Colleges[0].Description, named.Colleges[0].Mastered));
        Assert.AreEqual("School 0\nAbout 0", AreaSpellsPage.Hint(named.Colleges[0]));
        Assert.AreEqual("Spell 0\nMinimum willpower: 6\nFatigue cost: 4  (2 / second)\nDoes 0", AreaSpellsPage.Hint(named.Spells[0]));
        Assert.AreEqual("Spell 1\nMinimum willpower: 9\nFatigue cost: 6  (3 / 5 seconds)\nDoes 1", AreaSpellsPage.Hint(named.Spells[1]),
            "the minimum the session's pool checks (no rules loaded here: the defaults), not the record's IQ: 7");
        Assert.AreEqual("Spell 2\nMinimum willpower: 12\nFatigue cost: 9\nDoes 2", AreaSpellsPage.Hint(named.Spells[2]), "not maintained");

        // A dwarf pays twice the cost and the maintenance (0x4B1660, 0x4B16C0).
        using var dwarf = Hero(race: CharacterRace.Dwarf);
        Assert.AreEqual((8, 4, 1), (AreaSpellsPage.Build(dwarf, data, 0)!.Spells[0].Cost, AreaSpellsPage.Build(dwarf, data, 0)!.Spells[0].MaintainCost,
            AreaSpellsPage.Build(dwarf, data, 0)!.Spells[0].MaintainPeriod));
        using var heroless = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1).Owner!;
        Assert.IsNull(AreaSpellsPage.Build(heroless, data, 0));
    }

    [TestMethod]
    public void Plus_and_minus_learn_and_unlearn_through_the_pool_and_name_the_failing_check()
    {
        var data = Data();
        using var session = Hero();
        Assert.AreEqual(GameSessionFailure.HeroEditorClosed, AreaSpellsPage.Learn(session, 0, data).Result.Failure, "a closed editor refuses");
        session.OpenHeroEditor();
        var learned = AreaSpellsPage.Learn(session, 0, data);
        Assert.AreEqual((true, "learned Spell 0"), (learned.Changed, learned.Message));
        var page = AreaSpellsPage.Build(session, data, 0)!;
        Assert.AreEqual((1, 0, 4, true, true), (page.Rank, page.FloorRank, page.UnspentPoints, page.CanLearn, page.CanUnlearn));
        Assert.AreEqual((AreaSpellState.Known, AreaSpellState.Next), (page.Spells[0].State, page.Spells[1].State));
        Assert.AreEqual((page.Spells[1], page.Spells[0]), (page.NextSpell, page.HighestSpell), "+ names the next rank, − the highest known");
        // Rank 2 needs willpower 9 (the session's default minima): willpower 8 fails.
        Assert.AreEqual("refused: raise Willpower first", AreaSpellsPage.Learn(session, 0, data).Message);
        var unlearned = AreaSpellsPage.Unlearn(session, 0, data);
        Assert.AreEqual((true, "unlearned Spell 0"), (unlearned.Changed, unlearned.Message));
        Assert.AreEqual(("refused: a spell learned before the sheet opened cannot be unlearned", false),
            (AreaSpellsPage.Unlearn(session, 0, data).Message, AreaSpellsPage.Build(session, data, 0)!.CanUnlearn));
        // A rank learned before the editor opened is its floor.
        Assert.IsTrue(AreaSpellsPage.Learn(session, 5, data).Changed);
        session.CloseHeroEditor(); session.OpenHeroEditor();
        var reopened = AreaSpellsPage.Build(session, data, 5)!;
        Assert.AreEqual((1, 1, false), (reopened.Rank, reopened.FloorRank, reopened.CanUnlearn));

        using var young = Hero(willpower: 20);
        young.OpenHeroEditor();
        Assert.IsTrue(AreaSpellsPage.Learn(young, 0, null).Changed && AreaSpellsPage.Learn(young, 0, null).Changed);
        Assert.AreEqual("refused: the character level is too low for this spell", AreaSpellsPage.Learn(young, 0, null).Message, "rank 3 needs level 5");
        using var dull = Hero(intelligence: 4);
        dull.OpenHeroEditor();
        Assert.AreEqual("refused: raise Intelligence first", AreaSpellsPage.Learn(dull, 0, null).Message);
        using var poor = Hero(unspent: 0);
        poor.OpenHeroEditor();
        Assert.AreEqual("refused: not enough unspent points", AreaSpellsPage.Learn(poor, 0, null).Message, "the points are tested first");
    }

    [TestMethod]
    public void A_mastered_college_shows_the_mastery_description_and_the_mastery_costs()
    {
        var data = Data();
        using var session = Hero(unspent: 10, willpower: 18, level: 15);
        session.OpenHeroEditor();
        for (var rank = 0; rank < 5; rank++) Assert.IsTrue(AreaSpellsPage.Learn(session, 0, data).Changed, $"rank {rank + 1}");
        var full = AreaSpellsPage.Build(session, data, 0)!;
        Assert.AreEqual((5, false, (AreaSpellRow?)null), (full.Rank, full.CanLearn, full.NextSpell));
        Assert.AreEqual(GameSessionFailure.None, session.MasterHeroCollege(session.Version, 0).Failure);
        var mastered = AreaSpellsPage.Build(session, data, 0)!;
        Assert.AreEqual((true, "Master of 0"), (mastered.Colleges[0].Mastered, mastered.Colleges[0].Description));
        Assert.AreEqual("About 1", mastered.Colleges[1].Description);
        // The mastered college halves the cost (toward zero) and doubles the maintenance period.
        Assert.AreEqual((2, 2, 2), (mastered.Spells[0].Cost, mastered.Spells[0].MaintainCost, mastered.Spells[0].MaintainPeriod));
        Assert.AreEqual("Spell 0\nMinimum willpower: 6\nFatigue cost: 2  (2 / 2 seconds)\nDoes 0", AreaSpellsPage.Hint(mastered.Spells[0]));
    }
}
