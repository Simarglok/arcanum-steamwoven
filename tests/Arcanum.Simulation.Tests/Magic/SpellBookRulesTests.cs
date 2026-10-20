using System.Collections.Immutable;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Character.AutoLevelRulesTests;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-004: the spell book (ledger SB1–SB10). Books and sheets are invented.
[TestClass]
public sealed class SpellBookRulesTests
{
    private static SpellBook With(int college, int rank, int mastery = SpellBookRules.NoMastery) =>
        new(SpellBook.Empty.Ranks.SetItem(college, rank), mastery);

    [TestMethod]
    public void RanksAreLearnedInOrderWithLevelIntelligenceAndWillpower()
    {
        var learned = SpellBookRules.Learn(SpellBook.Empty, 10, force: false, level: 1, intelligence: 5, willpower: 6, minimumWillpower: 6);
        Assert.AreEqual((SpellBookStatus.Done, true, 1, 1), (learned.Status, learned.Changed, learned.Book.Ranks[2], learned.MagickPoints));
        Assert.AreEqual(SpellBookStatus.WrongRank, SpellBookRules.Learn(SpellBook.Empty, 11, false, 50, 20, 20, 0).Status, "rank 2 before rank 1");
        Assert.AreEqual(SpellBookStatus.WrongRank, SpellBookRules.Learn(With(2, 1), 10, false, 50, 20, 20, 0).Status, "already known");
        Assert.AreEqual(SpellBookStatus.LevelTooLow, SpellBookRules.Learn(With(2, 2), 12, false, 4, 20, 20, 0).Status, "rank 3 needs level 5");
        Assert.AreEqual(SpellBookStatus.IntelligenceTooLow, SpellBookRules.Learn(SpellBook.Empty, 10, false, 1, 4, 20, 0).Status);
        Assert.AreEqual(SpellBookStatus.WillpowerTooLow, SpellBookRules.Learn(SpellBook.Empty, 10, false, 1, 20, 6, 7).Status);
        Assert.AreEqual(SpellBookStatus.LevelTooLow, SpellBookRules.Learn(With(2, 4), 14, false, 14, 4, 0, 99).Status,
            "the level is tested before intelligence and willpower");
        CollectionAssert.AreEqual(new[] { 1, 1, 5, 10, 15 }, Enumerable.Range(0, 5).Select(SpellBookRules.MinimumLevel).ToArray());
    }

    [TestMethod]
    public void AForcedLearnSkipsTheChecksAndAddsTheMissingRanks()
    {
        var forced = SpellBookRules.Learn(With(3, 1), 18, force: true, level: 0, intelligence: 1, willpower: 1, minimumWillpower: 99);
        Assert.AreEqual((SpellBookStatus.Done, true, 4, 3), (forced.Status, forced.Changed, forced.Book.Ranks[3], forced.MagickPoints));
        var known = SpellBookRules.Learn(With(3, 4), 16, true, 0, 1, 1, 99);
        Assert.AreEqual((SpellBookStatus.Done, false, 0), (known.Status, known.Changed, known.MagickPoints), "a known rank: success, no change");
    }

    [TestMethod]
    public void OnlyTheHighestRankIsUnlearnedAndMasteryStays()
    {
        var book = With(7, 5, mastery: 7);
        var unlearned = SpellBookRules.Unlearn(book, 39);
        Assert.AreEqual((SpellBookStatus.Done, 4, -1, 7), (unlearned.Status, unlearned.Book.Ranks[7], unlearned.MagickPoints, unlearned.Book.MasteredCollege));
        Assert.AreEqual(SpellBookStatus.WrongRank, SpellBookRules.Unlearn(book, 38).Status);
        Assert.AreEqual(SpellBookStatus.WrongRank, SpellBookRules.Unlearn(SpellBook.Empty, 0).Status);
        Assert.IsTrue(SpellBookRules.IsKnown(book, 35) && SpellBookRules.IsKnown(book, 39));
        Assert.IsFalse(SpellBookRules.IsKnown(unlearned.Book, 39) || SpellBookRules.IsKnown(book, 40) || SpellBookRules.IsKnown(book, 80));
    }

    [TestMethod]
    public void MasteryIsOnceAndNeedsAllFiveRanks()
    {
        Assert.AreEqual(SpellBookStatus.RanksMissing, SpellBookRules.CanMaster(With(4, 4), 4));
        var mastered = SpellBookRules.Master(With(4, 5), 4);
        Assert.AreEqual((SpellBookStatus.Done, true, 4), (mastered.Status, mastered.Changed, mastered.Book.MasteredCollege));
        var full = mastered.Book with { Ranks = mastered.Book.Ranks.SetItem(9, 5) };
        Assert.AreEqual((SpellBookStatus.AlreadyMastered, false, 4), (SpellBookRules.Master(full, 9).Status, SpellBookRules.Master(full, 9).Changed,
            SpellBookRules.Master(full, 9).Book.MasteredCollege));
        Assert.AreEqual(SpellBookStatus.Invalid, SpellBookRules.CanMaster(With(4, 5), 16));
    }

    [TestMethod]
    public void BooksOutsideTheirRangesAndSpellsOutsideTheCollegesAreRefused()
    {
        Assert.AreEqual(SpellBookStatus.Invalid, SpellBookRules.Learn(SpellBook.Empty, 80, true, 50, 20, 20, 0).Status, "SB9: past the sixteen ranks");
        Assert.AreEqual(SpellBookStatus.Invalid, SpellBookRules.Learn(SpellBook.Empty, -1, false, 50, 20, 20, 0).Status);
        Assert.AreEqual(SpellBookStatus.Invalid, SpellBookRules.Unlearn(With(0, 1), 85).Status);
        Assert.IsFalse(SpellBookRules.IsValid(With(0, 6)) || SpellBookRules.IsValid(With(0, 1, mastery: 16)) || SpellBookRules.IsValid(new([1, 2])));
        Assert.AreEqual(SpellBookStatus.Invalid, SpellBookRules.Learn(With(0, 6), 0, false, 50, 20, 20, 0).Status);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SpellBookRules.MinimumLevel(80));
    }

    [TestMethod]
    public void ACrittersBookComesFromItsSpellAndTechValues()
    {
        int[] fields = [.. Enumerable.Repeat(0, 25)];
        fields[3] = 5; fields[16] = 3; fields[17] = 7;
        Assert.AreEqual(new SpellBook(SpellBook.Empty.Ranks.SetItem(3, 5), 3), SpellBookRules.FromFields(fields));
        fields[16] = -1;
        Assert.AreEqual(-1, SpellBookRules.FromFields(fields)!.MasteredCollege);
        Assert.IsNull(SpellBookRules.FromFields(fields.Take(16).ToArray()));
        fields[0] = 6;
        Assert.IsNull(SpellBookRules.FromFields(fields));
        Assert.IsNull(SpellBookRules.FromFields(null));
    }

    [TestMethod]
    public void TheStatChecksReadEveryKnownRank()
    {
        var ranks = SpellBook.Empty.Ranks.SetItem(1, 3);
        Assert.IsTrue(SpellBookRules.RanksHold(ranks, AutoLevelRules.DefaultSpellMinimumWillpower, 12), "ranks 1..3 need 6, 9, 12");
        Assert.IsFalse(SpellBookRules.RanksHold(ranks, AutoLevelRules.DefaultSpellMinimumWillpower, 11));
        Assert.IsTrue(SpellBookRules.RanksHold(SpellBook.Empty.Ranks, _ => 99, 0), "nothing known holds anything");
        Assert.ThrowsExactly<ArgumentException>(() => SpellBookRules.RanksHold([1], _ => 0, 0));
    }

    [TestMethod]
    public void TheSpellLaneLearnsThroughThePoolAndKeepsTheFloor()
    {
        var lane = new SpellRankLane(AutoLevelRules.DefaultSpellWillpower);
        var floor = Sheet(unspent: 2);
        Assert.AreEqual(1, lane.Cost(floor, 5));
        var learned = CharacterPointRules.Spend(floor, AdvancementModifiers.None, AdvancementTrack.College, 5, AutoLevelRules.DefaultSpellWillpower, lane);
        Assert.AreEqual((PointChangeStatus.Changed, 1, 1, 1, 1), (learned.Status, learned.Points, learned.Value, learned.Sheet.UnspentPoints,
            learned.Sheet.MagickPoints));
        var back = CharacterPointRules.Refund(learned.Sheet, floor, AdvancementModifiers.None, AdvancementTrack.College, 5,
            SkillRefundClick.UntilLevelChanges, AutoLevelRules.DefaultSpellWillpower, lane);
        Assert.AreEqual(floor, back.Sheet, "unlearned: the rank, the magick point and the character point come back");
        Assert.AreEqual(PointChangeStatus.AtFloor, CharacterPointRules.Refund(floor, floor, AdvancementModifiers.None, AdvancementTrack.College, 5,
            SkillRefundClick.UntilLevelChanges, AutoLevelRules.DefaultSpellWillpower, lane).Status);

        PointChange Learn(AdvancementSheet sheet, AdvancementModifiers? modifiers = null) =>
            CharacterPointRules.Spend(sheet, modifiers ?? AdvancementModifiers.None, AdvancementTrack.College, 5, AutoLevelRules.DefaultSpellWillpower, lane);
        Assert.AreEqual(PointChangeStatus.NotEnoughPoints, Learn(Sheet(unspent: 0) with { Level = 0 }).Status, "the points before the learn's checks");
        Assert.AreEqual(PointChangeStatus.LevelTooLow, Learn(Sheet(level: 0)).Status);
        var dull = Learn(Sheet(), AdvancementModifiers.None with { Stats = [new(CharacterStat.Intelligence, StatModifierOperator.Add, -4)] });
        Assert.AreEqual((PointChangeStatus.StatTooLow, (CharacterStat?)CharacterStat.Intelligence), (dull.Status, dull.Stat), "the effective value");
        var weak = Learn(Sheet() with { Stats = Sheet().Stats.SetItem(6, 5) });
        Assert.AreEqual((PointChangeStatus.StatTooLow, (CharacterStat?)CharacterStat.Willpower), (weak.Status, weak.Stat));
        Assert.AreEqual(PointChangeStatus.AtMaximum, Learn(Sheet() with { Colleges = Sheet().Colleges.SetItem(5, 5) }).Status);
        var top = Sheet() with { MagickPoints = 210 };
        Assert.AreEqual((PointChangeStatus.Changed, 210), (Learn(top).Status, Learn(top).Sheet.MagickPoints), "the magick points stop at 210");
        var mastered = Sheet() with { Colleges = Sheet().Colleges.SetItem(5, 5), MasteredCollege = 5 };
        var kept = lane.Lower(mastered, AdvancementModifiers.None, 5);
        Assert.AreEqual((PointChangeStatus.Changed, 4, 5), (kept.Status, kept.Sheet.Colleges[5], kept.Sheet.MasteredCollege), "mastery is for life");
        Assert.AreEqual(PointChangeStatus.AtMinimum, lane.Lower(Sheet(), AdvancementModifiers.None, 5).Status);
        Assert.ThrowsExactly<ArgumentException>(() => new SpellRankLane([1, 2]));
    }

    [TestMethod]
    public void AutoLevelLearnsThroughTheSameRuleAndKeepsTheMastery()
    {
        var sheet = Sheet(unspent: 3, level: 5) with { MasteredCollege = 2, Stats = Sheet().Stats.SetItem(6, 20) };
        var outcome = AutoLevelRules.Apply(sheet, AdvancementModifiers.None, AutoLevelRules.Parse("fire 3,"u8).Scheme, null, false,
            AutoLevelRules.DefaultSpellWillpower);
        Assert.AreEqual((3, 3, 0, 2), (outcome.Sheet.Colleges[4], outcome.Sheet.MagickPoints, outcome.Sheet.UnspentPoints, outcome.Sheet.MasteredCollege));
        Assert.IsFalse(AutoLevelRules.IsValid(sheet with { MasteredCollege = 16 }));
        Assert.AreNotEqual(sheet, sheet with { MasteredCollege = 3 }, "equality reads the mastery");
    }
}
