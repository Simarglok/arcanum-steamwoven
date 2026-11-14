using Arcanum.Simulation.Character;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Reaction;

[TestClass]
public sealed class ReactionRulesTests
{
    // A plain human NPC (base 50) and a plain human PC: every term is 0, so each test varies one input.
    private static ReactionNpcFacts Npc() => new(50, CharacterRace.Human, false, false);
    private static ReactionPcFacts Pc() => new(CharacterRace.Human, 0, 0, [], [], [], false);
    private static ReactionModifier Add(int value) => new(StatModifierOperator.Add, value);
    private static readonly ReactionHitFacts Row = new(FollowerHitAdjustment: 7, HostileReaction: -30);

    [TestMethod]
    public void TheRaceTableIsTheSourceTableRowNpcColumnPc()
    {
        Assert.AreEqual(0, ReactionRules.RaceReaction(CharacterRace.Human, CharacterRace.Human));
        Assert.AreEqual(10, ReactionRules.RaceReaction(CharacterRace.Human, CharacterRace.Halfling));
        Assert.AreEqual(-20, ReactionRules.RaceReaction(CharacterRace.Elf, CharacterRace.Dwarf));
        Assert.AreEqual(-10, ReactionRules.RaceReaction(CharacterRace.Dwarf, CharacterRace.Elf), "not symmetric: row is the NPC");
        Assert.AreEqual(20, ReactionRules.RaceReaction(CharacterRace.HalfOgre, CharacterRace.Gnome));
        Assert.AreEqual(-40, ReactionRules.RaceReaction(CharacterRace.Orc, CharacterRace.Elf));
        Assert.AreEqual(5, ReactionRules.RaceReaction(CharacterRace.DarkElf, CharacterRace.Elf));
        // Sum of all 88 cells of the source table, so a changed cell fails.
        var sum = 0;
        foreach (var npc in Enum.GetValues<CharacterRace>())
            for (var pc = CharacterRace.Human; pc <= CharacterRace.HalfOgre; pc++) sum += ReactionRules.RaceReaction(npc, pc);
        Assert.AreEqual(-545, sum);
        // LM12: the source reads past its 8 columns for a non-PC race; refused in both modes.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReactionRules.RaceReaction(CharacterRace.Human, CharacterRace.DarkElf));
        Assert.IsFalse(ReactionRules.IsValid(Pc() with { Race = CharacterRace.Orc }));
    }

    [TestMethod]
    public void EffectiveAddsEachTermAndKeepsNoClamp()
    {
        Assert.AreEqual(50, ReactionRules.Effective(50, Npc(), Pc()));
        Assert.AreEqual(57, ReactionRules.Effective(57, Npc(), Pc()), "the stored value");
        Assert.AreEqual(60, ReactionRules.Effective(50, Npc(), Pc() with { Race = CharacterRace.Halfling }), "race term");
        Assert.AreEqual(43, ReactionRules.Effective(50, Npc(), Pc() with { ReactionModifier = -7 }), "beauty modifier");
        Assert.AreEqual(35, ReactionRules.Effective(50, Npc(), Pc() with { ReputationAdjustment = -15 }), "reputation sum");
        Assert.AreEqual(150, ReactionRules.Effective(140, Npc(), Pc() with { ReactionModifier = 10 }), "no clamp above 100");
        Assert.AreEqual(-80, ReactionRules.Effective(-80, Npc(), Pc()), "no clamp below 0");
    }

    [TestMethod]
    public void AloofAndMonstrousSkipRaceAndBeautyButNotReputation()
    {
        var pc = Pc() with { Race = CharacterRace.Halfling, ReactionModifier = 12, ReputationAdjustment = -5 };
        Assert.AreEqual(67, ReactionRules.Effective(50, Npc(), pc));
        Assert.AreEqual(45, ReactionRules.Effective(50, Npc() with { Aloof = true }, pc));
        Assert.AreEqual(45, ReactionRules.Effective(50, Npc() with { Monstrous = true }, pc));
    }

    [TestMethod]
    public void EnemyListMindControlAndPcEffectsComeInSourceOrder()
    {
        Assert.AreEqual(0, ReactionRules.Effective(90, Npc() with { PcOnEnemyList = true }, Pc()), "enemy list first");
        Assert.AreEqual(0, ReactionRules.Effective(90, Npc() with { PcOnEnemyList = true, MindControlledByPc = true }, Pc()));
        Assert.AreEqual(50, ReactionRules.Effective(10, Npc() with { MindControlledByPc = true }, Pc()), "mind control floor");
        Assert.AreEqual(70, ReactionRules.Effective(70, Npc() with { MindControlledByPc = true }, Pc()), "a floor, not a set");
        // The PC's reaction effects apply to the whole sum: +50% then +4.
        var effects = Pc() with { ReactionModifier = 10, ReactionEffects = [Add(4), new(StatModifierOperator.Percent, 50)] };
        Assert.AreEqual(94, ReactionRules.Effective(50, Npc(), effects));
        // The floor comes after the effects: 10 → 5 by a −50% effect, then raised to 50.
        var lowering = Pc() with { ReactionEffects = [new(StatModifierOperator.Percent, -50)] };
        Assert.AreEqual(5, ReactionRules.Effective(10, Npc(), lowering));
        Assert.AreEqual(50, ReactionRules.Effective(10, Npc() with { MindControlledByPc = true }, lowering));
        // The good and bad adjustment effects do not change a query.
        Assert.AreEqual(50, ReactionRules.Effective(50, Npc(), Pc() with { GoodReactionEffects = [Add(9)], BadReactionEffects = [Add(9)] }));
    }

    [TestMethod]
    public void TheHostilityVariantFloorsOnlyTheRaceAndBeautyTerm()
    {
        var ugly = Pc() with { Race = CharacterRace.Human, ReactionModifier = -65, ReputationAdjustment = -20 };
        var darkElf = Npc() with { Race = CharacterRace.DarkElf };
        // −30 race −65 beauty = −95, floored to −49; reputation −20 is not floored.
        Assert.AreEqual(50 - 95 - 20, ReactionRules.Effective(50, darkElf, ugly));
        Assert.AreEqual(50 - 49 - 20, ReactionRules.Effective(50, darkElf, ugly, ReactionQuery.Hostility));
        Assert.AreEqual(43, ReactionRules.Effective(50, Npc(), Pc() with { ReactionModifier = -7 }, ReactionQuery.Hostility), "above the floor");
        Assert.AreEqual(30, ReactionRules.Effective(50, darkElf with { Aloof = true }, ugly, ReactionQuery.Hostility), "skipped term");
    }

    [TestMethod]
    public void CategoriesUseTheSourceThresholds()
    {
        (int Value, ReactionCategory Category)[] cases =
        [
            (-500, ReactionCategory.Hatred), (0, ReactionCategory.Hatred), (1, ReactionCategory.Dislike), (20, ReactionCategory.Dislike),
            (21, ReactionCategory.Suspicious), (40, ReactionCategory.Suspicious), (41, ReactionCategory.Neutral),
            (60, ReactionCategory.Neutral), (61, ReactionCategory.Courteous), (80, ReactionCategory.Courteous),
            (81, ReactionCategory.Amiable), (100, ReactionCategory.Amiable), (101, ReactionCategory.Love),
        ];
        foreach (var (value, category) in cases) Assert.AreEqual(category, ReactionRules.Category(value), value.ToString());
        Assert.AreEqual(0, (int)ReactionCategory.Love);
        Assert.AreEqual(6, (int)ReactionCategory.Hatred);
    }

    [TestMethod]
    public void AdjustSkipsTheSourceNoOpCases()
    {
        Assert.AreEqual(new ReactionAdjustment(ReactionSkip.ZeroDelta, 0, false), ReactionRules.Adjust(0, Npc(), Pc(), true));
        Assert.AreEqual(ReactionSkip.MindControlled, ReactionRules.Adjust(5, Npc() with { MindControlledByPc = true }, Pc(), false).Skip);
        Assert.AreEqual(ReactionSkip.MindControlled, ReactionRules.Adjust(-5, Npc() with { MindControlledByPc = true }, Pc(), false).Skip);
        var charming = Pc() with { ExtraordinaryCharisma = true };
        Assert.AreEqual(ReactionSkip.LeaderCharisma, ReactionRules.Adjust(-5, Npc(), charming, true).Skip);
        Assert.AreEqual(new ReactionAdjustment(ReactionSkip.None, 5, false), ReactionRules.Adjust(5, Npc(), charming, true), "positive applies");
        Assert.AreEqual(ReactionSkip.LeaderCharisma, ReactionRules.Adjust(-5, Npc(), charming, false, pcLeads: true).Skip, "ARC-WORLD-2119: the skip reads the chain, an NPC led through a follower too");
        Assert.AreEqual(new ReactionAdjustment(ReactionSkip.None, -5, false), ReactionRules.Adjust(-5, Npc(), charming, false, pcLeads: false), "the leader check stays direct");
        Assert.AreEqual(new ReactionAdjustment(ReactionSkip.None, -5, false), ReactionRules.Adjust(-5, Npc(), charming, false), "not led");
        Assert.AreEqual(new ReactionAdjustment(ReactionSkip.None, -5, true), ReactionRules.Adjust(-5, Npc(), Pc(), true), "leader check");
        Assert.AreEqual(new ReactionAdjustment(ReactionSkip.None, 5, false), ReactionRules.Adjust(5, Npc(), Pc(), true), "only a drop checks");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReactionRules.Adjust(ReactionLimits.MaxMagnitude + 1, Npc(), Pc(), false));
    }

    [TestMethod]
    public void AdjustPassesTheDeltaThroughThePcEffectsOfItsSign()
    {
        var gnome = Pc() with { GoodReactionEffects = [new(StatModifierOperator.Percent, 100)], BadReactionEffects = [Add(10)] };
        Assert.AreEqual(20, ReactionRules.Adjust(10, Npc(), gnome, false).Delta, "good: doubled");
        Assert.AreEqual(0, ReactionRules.Adjust(-10, Npc(), gnome, false).Delta, "bad: −10 + 10 still applies as 0");
        Assert.IsTrue(ReactionRules.Adjust(-10, Npc(), gnome, false).Applies);
        Assert.AreEqual(-20, ReactionRules.Adjust(-30, Npc(), gnome, false).Delta);
        // The NPC's reaction does not pass through the NPC's effects: only the PC's adjustment effects count.
        Assert.AreEqual(10, ReactionRules.Adjust(10, Npc(), Pc() with { ReactionEffects = [Add(99)] }, false).Delta);
    }

    [TestMethod]
    public void SetToIsADeltaAndConversationStartMayForceAHundred()
    {
        Assert.AreEqual(-9L, ReactionRules.DeltaTo(41, 50));
        Assert.AreEqual(2_000_000L, ReactionRules.DeltaTo(1_000_000, -1_000_000));
        Assert.AreEqual(60, ReactionRules.ConversationStart(60, false));
        Assert.AreEqual(100, ReactionRules.ConversationStart(60, true));
        Assert.AreEqual(120, ReactionRules.ConversationStart(120, true), "only below 100");
    }

    [TestMethod]
    public void AFollowerHitFollowsTheRuleMode()
    {
        var original = ReactionRules.PcHit(50, Npc(), Pc(), Row, ledByPc: true, deliberate: true, ReactionRuleProfile.Original);
        Assert.AreEqual(new ReactionHit(7, true), original, "A2 in the original: the reaction rises by parameter 9");
        var steamwoven = ReactionRules.PcHit(50, Npc(), Pc(), Row, ledByPc: true, deliberate: true, ReactionRuleProfile.Steamwoven);
        Assert.AreEqual(new ReactionHit(-7, true), steamwoven, "the recorded fix lowers it");
        Assert.AreEqual(new ReactionHit(null, true), ReactionRules.PcHit(50, Npc(), Pc(), Row, true, false, ReactionRuleProfile.Original),
            "an accidental hit changes nothing but the follow check runs");
        Assert.AreEqual(new ReactionHit(null, true), ReactionRules.PcHit(50, Npc(), Pc(), Row, true, false, ReactionRuleProfile.Steamwoven));
    }

    [TestMethod]
    public void AHitOnAnotherNpcLowersItToParameterTen()
    {
        foreach (var profile in ReactionRuleProfile.All)
        {
            Assert.AreEqual(new ReactionHit(ReactionRules.AccidentalHit, false), ReactionRules.PcHit(50, Npc(), Pc(), Row, false, false, profile));
            Assert.AreEqual(new ReactionHit(-80, false), ReactionRules.PcHit(50, Npc(), Pc(), Row, false, true, profile), "50 → −30");
            Assert.AreEqual(new ReactionHit(null, false), ReactionRules.PcHit(-30, Npc(), Pc(), Row, false, true, profile), "at the parameter");
            Assert.AreEqual(new ReactionHit(null, false), ReactionRules.PcHit(-60, Npc(), Pc(), Row, false, true, profile), "below it");
        }
        // The comparison reads the hostility variant: −95 is floored to −49, so 50 reads 1, not −45.
        var ugly = Pc() with { ReactionModifier = -65 };
        var darkElf = Npc() with { Race = CharacterRace.DarkElf };
        Assert.AreEqual(new ReactionHit(-31, false), ReactionRules.PcHit(50, darkElf, ugly, Row, false, true, ReactionRuleProfile.Steamwoven));
    }

    [TestMethod]
    public void ProfilesAreNamedAndOnlyTheOriginalKeepsTheRise()
    {
        Assert.AreEqual("steamwoven", ReactionRuleProfile.Steamwoven.Name);
        Assert.AreEqual("original", ReactionRuleProfile.Original.Name);
        Assert.IsFalse(ReactionRuleProfile.Steamwoven.FollowerHitRaisesReaction);
        Assert.IsTrue(ReactionRuleProfile.Original.FollowerHitRaisesReaction);
        Assert.AreSame(ReactionRuleProfile.Original, ReactionRuleProfile.FromName("original"));
        Assert.IsNull(ReactionRuleProfile.FromName("Original"));
        CollectionAssert.AreEqual(new[] { ReactionRuleProfile.Steamwoven, ReactionRuleProfile.Original }, ReactionRuleProfile.All.ToArray());
    }

    [TestMethod]
    public void InvalidFactsAreRefused()
    {
        Assert.IsTrue(ReactionRules.IsValid(Npc()));
        Assert.IsTrue(ReactionRules.IsValid(Pc()));
        Assert.IsFalse(ReactionRules.IsValid((ReactionNpcFacts?)null));
        Assert.IsFalse(ReactionRules.IsValid(Npc() with { BaseReaction = ReactionLimits.MaxMagnitude + 1 }));
        Assert.IsFalse(ReactionRules.IsValid(Npc() with { Race = (CharacterRace)11 }));
        Assert.IsFalse(ReactionRules.IsValid(Pc() with { ReactionModifier = -ReactionLimits.MaxMagnitude - 1 }));
        Assert.IsFalse(ReactionRules.IsValid(Pc() with { ReactionEffects = null! }));
        Assert.IsFalse(ReactionRules.IsValid(Pc() with { BadReactionEffects = [new(StatModifierOperator.Divide, 0)] }), "divisor 0");
        Assert.IsFalse(ReactionRules.IsValid(Pc() with { GoodReactionEffects = [new((StatModifierOperator)9, 1)] }));
        Assert.IsFalse(ReactionRules.IsValid(Pc() with { ReactionEffects = Enumerable.Repeat(Add(1), ReactionLimits.MaxModifiers + 1).ToArray() }));
        Assert.ThrowsExactly<ArgumentException>(() => ReactionRules.Effective(50, Npc() with { BaseReaction = int.MaxValue }, Pc()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReactionRules.Effective(int.MinValue, Npc(), Pc()));
    }

    [TestMethod]
    public void EffectsAggregateInTheStatOrder()
    {
        ReactionModifier[] all =
        [
            new(StatModifierOperator.Maximum, 90), new(StatModifierOperator.Minimum, 10), Add(3),
            new(StatModifierOperator.Divide, 2), new(StatModifierOperator.Multiply, 3), new(StatModifierOperator.Percent, 20),
        ];
        // 40 × 120% = 48, × 3 = 144, / 2 = 72, + 3 = 75, within 10..90.
        Assert.AreEqual(75, ReactionRules.ApplyEffects(40, all));
        Assert.AreEqual(90, ReactionRules.ApplyEffects(100, all), "maximum last");
        Assert.AreEqual(10, ReactionRules.ApplyEffects(-20, all), "minimum");
        Assert.AreEqual(-7, ReactionRules.ApplyEffects(-15, [new(StatModifierOperator.Divide, 2)]), "truncates toward zero");
        // Several of a kind: percents sum, the highest minimum and the lowest maximum win.
        Assert.AreEqual(130, ReactionRules.ApplyEffects(100, [new(StatModifierOperator.Percent, 10), new(StatModifierOperator.Percent, 20)]));
        ReactionModifier[] bounds = [new(StatModifierOperator.Minimum, 10), new(StatModifierOperator.Minimum, 30),
            new(StatModifierOperator.Maximum, 80), new(StatModifierOperator.Maximum, 60)];
        Assert.AreEqual(30, ReactionRules.ApplyEffects(0, bounds));
        Assert.AreEqual(60, ReactionRules.ApplyEffects(100, bounds));
        Assert.AreEqual(45, ReactionRules.ApplyEffects(45, bounds));
    }
}
