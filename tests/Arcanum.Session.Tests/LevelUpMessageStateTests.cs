using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's level-up messages in an award (ARC-UI-046, ledger OL27–OL28). The half-elf fixture hero starts at level 1 with 5 unspent
// points; the experience tables are ExperienceStateTests' and the schemes AdvancementStateTests' (61: "fire 3", invented).
[TestClass]
public sealed class LevelUpMessageStateTests
{
    private static int T(int level) => 1000 * (level - 1) + 10 * (level - 1) * (level - 1);

    private static GameSessionOwner Owner(int scheme, RuleMode rules = RuleMode.Steamwoven, int unspent = 5, bool autoLevel = true)
    {
        var result = GameSessionOwner.Create(Descriptor() with
        {
            Rules = rules,
            Hero = HalfElf() with { UnspentPoints = unspent, Advancement = SessionHeroAdvancement.WithScheme(scheme) },
            Experience = ExperienceStateTests.Rules(), AutoLevel = autoLevel ? AdvancementStateTests.Rules() : null,
        });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    [TestMethod]
    public void A_hero_without_a_scheme_gets_one_points_message_per_level()
    {
        using var owner = Owner(0);
        var award = owner.AwardHeroExperience(owner.Version, T(3)).Award!;
        CollectionAssert.AreEqual(new SessionLevelUp[] { new(2, 6, null), new(3, 7, null) }, award.LevelUps!.ToArray());
        // An award that writes no level has none.
        Assert.IsNull(owner.AwardHeroExperience(owner.Version, 1).Award!.LevelUps);
    }

    [TestMethod]
    public void The_points_past_the_cap_follow_the_rule_setting()
    {
        // 55 stored: level 2 names 56 in both settings; level 3 names 57 in the original (OL28) and the 56 held in Steamwoven (D6).
        foreach (var (mode, third) in new[] { (RuleMode.Original, 57), (RuleMode.Steamwoven, 56) })
        {
            using var owner = Owner(0, mode, unspent: 55);
            var award = owner.AwardHeroExperience(owner.Version, T(3)).Award!;
            CollectionAssert.AreEqual(new SessionLevelUp[] { new(2, 56, null), new(3, third, null) }, award.LevelUps!.ToArray(), mode.ToString());
            Assert.AreEqual(ExperienceRules.MaxUnspentPoints, owner.QueryHero()!.UnspentPoints);
        }
    }

    [TestMethod]
    public void A_scheme_names_each_levels_purchases_and_its_completion()
    {
        using var owner = Owner(61);
        var award = owner.AwardHeroExperience(owner.Version, T(5)).Award!;
        var levels = award.LevelUps!;
        CollectionAssert.AreEqual(new[] { 2, 3, 4, 5 }, levels.Select(l => l.Level).ToArray());
        Assert.IsTrue(levels.All(l => l.Scheme is { HasRule: true }), "every level carries the scheme's text, not the points");
        // Each level's purchases, in order, are the award's purchases; the rule completes at level 5 with fire 3.
        CollectionAssert.AreEqual(award.Purchases!.ToArray(), levels.SelectMany(l => l.Scheme!.Purchases).ToArray());
        Assert.IsTrue(levels[^1].Scheme!.Completed && levels[^1].Scheme!.Purchases.Contains(new(AdvancementTrack.College, 4, 3)));
        // Levels 2..4 buy nothing (rank 3 needs level 5: the step fails and is skipped), and the rule still completes: the text is
        // "nothing bought" and the completion message.
        Assert.IsTrue(levels.Take(3).All(l => l.Scheme is { Purchases.Count: 0, Completed: true }));
    }

    [TestMethod]
    public void A_scheme_number_without_a_rule_names_no_rule()
    {
        using (var owner = Owner(65))
            Assert.IsTrue(owner.AwardHeroExperience(owner.Version, T(2)).Award!.LevelUps!.Single().Scheme is { HasRule: false, Purchases.Count: 0, Completed: false });
        // Without scheme data the session cannot run the scheme: the message names no rule as well.
        using var plain = Owner(61, autoLevel: false);
        Assert.IsTrue(plain.AwardHeroExperience(plain.Version, T(2)).Award!.LevelUps!.Single().Scheme is { HasRule: false });
    }

    [TestMethod]
    public void A_followers_level_update_posts_no_message()
    {
        // The award's messages are the hero's; a follower's update has no message of its own (OL27).
        using var owner = Owner(0);
        var award = owner.AwardHeroExperience(owner.Version, T(2)).Award!;
        Assert.AreEqual(1, award.LevelUps!.Count);
        var follower = new SessionNpcAdvancement("virgil", new(CharacterRace.Human, false, [.. Enumerable.Repeat(8, 8)], 0, 0, 1, 0, [.. new int[12]],
            [.. new int[4]], [.. new int[16]], [.. new int[8]], 1, 2, 0, 0), 62);
        var result = owner.LevelFollower(owner.Version, follower, AdvancementModifiers.None, award.Followers!.Value);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.IsFalse(typeof(SessionFollowerLevelResult).GetProperties().Any(p => p.PropertyType == typeof(IReadOnlyList<SessionLevelUp>)));
    }
}
