using Arcanum.Simulation.Character;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The spell book in the session (ARC-MAGIC-004). The half-elf fixture hero has 5 unspent points, every stat 8 (willpower 9 where
// it learns a second rank) and level 1.
[TestClass]
public sealed class SpellBookStateTests
{
    private static SessionNpcAdvancement Mage(string entity = "mage", int fire = 5, int mastery = SpellBookRules.NoMastery) => new(entity,
        new(CharacterRace.Human, false, [.. Enumerable.Repeat(8, 8)], 5, 0, 15, 0, [.. new int[12]], [.. new int[4]],
            [.. new int[16].Select((_, i) => i == 4 ? fire : 0)], [.. new int[8]], 1, 2, 0, 0, mastery), 0);

    [TestMethod]
    public void TheHeroLearnsAndUnlearnsSpellRanksInTheEditor()
    {
        using var owner = CreateWith(HalfElf([8, 8, 8, 8, 8, 8, 9, 8, 0, 0]));
        Assert.AreEqual(SpellBook.Empty, owner.QueryHeroSpellBook());
        owner.OpenHeroEditor();
        var learned = owner.SpendHeroPoint(owner.Version, AdvancementTrack.College, 4);
        Assert.AreEqual((GameSessionDisposition.Changed, 1, 1), (learned.Disposition, learned.Change!.Value, learned.Change.Points));
        Assert.AreEqual((1, 4, 1), (owner.QueryHeroSpellBook()!.Ranks[4], owner.QueryHero()!.UnspentPoints, owner.QueryHero()!.Stats[8]),
            "the rank, one character point, one magick point");
        var second = owner.SpendHeroPoint(owner.Version, AdvancementTrack.College, 4);
        Assert.AreEqual((GameSessionDisposition.Changed, 2), (second.Disposition, owner.QueryHeroSpellBook()!.Ranks[4]));
        var third = owner.SpendHeroPoint(owner.Version, AdvancementTrack.College, 4);
        Assert.AreEqual((GameSessionFailure.PointRefused, PointChangeStatus.LevelTooLow), (third.Failure, third.Change!.Status), "rank 3 needs level 5");

        Assert.AreEqual(GameSessionDisposition.Changed, owner.RefundHeroPoint(owner.Version, AdvancementTrack.College, 4).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RefundHeroPoint(owner.Version, AdvancementTrack.College, 4).Disposition);
        Assert.AreEqual(PointChangeStatus.AtFloor, owner.RefundHeroPoint(owner.Version, AdvancementTrack.College, 4).Change!.Status);
        Assert.AreEqual((0, 5, 0), (owner.QueryHeroSpellBook()!.Ranks[4], owner.QueryHero()!.UnspentPoints, owner.QueryHero()!.Stats[8]));

        Spent(owner, AdvancementTrack.College, 4);
        owner.CloseHeroEditor();
        owner.OpenHeroEditor();
        Assert.AreEqual(PointChangeStatus.AtFloor, owner.RefundHeroPoint(owner.Version, AdvancementTrack.College, 4).Change!.Status,
            "learned before this opening: kept for good");
    }

    [TestMethod]
    public void AKnownRankHoldsWillpowerAndIntelligenceInTheEditor()
    {
        using var owner = CreateWith(HalfElf());
        owner.OpenHeroEditor();
        Spent(owner, AdvancementTrack.Stat, (int)CharacterStat.Willpower);
        Spent(owner, AdvancementTrack.College, 0);
        Spent(owner, AdvancementTrack.College, 0);
        var refused = owner.RefundHeroPoint(owner.Version, AdvancementTrack.Stat, (int)CharacterStat.Willpower);
        Assert.AreEqual((PointChangeStatus.SpellNeedsStat, (CharacterStat?)CharacterStat.Willpower), (refused.Change!.Status, refused.Change.Stat),
            "rank 2 needs willpower 9");
    }

    private static void Spent(GameSessionOwner owner, AdvancementTrack track, int index) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SpendHeroPoint(owner.Version, track, index).Disposition, $"{track} {index}");

    [TestMethod]
    public void MasteryIsGivenOnceAndSaved()
    {
        var full = HalfElf() with { Advancement = SessionHeroAdvancement.None with { Colleges = SessionHeroAdvancement.None.Colleges.SetItem(4, 5) } };
        using var owner = CreateWith(full);
        var missing = owner.MasterHeroCollege(owner.Version, 5);
        Assert.AreEqual((GameSessionFailure.SpellBookRefused, (SpellBookStatus?)SpellBookStatus.RanksMissing, 0L),
            (missing.Failure, missing.Status, owner.Version.Revision));
        Assert.AreEqual(GameSessionFailure.InvalidSpellBook, owner.MasterHeroCollege(owner.Version, 16).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.MasterHeroCollege(new(owner.Identity, 9), 4).Failure);
        var mastered = owner.MasterHeroCollege(owner.Version, 4);
        Assert.AreEqual((GameSessionDisposition.Changed, 1L, 4), (mastered.Disposition, owner.Version.Revision, owner.QueryHeroSpellBook()!.MasteredCollege));
        Assert.AreEqual(SpellBookStatus.AlreadyMastered, owner.MasterHeroCollege(owner.Version, 4).Status);

        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"mastery\":4");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(4, restored.QueryHeroSpellBook()!.MasteredCollege);

        using var none = Create();
        Assert.IsNull(none.QueryHeroSpellBook());
        Assert.AreEqual(GameSessionFailure.SpellBookUnavailable, none.MasterHeroCollege(none.Version, 4).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidHero, GameSessionOwner.Create(Descriptor() with
        {
            Hero = full with { Advancement = full.Advancement! with { MasteredCollege = 16 } },
        }).Failure);
    }

    [TestMethod]
    public void AnNpcsMasteryIsKeptOnItsAdvancementRecord()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryNpcSpellBook("mage"));
        Assert.AreEqual(GameSessionFailure.SpellBookRefused, owner.MasterNpcCollege(owner.Version, Mage(fire: 4), 4).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAdvancement, owner.MasterNpcCollege(owner.Version, Mage(mastery: 20), 4).Failure);
        var mastered = owner.MasterNpcCollege(owner.Version, Mage(), 4);
        Assert.AreEqual((GameSessionDisposition.Changed, 4), (mastered.Disposition, owner.QueryNpcSpellBook("mage")!.MasteredCollege));
        Assert.AreEqual(SpellBookStatus.AlreadyMastered, owner.MasterNpcCollege(owner.Version, Mage(), 4).Status, "the saved record is read");
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(new SpellBook(SpellBook.Empty.Ranks.SetItem(4, 5), 4), restored.QueryNpcSpellBook("mage"));
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryNpcSpellBook("bad id"));
    }
}
