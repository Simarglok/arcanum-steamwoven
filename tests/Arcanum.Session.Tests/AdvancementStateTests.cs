using System.Collections.Immutable;
using System.Text;
using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's auto-level scheme and the followers' level updates (ARC-WORLD-036). Schemes are invented; the half-elf fixture
// hero has ST 8, DX 9 (effect), CN 7, IN 8, WP 8, 5 unspent points at level 1 and 30 hit points; the experience tables are
// ExperienceStateTests' (level 2 1010, level 3 2040, level 5 4160).
[TestClass]
public sealed class AdvancementStateTests
{
    private static int T(int level) => 1000 * (level - 1) + 10 * (level - 1) * (level - 1);

    private static AutoLevelScheme Scheme(string text) => AutoLevelRules.Parse(Encoding.ASCII.GetBytes(text)).Scheme!;

    internal static SessionAutoLevelRules Rules() => SessionAutoLevelRules.Create(new Dictionary<int, AutoLevelScheme>
    {
        [60] = Scheme("st 10, melee 4, maxhps 40"),
        [61] = Scheme("fire 3"),
        [62] = Scheme("dx 10"),
        [70] = Scheme("cn 9"),
    }, AutoLevelRules.DefaultSpellWillpower)!;

    private static GameSessionOwner Owner(int scheme, bool autoLevel = true, GameSessionLimits? limits = null)
    {
        var result = GameSessionOwner.Create(Descriptor(limits) with
        {
            Hero = HalfElf() with { Advancement = SessionHeroAdvancement.WithScheme(scheme) },
            Experience = ExperienceStateTests.Rules(), AutoLevel = autoLevel ? Rules() : null,
        });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    // A follower like Virgil: level 1, one hit point bought, nothing to spend, scheme 62.
    private static SessionNpcAdvancement Follower(string entity = "virgil", int level = 1) => new(entity, new(CharacterRace.Human, false,
        [.. Enumerable.Repeat(8, 8)], 0, 0, level, 0, [.. new int[12]], [.. new int[4]], [.. new int[16]], [.. new int[8]], 1, 2, 0, 0), 62);

    [TestMethod]
    public void ANewHeroSpendsItsStartingPointsOnItsScheme()
    {
        using var owner = Owner(60);
        var hero = owner.QueryHero()!;
        // ST 9 and 10 (2 points), one melee point (DX 9 allows it), then two hit points (34 → 38 → 42) with the last two points.
        Assert.AreEqual((10, 1, 2, 0), (hero.Stats[0], hero.Advancement.BasicSkills[2], hero.Advancement.HitPointPoints, hero.UnspentPoints));
        Assert.AreEqual(10, owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Strength));
        // Hit points: 4 × 2 + 8 + 2 × (10 + 1) + 4 = 42.
        Assert.AreEqual(42, owner.QueryHeroLife()!.Status.MaxHitPoints);
        Assert.AreEqual(0, owner.Version.Revision);

        using var plain = Owner(60, autoLevel: false);
        Assert.AreEqual((8, 5), (plain.QueryHero()!.Stats[0], plain.QueryHero()!.UnspentPoints));
        using var none = Owner(0);
        Assert.AreEqual(5, none.QueryHero()!.UnspentPoints);
    }

    [TestMethod]
    public void ASchemeRunPastThePoolBoundBuysNothing()
    {
        var rules = SessionAutoLevelRules.Create(new Dictionary<int, AutoLevelScheme> { [63] = Scheme("maxhps 9999"), [64] = Scheme("st 10, maxhps 9999") },
            AutoLevelRules.DefaultSpellWillpower);
        GameSessionOwner Start(int scheme, int points) => GameSessionOwner.Create(Descriptor() with
        {
            Hero = HalfElf() with { Advancement = SessionHeroAdvancement.WithScheme(scheme) with { HitPointPoints = points } },
            Experience = ExperienceStateTests.Rules(), AutoLevel = rules,
        }).Owner!;
        // A run that ends exactly at the bound is kept.
        using (var edge = Start(63, SessionAdvancementLimits.MaxHeroPoolPoints - 5))
            Assert.AreEqual((SessionAdvancementLimits.MaxHeroPoolPoints, 0), (edge.QueryHero()!.Advancement.HitPointPoints, edge.QueryHero()!.UnspentPoints));
        // A run past it keeps none of its purchases, not only the pools.
        using (var whole = Start(64, SessionAdvancementLimits.MaxHeroPoolPoints))
            Assert.AreEqual((8, SessionAdvancementLimits.MaxHeroPoolPoints, 5), (whole.QueryHero()!.Stats[0], whole.QueryHero()!.Advancement.HitPointPoints,
                whole.QueryHero()!.UnspentPoints));
        var full = SessionHeroAdvancement.WithScheme(63) with { HitPointPoints = SessionAdvancementLimits.MaxHeroPoolPoints };
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() with { Advancement = full }, Experience = ExperienceStateTests.Rules(), AutoLevel = rules });
        using var owner = result.Owner!;
        // The creation run would buy a 101st point: it buys nothing, the points stay, and the session restores what it saves.
        Assert.AreEqual((SessionAdvancementLimits.MaxHeroPoolPoints, 5), (owner.QueryHero()!.Advancement.HitPointPoints, owner.QueryHero()!.UnspentPoints));
        var award = owner.AwardHeroExperience(owner.Version, T(2));
        Assert.AreEqual((GameSessionDisposition.Changed, (IReadOnlyList<AdvancementPurchase>?)null), (award.Disposition, award.Award!.Purchases));
        Assert.AreEqual((SessionAdvancementLimits.MaxHeroPoolPoints, 6), (owner.QueryHero()!.Advancement.HitPointPoints, owner.QueryHero()!.UnspentPoints));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text), RestoreOptions() with { AutoLevel = rules, Experience = ExperienceStateTests.Rules() });
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
    }

    [TestMethod]
    public void EachLevelWrittenRunsTheSchemeWithThatLevel()
    {
        using var owner = Owner(61);
        // At level 1 the scheme buys fire 1, willpower 9 and fire 2 (3 of 5 points); rank 3 needs level 5.
        var start = owner.QueryHero()!;
        Assert.AreEqual((2, 9, 2), (start.Advancement.Colleges[4], start.Stats[6], start.UnspentPoints));
        // One award to level 5: levels 2..4 add a point each (still no rank 3), level 5 adds two and rank 3 needs WP 12 at level 5.
        var award = owner.AwardHeroExperience(owner.Version, T(5));
        Assert.AreEqual(GameSessionDisposition.Changed, award.Disposition);
        var hero = owner.QueryHero()!;
        Assert.AreEqual((5, 3, 12), (hero.Level, hero.Advancement.Colleges[4], hero.Stats[6]));
        Assert.AreEqual(2 + 5 - 3 - 1, hero.UnspentPoints);
        CollectionAssert.AreEqual(new AdvancementPurchase[]
        {
            new(AdvancementTrack.Stat, 6, 10), new(AdvancementTrack.Stat, 6, 11), new(AdvancementTrack.Stat, 6, 12), new(AdvancementTrack.College, 4, 3),
        }, award.Award!.Purchases!.ToArray());
        Assert.AreEqual(3, owner.QueryHero()!.Stats[8], "magick points: one per spell");

        using var without = Owner(61, autoLevel: false);
        var plain = without.AwardHeroExperience(without.Version, T(2));
        Assert.IsNull(plain.Award!.Purchases);
        Assert.AreEqual(6, without.QueryHero()!.UnspentPoints);
    }

    [TestMethod]
    public void FollowersTakeTheLevelStepAndRunTheirScheme()
    {
        using var owner = Owner(0);
        var revision = owner.Version.Revision;
        var up = owner.LevelFollower(owner.Version, Follower(), AdvancementModifiers.None, new(1, 2));
        Assert.AreEqual((GameSessionDisposition.Changed, new FollowerLevelUp(2, 1)), (up.Disposition, up.Level));
        // One point: scheme 62 buys dexterity 9, cannot pay for 10 and stops, so rule 70 does not run.
        Assert.AreEqual((2, 9, 8, 0, false), (up.Record!.Sheet.Level, up.Record.Sheet.Stats[1], up.Record.Sheet.Stats[2], up.Record.Sheet.UnspentPoints,
            up.Scheme!.Completed));
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        Assert.AreEqual(up.Record, owner.QueryNpcAdvancement("virgil"));

        // The record wins over the loaded values; reaching level 5 gives 1 + 1 + 2 points: dexterity 10 completes the scheme and
        // rule 70 buys constitution 9.
        var five = owner.LevelFollower(owner.Version, Follower(), AdvancementModifiers.None, new(2, 5));
        Assert.AreEqual((5, 10, 9, 2, true), (five.Record!.Sheet.Level, five.Record.Sheet.Stats[1], five.Record.Sheet.Stats[2],
            five.Record.Sheet.UnspentPoints, five.Scheme!.Completed));
        // A step that does not raise the follower changes nothing and runs no scheme.
        var same = owner.LevelFollower(owner.Version, Follower(), AdvancementModifiers.None, new(4, 5));
        Assert.AreEqual((GameSessionDisposition.NoChange, (AutoLevelOutcome?)null), (same.Disposition, same.Scheme));
        // An effect that lowers dexterity: the stat loop counts from the base value, so the one point buys base 9.
        var lowered = AdvancementModifiers.None with { Stats = [new(CharacterStat.Dexterity, StatModifierOperator.Add, -1)] };
        var other = owner.LevelFollower(owner.Version, Follower("cutthroat"), lowered, new(1, 2));
        Assert.AreEqual((9, 0), (other.Record!.Sheet.Stats[1], other.Record.Sheet.UnspentPoints));
    }

    [TestMethod]
    public void FollowerUpdatesAreRefusedWithoutDataOrValidInput()
    {
        using var plain = Owner(0, autoLevel: false);
        Assert.AreEqual(GameSessionFailure.AdvancementUnavailable,
            plain.LevelFollower(plain.Version, Follower(), AdvancementModifiers.None, new(1, 2)).Failure);
        using var owner = Owner(0, limits: new(maxAdvancementRecords: 1));
        void Refused(SessionNpcAdvancement npc, AdvancementModifiers modifiers, FollowerLevelStep step, GameSessionFailure failure) =>
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (owner.LevelFollower(owner.Version, npc, modifiers, step).Disposition,
                owner.LevelFollower(owner.Version, npc, modifiers, step).Failure));
        Refused(Follower("bad id"), AdvancementModifiers.None, new(1, 2), GameSessionFailure.InvalidAdvancement);
        Refused(Follower() with { AutoLevelScheme = 200 }, AdvancementModifiers.None, new(1, 2), GameSessionFailure.InvalidAdvancement);
        Refused(Follower() with { Sheet = Follower().Sheet with { UnspentPoints = 57 } }, AdvancementModifiers.None, new(1, 2),
            GameSessionFailure.InvalidAdvancement);
        Refused(Follower(), AdvancementModifiers.None with { PoisonLevel = -1 }, new(1, 2), GameSessionFailure.InvalidAdvancement);
        Refused(Follower(), AdvancementModifiers.None, new(1, 52), GameSessionFailure.InvalidAdvancement);
        Assert.AreEqual(GameSessionFailure.StaleVersion,
            owner.LevelFollower(new(owner.Identity, 7), Follower(), AdvancementModifiers.None, new(1, 2)).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LevelFollower(owner.Version, Follower(), AdvancementModifiers.None, new(1, 2)).Disposition);
        Refused(Follower("second"), AdvancementModifiers.None, new(1, 2), GameSessionFailure.BudgetExceeded);
        Assert.IsNull(owner.QueryNpcAdvancement("second"));
    }

    [TestMethod]
    public void AdvancementRoundTripsThroughTheSnapshot()
    {
        using var owner = Owner(60);
        owner.LevelFollower(owner.Version, Follower(), AdvancementModifiers.None, new(1, 2));
        owner.LevelFollower(owner.Version, Follower("cutthroat"), AdvancementModifiers.None, new(1, 2));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"advancement\":{\"basicSkills\":[0,0,1,0,0,0,0,0,0,0,0,0],\"techSkills\":[0,0,0,0]," +
            "\"colleges\":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],\"techDegrees\":[0,0,0,0,0,0,0,0],\"hitPointPoints\":2,\"fatiguePoints\":0,\"scheme\":60}");
        StringAssert.Contains(text, ",\"advancement\":{\"npcs\":[{\"entity\":\"cutthroat\",");
        StringAssert.Contains(text, "{\"entity\":\"virgil\",\"race\":\"Human\",\"monstrous\":false," +
            "\"stats\":[8,9,8,8,8,8,8,8],\"magickPoints\":0,\"techPoints\":0,\"level\":2,\"unspentPoints\":0,");
        using var restored = Restore(Decode(text), RestoreOptions() with { AutoLevel = Rules(), Experience = ExperienceStateTests.Rules() });
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(42, restored.QueryHeroLife()!.Status.MaxHitPoints);
        Assert.IsNotNull(restored.AutoLevel);
        Assert.AreEqual(owner.QueryNpcAdvancement("virgil"), restored.QueryNpcAdvancement("virgil"));

        void RestoreFails(string damaged, GameSessionFailure failure, GameSessionLimits? limits = null) =>
            Assert.AreEqual(failure, GameSessionOwner.Restore(Decode(damaged), RestoreOptions(limits)).Failure);
        RestoreFails(text.Replace("\"techSkills\":[0,0,0,0],\"colleges\"", "\"techSkills\":[0,0,0],\"colleges\""), GameSessionFailure.InvalidHero);
        RestoreFails(text.Replace("\"hitPointPoints\":2,\"fatiguePoints\":0,\"scheme\":60", "\"hitPointPoints\":101,\"fatiguePoints\":0,\"scheme\":60"),
            GameSessionFailure.InvalidHero);
        RestoreFails(text.Replace("\"scheme\":60}", "\"scheme\":200}"), GameSessionFailure.InvalidHero);
        RestoreFails(text.Replace("\"level\":2,\"unspentPoints\":0,", "\"level\":52,\"unspentPoints\":0,"), GameSessionFailure.InvalidAdvancement);
        RestoreFails(text, GameSessionFailure.BudgetExceeded, new(maxAdvancementRecords: 1));
        var unsorted = GameSessionSnapshotCodec.TryDecode(text.Replace("{\"entity\":\"cutthroat\"", "{\"entity\":\"zz\""));
        Assert.AreEqual((SnapshotDecodeFailure.NotCanonical, "$.advancement.npcs[1].entity"), (unsorted.Failure, unsorted.Path));
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Restore(Decode(text), RestoreOptions()).Failure);
        var decoded = GameSessionSnapshotCodec.TryDecode(text.Replace("\"monstrous\":false", "\"monstrous\":0"));
        Assert.AreEqual((SnapshotDecodeFailure.WrongType, "$.advancement.npcs[0].monstrous"), (decoded.Failure, decoded.Path));
    }

    [TestMethod]
    public void TheRulesRefuseDataOutsideTheirBounds()
    {
        var wp = AutoLevelRules.DefaultSpellWillpower;
        Assert.IsNull(SessionAutoLevelRules.Create(null, wp));
        Assert.IsNull(SessionAutoLevelRules.Create(new Dictionary<int, AutoLevelScheme>(), null));
        Assert.IsNull(SessionAutoLevelRules.Create(new Dictionary<int, AutoLevelScheme>(), wp.RemoveAt(0)));
        Assert.IsNull(SessionAutoLevelRules.Create(new Dictionary<int, AutoLevelScheme> { [0] = Scheme("st 9") }, wp));
        Assert.IsNull(SessionAutoLevelRules.Create(new Dictionary<int, AutoLevelScheme> { [200] = Scheme("st 9") }, wp));
        Assert.IsNull(SessionAutoLevelRules.Create(new Dictionary<int, AutoLevelScheme> { [5] = null! }, wp));
        var rules = SessionAutoLevelRules.Create(new Dictionary<int, AutoLevelScheme> { [5] = Scheme("st 9") }, wp)!;
        Assert.IsNotNull(rules.Scheme(5));
        Assert.IsNull(rules.Scheme(6));
        Assert.IsNull(rules.Scheme(0));
        Assert.AreEqual(GameSessionFailure.InvalidHero, GameSessionOwner.Create(Descriptor() with
        {
            Hero = HalfElf() with { Advancement = SessionHeroAdvancement.None with { Colleges = [6, .. new int[15]] } },
        }).Failure);
    }

    // ARC-SCRIPT-016: SCR action 139 sets a scheme and buys nothing until the next level step (ledger OL30).
    [TestMethod]
    public void AScriptSchemeIsStoredAndRunsOnlyAtTheNextLevel()
    {
        using var owner = Owner(0);
        var before = owner.QueryHero()!;
        var revision = owner.Version.Revision;
        var set = owner.SetAutoLevelScheme(owner.Version, SessionSchemeTarget.Hero, 60);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, (int?)60), (set.Disposition, set.Failure, set.Scheme));
        var hero = owner.QueryHero()!;
        Assert.AreEqual((60, 5, 8, revision + 1), (hero.Advancement.AutoLevelScheme, hero.UnspentPoints, hero.Stats[0], owner.Version.Revision),
            "nothing is bought at once");
        CollectionAssert.AreEqual(before.Stats.ToArray(), hero.Stats.ToArray());
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetAutoLevelScheme(owner.Version, SessionSchemeTarget.Hero, 60).Disposition);

        // The next level runs it: strength 9 and 10 among the purchases.
        var award = owner.AwardHeroExperience(owner.Version, T(2));
        Assert.IsNotNull(award.Award!.Purchases);
        Assert.AreEqual(10, owner.QueryHero()!.Stats[0]);
        Assert.IsTrue(owner.QueryHero()!.UnspentPoints < 6);
    }

    [TestMethod]
    public void ASchemeWithoutARuleBuysNothingAndKeepsThePoints()
    {
        using var owner = Owner(60);
        var start = owner.QueryHero()!;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetAutoLevelScheme(owner.Version, SessionSchemeTarget.Hero, 150).Disposition);
        var award = owner.AwardHeroExperience(owner.Version, T(2));
        Assert.IsNull(award.Award!.Purchases);
        Assert.AreEqual((150, start.UnspentPoints + 1, start.Stats[0]), (owner.QueryHero()!.Advancement.AutoLevelScheme, owner.QueryHero()!.UnspentPoints,
            owner.QueryHero()!.Stats[0]));
    }

    [TestMethod]
    public void AnNpcSchemeMakesARecordThatItsNextLevelStepRuns()
    {
        using var owner = Owner(0);
        var set = owner.SetAutoLevelScheme(owner.Version, SessionSchemeTarget.ForNpc(Follower()), 60);
        Assert.AreEqual(GameSessionDisposition.Changed, set.Disposition);
        Assert.AreEqual(Follower() with { AutoLevelScheme = 60 }, owner.QueryNpcAdvancement("virgil"), "the loaded sheet, nothing bought");
        // A loaded scheme no longer counts: the record wins. One point: scheme 60 buys strength 9 (not dexterity, scheme 62).
        var up = owner.LevelFollower(owner.Version, Follower(), AdvancementModifiers.None, new(1, 2));
        Assert.AreEqual((9, 8, 0), (up.Record!.Sheet.Stats[0], up.Record.Sheet.Stats[1], up.Record.Sheet.UnspentPoints));
        Assert.AreEqual(60, up.Record.AutoLevelScheme);
        // A scheme without a rule: the next rise buys nothing, not even rule 70, and the point stays.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetAutoLevelScheme(owner.Version, SessionSchemeTarget.ForNpc(Follower("cutthroat")), 150).Disposition);
        var plain = owner.LevelFollower(owner.Version, Follower("cutthroat"), AdvancementModifiers.None, new(1, 2));
        Assert.AreEqual((2, 1, 8, 8), (plain.Record!.Sheet.Level, plain.Record.Sheet.UnspentPoints, plain.Record.Sheet.Stats[0], plain.Record.Sheet.Stats[2]));
        // A later change keeps the levelled sheet.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetAutoLevelScheme(owner.Version, SessionSchemeTarget.ForNpc(Follower()), 0).Disposition);
        Assert.AreEqual((2, 0), (owner.QueryNpcAdvancement("virgil")!.Sheet.Level, owner.QueryNpcAdvancement("virgil")!.AutoLevelScheme));
    }

    [TestMethod]
    public void SchemeChangesAreRefusedOutsideTheSessionBounds()
    {
        using var owner = Owner(0, limits: new(maxAdvancementRecords: 1));
        var revision = owner.Version.Revision;
        void Refused(SessionSchemeTarget target, int scheme, GameSessionFailure failure) =>
            Assert.AreEqual((GameSessionDisposition.Rejected, failure, (int?)null),
                (owner.SetAutoLevelScheme(owner.Version, target, scheme).Disposition, owner.SetAutoLevelScheme(owner.Version, target, scheme).Failure,
                    owner.SetAutoLevelScheme(owner.Version, target, scheme).Scheme));
        // The original stores any number; the session keeps 0..199 (documented deviation).
        Refused(SessionSchemeTarget.Hero, 200, GameSessionFailure.InvalidAdvancement);
        Refused(SessionSchemeTarget.Hero, -1, GameSessionFailure.InvalidAdvancement);
        Refused(SessionSchemeTarget.ForNpc(Follower("bad id")), 60, GameSessionFailure.InvalidAdvancement);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.SetAutoLevelScheme(new(owner.Identity, 7), SessionSchemeTarget.Hero, 60).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetAutoLevelScheme(owner.Version, SessionSchemeTarget.ForNpc(Follower()), 60).Disposition);
        Refused(SessionSchemeTarget.ForNpc(Follower("second")), 60, GameSessionFailure.BudgetExceeded);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetAutoLevelScheme(owner.Version, SessionSchemeTarget.ForNpc(Follower("second")), 62).Disposition,
            "the loaded scheme already");
        Assert.AreEqual((0, revision + 1), (owner.QueryHero()!.Advancement.AutoLevelScheme, owner.Version.Revision));
        Assert.ThrowsExactly<ArgumentNullException>(() => SessionSchemeTarget.ForNpc(null!));

        using var heroless = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.AreEqual(GameSessionFailure.AdvancementUnavailable, heroless.SetAutoLevelScheme(heroless.Version, SessionSchemeTarget.Hero, 60).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed,
            heroless.SetAutoLevelScheme(heroless.Version, SessionSchemeTarget.ForNpc(Follower()), 60).Disposition, "an NPC needs no hero");
    }

    [TestMethod]
    public void ScriptSchemesRoundTripThroughTheSnapshot()
    {
        using var owner = Owner(0);
        owner.SetAutoLevelScheme(owner.Version, SessionSchemeTarget.Hero, 61);
        owner.SetAutoLevelScheme(owner.Version, SessionSchemeTarget.ForNpc(Follower()), 101);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text), RestoreOptions() with { AutoLevel = Rules(), Experience = ExperienceStateTests.Rules() });
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual((61, 101), (restored.QueryHero()!.Advancement.AutoLevelScheme, restored.QueryNpcAdvancement("virgil")!.AutoLevelScheme));
    }
}
