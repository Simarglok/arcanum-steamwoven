using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's durable effects (ARC-WORLD-029). Effect numbers and changes are invented; the half-elf fixture hero has ST 8,
// CN 7, WP 8 at level 1: 30 hit points and 28 fatigue, and its innate race effect is number 67.
[TestClass]
public sealed class EffectStateTests
{
    private const long T0 = 54_000_000;

    private static EffectChange Change(EffectAttributeKind kind, int attribute, int value, EffectChangeOperator op = EffectChangeOperator.Add) =>
        new(kind, attribute, op, value);

    // Strength +2 (stat 0).
    private static readonly SessionEffectDefinition Strong = new(120, [Change(EffectAttributeKind.Stat, 0, 2)]);
    // Only a basic skill (dodge +1): nothing the session models.
    private static readonly SessionEffectDefinition Nimble = new(121, [Change(EffectAttributeKind.BasicSkill, 1, 1)]);
    // Maximum hit points +10 (special 0) and maximum fatigue −50 % (special 1).
    private static readonly SessionEffectDefinition Hardy = new(122, [Change(EffectAttributeKind.Special, 0, 10),
        Change(EffectAttributeKind.Special, 1, -50, EffectChangeOperator.Percent)]);
    // Constitution −3 (stat 2).
    private static readonly SessionEffectDefinition Frail = new(123, [Change(EffectAttributeKind.Stat, 2, -3)]);

    private sealed class World : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static GameSessionOwner Owner(bool hero = true)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = hero ? HalfElf() : null });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionEffectResult Ok(SessionEffectResult result, GameSessionOwner owner, int entries)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, entries), (result.Disposition, result.Failure, result.Entries));
        Assert.AreEqual(owner.Version, result.Version);
        return result;
    }

    private static void Refused(SessionEffectResult result, GameSessionOwner owner, GameSessionFailure failure)
    {
        var revision = owner.Version.Revision;
        Assert.AreEqual((GameSessionDisposition.Rejected, failure, 0), (result.Disposition, result.Failure, result.Entries));
        Assert.AreEqual(revision, result.Version.Revision);
    }

    private static SessionEffect[] Effects(GameSessionOwner owner) => owner.QueryHeroEffects()!.Effects.ToArray();

    private static int Strength(GameSessionOwner owner) => owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Strength);

    private static LifeStatus Life(GameSessionOwner owner) => owner.QueryHeroLife()!.Status;

    [TestMethod]
    public void EntriesFollowTheSourceListWhateverTheirCause()
    {
        using var owner = Owner();
        var view = owner.QueryHeroEffects()!;
        CollectionAssert.AreEqual(new SessionEffect[] { new(67, EffectCause.Race) }, view.Innate.ToArray());
        Assert.AreEqual((0, 0, 0, 1), (view.Effects.Count, view.Definitions.Count, view.Timed.Count, view.Count(67)));

        Ok(owner.AddHeroEffect(owner.Version, Strong, EffectCause.Spell), owner, 1);
        Ok(owner.AddHeroEffect(owner.Version, Nimble, EffectCause.Item), owner, 1);
        Ok(owner.AddHeroEffect(owner.Version, Strong, EffectCause.Curse), owner, 1);
        Assert.AreEqual(3, owner.Version.Revision);
        CollectionAssert.AreEqual(new SessionEffect[] { new(120, EffectCause.Spell), new(121, EffectCause.Item), new(120, EffectCause.Curse) },
            Effects(owner));
        Assert.AreEqual((2, 12), (owner.QueryHeroEffects()!.Count(120), Strength(owner)));
        CollectionAssert.AreEqual(new[] { 120, 121 }, owner.QueryHeroEffects()!.Definitions.Select(d => d.Number).ToArray());

        // effect_remove_one_typed: the first entry of the number, whatever caused it.
        Ok(owner.RemoveHeroEffect(owner.Version, 120), owner, 1);
        CollectionAssert.AreEqual(new SessionEffect[] { new(121, EffectCause.Item), new(120, EffectCause.Curse) }, Effects(owner));
        Assert.AreEqual(10, Strength(owner));
        // effect_remove_one_caused_by, then all of a number; an absent match changes nothing.
        Ok(owner.RemoveHeroEffectsCausedBy(owner.Version, EffectCause.Item), owner, 1);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RemoveHeroEffectsCausedBy(owner.Version, EffectCause.Item).Disposition);
        Ok(owner.AddHeroEffect(owner.Version, Strong, EffectCause.Spell, count: 2), owner, 2);
        Ok(owner.RemoveAllHeroEffects(owner.Version, 120), owner, 3);
        Assert.AreEqual((0, 0, 8), (Effects(owner).Length, owner.QueryHeroEffects()!.Definitions.Count, Strength(owner)));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RemoveHeroEffect(owner.Version, 120).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RemoveAllHeroEffects(owner.Version, 121).Disposition);

        // Every entry of a cause.
        Ok(owner.AddHeroEffect(owner.Version, Strong, EffectCause.Bless), owner, 1);
        Ok(owner.AddHeroEffect(owner.Version, Nimble, EffectCause.Bless), owner, 1);
        Ok(owner.AddHeroEffect(owner.Version, Nimble, EffectCause.Tech), owner, 1);
        Ok(owner.RemoveHeroEffectsCausedBy(owner.Version, EffectCause.Bless, all: true), owner, 2);
        CollectionAssert.AreEqual(new SessionEffect[] { new(121, EffectCause.Tech) }, Effects(owner));
    }

    [TestMethod]
    public void AChangeOfAMaximumMovesTheDamageSoCurrentValuesStay()
    {
        using var owner = Owner();
        Assert.AreEqual((30, 30, 28, 28), (Life(owner).MaxHitPoints, Life(owner).HitPoints, Life(owner).MaxFatigue, Life(owner).Fatigue));

        // An unharmed hero who grows stronger keeps 30 hit points of a new 34: the source adds the difference to the damage.
        var stronger = Ok(owner.AddHeroEffect(owner.Version, Strong, EffectCause.Spell), owner, 1);
        Assert.AreEqual(new LifeState(4, 0, 0, LifeConditions.None, 0), stronger.Life!.Value.State);
        Assert.AreEqual((34, 30), (Life(owner).MaxHitPoints, Life(owner).HitPoints));
        // The maximum's own effects apply to the whole sum: 34 + 10 hit points, 28 × 50 % = 14 fatigue.
        var hardy = Ok(owner.AddHeroEffect(owner.Version, Hardy, EffectCause.Item), owner, 1);
        Assert.AreEqual((44, 30, 14, 0), (Life(owner).MaxHitPoints, Life(owner).HitPoints, Life(owner).MaxFatigue, hardy.Life!.Value.State.FatigueDamage));
        Assert.AreEqual((14, 14), (Life(owner).Fatigue, owner.QueryHeroLife()!.Record.State.HitPointDamage));

        // Removing the halving gives the fatigue back to the damage (14 of 28 left), and the hit-point bonus is taken back.
        var restored = Ok(owner.RemoveHeroEffect(owner.Version, 122), owner, 1);
        Assert.AreEqual((34, 30, 28, 14), (Life(owner).MaxHitPoints, Life(owner).HitPoints, Life(owner).MaxFatigue, Life(owner).Fatigue));
        Assert.AreEqual(new LifeState(4, 14, 0, LifeConditions.None, 0), restored.Life!.Value.State);
        // That positive fatigue-damage write set the hero's recovery timer, as critter_fatigue_damage_set does.
        Assert.AreEqual(("life.hero.fatigue", T0 + 80_000), (owner.QueryEvents().Single().Kind, owner.QueryEvents().Single().DeadlineMs));
        Ok(owner.AddHeroEffect(owner.Version, Frail, EffectCause.Curse), owner, 1);
        Assert.AreEqual((22, 14, 8), (Life(owner).MaxFatigue, Life(owner).Fatigue, owner.QueryHeroLife()!.Record.State.FatigueDamage));
        Ok(owner.RemoveHeroEffect(owner.Version, 120), owner, 1);
        Assert.AreEqual((30, 30, 0), (Life(owner).MaxHitPoints, Life(owner).HitPoints, owner.QueryHeroLife()!.Record.State.HitPointDamage));

        // A change that moves no maximum leaves the life record alone.
        Assert.IsNull(Ok(owner.AddHeroEffect(owner.Version, Nimble, EffectCause.Item), owner, 1).Life);
        Assert.AreEqual(1, owner.QueryEvents().Count);
    }

    [TestMethod]
    public void EachEntryMovesTheDamageInTurn()
    {
        using var owner = Owner();
        var up = new SessionEffectDefinition(124, [Change(EffectAttributeKind.Special, 1, 20)]);
        var down = new SessionEffectDefinition(125, [Change(EffectAttributeKind.Special, 1, -20)]);
        // +20 then −20 maximum fatigue: 48 with damage 20, then 28 with damage 0; a hit of 5 leaves 23 of 28.
        Ok(owner.AddHeroEffect(owner.Version, up, EffectCause.Bless), owner, 1);
        Ok(owner.AddHeroEffect(owner.Version, down, EffectCause.Bless), owner, 1);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { Fatigue = 5 }).Disposition);
        Assert.AreEqual((28, 23), (Life(owner).MaxFatigue, Life(owner).Fatigue));
        // Removing both is two removals: the +20 first (max 8, damage 5 − 20 clamped to 0), then the −20 (max 28, damage 20).
        // The maxima end where they began, but the hero keeps only 8, as in the source; one combined step would have kept 23.
        Ok(owner.RemoveHeroEffectsCausedBy(owner.Version, EffectCause.Bless, all: true), owner, 2);
        Assert.AreEqual((28, 8, 20), (Life(owner).MaxFatigue, Life(owner).Fatigue, owner.QueryHeroLife()!.Record.State.FatigueDamage));
    }

    [TestMethod]
    public void ATimedEffectEndsOnTheQueueAndRemovesTheFirstEntriesOfItsNumber()
    {
        using var owner = Owner();
        var world = new World();
        Ok(owner.AddHeroEffect(owner.Version, Strong, EffectCause.Item), owner, 1);
        var timed = Ok(owner.AddHeroTimedEffect(owner.Version, Strong, EffectCause.Spell, 2, 80_000), owner, 2);
        var pending = owner.QueryHeroEffects()!.Timed.Single();
        Assert.AreEqual(new SessionTimedEffect(timed.TimedEffect!.Value, T0, T0 + 80_000, 120, 2), pending);
        Assert.AreEqual(14, Strength(owner));

        Advance(owner, 79_999);
        Assert.AreEqual(0, owner.ProcessEvents(world).Effects.Count);
        Advance(owner, 1);
        var revision = owner.Version.Revision;
        var pass = owner.ProcessEvents(world);
        var firing = pass.Effects.Single();
        Assert.AreEqual((GameSessionDisposition.Changed, 2, revision + 1), (firing.Result.Disposition, firing.Result.Entries, owner.Version.Revision));
        // The END removes the item's entry first, as the source's typed removal does; one spell entry stays.
        CollectionAssert.AreEqual(new SessionEffect[] { new(120, EffectCause.Spell) }, Effects(owner));
        Assert.AreEqual((0, 10), (owner.QueryHeroEffects()!.Timed.Count, Strength(owner)));
    }

    [TestMethod]
    public void AnInterruptedTimedEffectEndsAtOnce()
    {
        using var owner = Owner();
        var timed = Ok(owner.AddHeroTimedEffect(owner.Version, Strong, EffectCause.Spell, 1, 1000), owner, 1).TimedEffect!.Value;
        Refused(owner.EndHeroTimedEffect(owner.Version, timed + 1), owner, GameSessionFailure.UnknownTimedEffect);
        Refused(owner.EndHeroTimedEffect(new(owner.Identity, 0), timed), owner, GameSessionFailure.StaleVersion);
        Ok(owner.EndHeroTimedEffect(owner.Version, timed), owner, 1);
        Assert.AreEqual((0, 0, 8), (Effects(owner).Length, owner.QueryEvents().Count, Strength(owner)));
        // An END whose entries are already gone still cancels its event.
        timed = Ok(owner.AddHeroTimedEffect(owner.Version, Strong, EffectCause.Spell, 1, 1000), owner, 1).TimedEffect!.Value;
        Ok(owner.RemoveHeroEffect(owner.Version, 120), owner, 1);
        Ok(owner.EndHeroTimedEffect(owner.Version, timed), owner, 0);
        Assert.AreEqual(0, owner.QueryEvents().Count);
    }

    [TestMethod]
    public void RefusalsAreTypedAndChangeNothing()
    {
        using (var plain = Owner(hero: false))
        {
            Assert.IsNull(plain.QueryHeroEffects());
            Refused(plain.AddHeroEffect(plain.Version, Strong, EffectCause.Spell), plain, GameSessionFailure.EffectsUnavailable);
            Refused(plain.RemoveHeroEffect(plain.Version, 120), plain, GameSessionFailure.EffectsUnavailable);
            Refused(plain.EndHeroTimedEffect(plain.Version, 1), plain, GameSessionFailure.EffectsUnavailable);
        }
        using var owner = Owner();
        void Invalid(SessionEffectDefinition definition, EffectCause cause = EffectCause.Spell, int count = 1) =>
            Refused(owner.AddHeroEffect(owner.Version, definition, cause, count), owner, GameSessionFailure.InvalidEffect);
        Invalid(Strong with { Number = 400 });
        Invalid(Strong with { Number = -1 });
        Invalid(Strong with { Changes = default });
        Invalid(new(125, [Change(EffectAttributeKind.Stat, 28, 1)]));
        Invalid(new(125, [Change(EffectAttributeKind.Special, 10, 1)]));
        Invalid(new(125, [Change((EffectAttributeKind)6, 0, 1)]));
        Invalid(new(125, [Change(EffectAttributeKind.Stat, 0, 1, (EffectChangeOperator)6)]));
        // Decision 7: a factor reads as the original reads it, but a divisor of 0 and a factor on a maximum are refused.
        Invalid(new(125, [Change(EffectAttributeKind.Stat, 0, 0, EffectChangeOperator.Divide)]));
        Invalid(new(125, [Change(EffectAttributeKind.Special, 0, 2, EffectChangeOperator.Multiply)]));
        Invalid(new(125, [Change(EffectAttributeKind.Special, 1, 2, EffectChangeOperator.Divide)]));
        Ok(owner.AddHeroEffect(owner.Version, new(126, [Change(EffectAttributeKind.Stat, 0, 2, EffectChangeOperator.Multiply),
            Change(EffectAttributeKind.Special, 8, 2, EffectChangeOperator.Multiply)]), EffectCause.Spell), owner, 1);
        Assert.AreEqual(16, Strength(owner));
        Ok(owner.RemoveHeroEffect(owner.Version, 126), owner, 1);
        // A modelled change above 1000 (strength, a maximum), or any change above nine digits; at most seven per collection.
        Invalid(new(125, [Change(EffectAttributeKind.Stat, 0, 1001)]));
        Invalid(new(125, [Change(EffectAttributeKind.Special, 0, -1001)]));
        Invalid(new(125, [Change(EffectAttributeKind.BasicSkill, 0, 1_000_000_000)]));
        Invalid(new(125, [.. Enumerable.Repeat(Change(EffectAttributeKind.BasicSkill, 0, 1), 8)]));
        Ok(owner.AddHeroEffect(owner.Version, new(125, [Change(EffectAttributeKind.BasicSkill, 0, 999_999_999),
            .. Enumerable.Repeat(Change(EffectAttributeKind.TechSkill, 0, 1), 7)]), EffectCause.Spell), owner, 1);
        Ok(owner.RemoveHeroEffect(owner.Version, 125), owner, 1);
        // Causes of the race and gender setters and of the life domain's scars; counts below 1.
        Invalid(Strong, EffectCause.Race);
        Invalid(Strong, EffectCause.Gender);
        Invalid(Strong, EffectCause.Injury);
        Invalid(Strong, (EffectCause)10);
        Invalid(Strong, count: 0);
        Refused(owner.RemoveHeroEffect(owner.Version, 120, 0), owner, GameSessionFailure.InvalidEffect);
        Refused(owner.RemoveHeroEffect(owner.Version, 400), owner, GameSessionFailure.InvalidEffect);
        Refused(owner.RemoveHeroEffectsCausedBy(owner.Version, EffectCause.Race), owner, GameSessionFailure.InvalidEffect);
        Refused(owner.RemoveHeroEffectsCausedBy(owner.Version, EffectCause.Injury, all: true), owner, GameSessionFailure.InvalidEffect);
        // Maxima the life rules refuse: 32,000 hit points or more.
        Invalid(new(125, [Change(EffectAttributeKind.Special, 0, 1000)]), count: 32);
        Assert.AreEqual(0, Effects(owner).Length);

        // The innate race effect's number: adding or removing it would meet the innate entry first.
        Refused(owner.AddHeroEffect(owner.Version, Strong with { Number = 67 }, EffectCause.Spell), owner, GameSessionFailure.InnateEffect);
        Refused(owner.RemoveHeroEffect(owner.Version, 67), owner, GameSessionFailure.InnateEffect);
        Refused(owner.RemoveAllHeroEffects(owner.Version, 67), owner, GameSessionFailure.InnateEffect);

        // One rule per number; the stale version and the capacity.
        Ok(owner.AddHeroEffect(owner.Version, Strong, EffectCause.Spell), owner, 1);
        Refused(owner.AddHeroEffect(owner.Version, Strong with { Changes = [Change(EffectAttributeKind.Stat, 0, 3)] }, EffectCause.Spell),
            owner, GameSessionFailure.EffectMismatch);
        Ok(owner.AddHeroEffect(owner.Version, Strong with { Changes = [Change(EffectAttributeKind.Stat, 0, 2)] }, EffectCause.Spell), owner, 1);
        Refused(owner.AddHeroEffect(new(owner.Identity, 0), Nimble, EffectCause.Spell), owner, GameSessionFailure.StaleVersion);
        Refused(owner.RemoveHeroEffect(new(owner.Identity, 0), 120), owner, GameSessionFailure.StaleVersion);
        Refused(owner.AddHeroEffect(owner.Version, Nimble, EffectCause.Spell, SessionEffectLimits.MaxHeroEffects - 1), owner,
            GameSessionFailure.EffectCapacityExceeded);
        Ok(owner.AddHeroEffect(owner.Version, Nimble, EffectCause.Spell, SessionEffectLimits.MaxHeroEffects - 2), owner, SessionEffectLimits.MaxHeroEffects - 2);
        Refused(owner.AddHeroEffect(owner.Version, Nimble, EffectCause.Spell), owner, GameSessionFailure.EffectCapacityExceeded);
        Refused(owner.AddHeroTimedEffect(owner.Version, Nimble, EffectCause.Spell, 1, 0), owner, GameSessionFailure.InvalidEffect);
        Refused(owner.AddHeroTimedEffect(owner.Version, Nimble, EffectCause.Spell, 1, SessionEffectLimits.MaxDurationMs + 1), owner,
            GameSessionFailure.InvalidEffect);
        Assert.AreEqual(SessionEffectLimits.MaxHeroEffects, Effects(owner).Length);
    }

    [TestMethod]
    public void AnAddIsRefusedWhenARemovalCouldLeaveTheBounds()
    {
        using var owner = Owner();
        // A cap of 100 hit points holds 31 entries of +1000 at 100; removing the cap would give 31,030 + … and with a 32nd
        // entry 32,030, beyond the pinned death damage, so the 32nd is refused before the cap could ever be removed.
        var cap = new SessionEffectDefinition(130, [Change(EffectAttributeKind.Special, 0, 100, EffectChangeOperator.Maximum)]);
        var boost = new SessionEffectDefinition(131, [Change(EffectAttributeKind.Special, 0, 1000)]);
        var timed = Ok(owner.AddHeroTimedEffect(owner.Version, cap, EffectCause.Spell, 1, 1000), owner, 1).TimedEffect!.Value;
        Ok(owner.AddHeroEffect(owner.Version, boost, EffectCause.Item, 31), owner, 31);
        Refused(owner.AddHeroEffect(owner.Version, boost, EffectCause.Item), owner, GameSessionFailure.InvalidEffect);
        Assert.AreEqual(100, Life(owner).MaxHitPoints);
        // The cap's END can always run.
        Ok(owner.EndHeroTimedEffect(owner.Version, timed), owner, 1);
        Assert.AreEqual(31 * 1000 + 30, Life(owner).MaxHitPoints);

        // At most 64 changes of a reaction value (the reaction rules' limit): nine entries of seven are 63, a tenth is refused.
        var charm = new SessionEffectDefinition(132, [.. Enumerable.Repeat(Change(EffectAttributeKind.Special, 2, 1), 7)]);
        using var other = Owner();
        Ok(other.AddHeroEffect(other.Version, charm, EffectCause.Spell, 9), other, 9);
        Refused(other.AddHeroEffect(other.Version, charm, EffectCause.Spell), other, GameSessionFailure.InvalidEffect);
        Assert.AreEqual(63, other.QueryHeroReactionFacts()!.ReactionEffects.Count);
    }

    [TestMethod]
    public void AnEffectCanLeaveTheHeroWithoutHitPointsAndNoDeathStep()
    {
        // EF10: the original kills nothing in effect_add; the maximum falls to 0 and the damage cannot go below none.
        using var owner = Owner();
        var wither = new SessionEffectDefinition(133, [Change(EffectAttributeKind.Special, 0, -100, EffectChangeOperator.Percent)]);
        var result = Ok(owner.AddHeroEffect(owner.Version, wither, EffectCause.Curse), owner, 1);
        Assert.AreEqual((0, 0, true), (Life(owner).MaxHitPoints, Life(owner).HitPoints, Life(owner).IsDead));
        // The damage stayed at none (so the life record did not change) and no death time was stamped.
        Assert.AreEqual(((SessionLifeRecord?)null, 0, (long?)null), (result.Life, owner.QueryHeroLife()!.Record.State.HitPointDamage,
            owner.QueryHeroLife()!.Record.DeathTimeMs));
        // Removing it adds the 30 points of the maximum to the damage again: the hero stays at no hit points.
        Ok(owner.RemoveHeroEffect(owner.Version, 133), owner, 1);
        Assert.AreEqual((30, 30, true), (Life(owner).MaxHitPoints, owner.QueryHeroLife()!.Record.State.HitPointDamage, Life(owner).IsDead));
    }

    [TestMethod]
    public void ReactionEffectsReachTheHerosReactionFacts()
    {
        using var owner = Owner();
        var charm = new SessionEffectDefinition(126, [Change(EffectAttributeKind.Special, 2, 10), Change(EffectAttributeKind.Special, 8, 20,
            EffectChangeOperator.Percent), Change(EffectAttributeKind.Special, 7, -50, EffectChangeOperator.Percent)]);
        Ok(owner.AddHeroEffect(owner.Version, charm, EffectCause.Spell), owner, 1);
        var facts = owner.QueryHeroReactionFacts()!;
        CollectionAssert.AreEqual(new ReactionModifier[] { new(StatModifierOperator.Add, 10) }, facts.ReactionEffects.ToArray());
        CollectionAssert.AreEqual(new ReactionModifier[] { new(StatModifierOperator.Percent, 20) }, facts.GoodReactionEffects.ToArray());
        CollectionAssert.AreEqual(new ReactionModifier[] { new(StatModifierOperator.Percent, -50) }, facts.BadReactionEffects.ToArray());
    }

    private const string EffectsJson =
        ",\"effects\":{\"hero\":[{\"effect\":120,\"cause\":\"Item\"},{\"effect\":122,\"cause\":\"Spell\"},{\"effect\":120,\"cause\":\"Spell\"}]," +
        "\"definitions\":[{\"effect\":120,\"changes\":[{\"kind\":\"Stat\",\"attribute\":0,\"operator\":\"Add\",\"value\":2}]}," +
        "{\"effect\":122,\"changes\":[{\"kind\":\"Special\",\"attribute\":0,\"operator\":\"Add\",\"value\":10}," +
        "{\"kind\":\"Special\",\"attribute\":1,\"operator\":\"Percent\",\"value\":-50}]}],\"blessings\":[],\"curses\":[],\"critters\":[]}";

    private static GameSessionOwner Saved()
    {
        var owner = Owner();
        Ok(owner.AddHeroEffect(owner.Version, Strong, EffectCause.Item), owner, 1);
        Ok(owner.AddHeroEffect(owner.Version, Hardy, EffectCause.Spell), owner, 1);
        Ok(owner.AddHeroTimedEffect(owner.Version, Strong, EffectCause.Spell, 1, 8000), owner, 1);
        return owner;
    }

    [TestMethod]
    public void EffectsAndTheirEndsRoundTripThroughTheSnapshot()
    {
        using var owner = Saved();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, EffectsJson + NoExperience + NoAdvancement + ",\"random\":");
        StringAssert.Contains(text, $"{{\"sequence\":1,\"kind\":\"effect.hero.end\",\"deadlineMs\":{T0 + 8000},\"entities\":[],\"values\":[{T0},8000,120,1]}}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        CollectionAssert.AreEqual(Effects(owner), Effects(restored));
        Assert.AreEqual((Strength(owner), Life(owner)), (Strength(restored), Life(restored)));

        // The END fires after the restore and takes the item's entry, the first of its number.
        Advance(restored, 8000);
        Assert.AreEqual(1, restored.ProcessEvents(new World()).Effects.Count);
        CollectionAssert.AreEqual(new SessionEffect[] { new(122, EffectCause.Spell), new(120, EffectCause.Spell) }, Effects(restored));
    }

    [TestMethod]
    public void DecodeAndRestoreRefuseEffectsNoChangeProduces()
    {
        using var owner = Saved();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        void Fails(string from, string to, SnapshotDecodeFailure failure, string path)
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(text.Replace(from, to, StringComparison.Ordinal));
            Assert.AreEqual((failure, path), (decoded.Failure, decoded.Path));
        }
        void Refused(string from, string to, GameSessionFailure failure) =>
            Assert.AreEqual(failure, GameSessionOwner.Restore(Decode(text.Replace(from, to, StringComparison.Ordinal)), RestoreOptions()).Failure);

        Fails("\"cause\":\"Item\"", "\"cause\":\"item\"", SnapshotDecodeFailure.OutOfRange, "$.effects.hero[0].cause");
        Fails("{\"effect\":120,\"cause\":\"Item\"}", "{\"effect\":400,\"cause\":\"Item\"}", SnapshotDecodeFailure.OutOfRange, "$.effects.hero[0].effect");
        Fails("\"definitions\":[", "\"definitions\":[],\"x\":[", SnapshotDecodeFailure.UnknownField, "$.effects.x");
        Fails("\"kind\":\"Stat\"", "\"kind\":\"Stats\"", SnapshotDecodeFailure.OutOfRange, "$.effects.definitions[0].changes[0].kind");
        Fails("\"operator\":\"Percent\"", "\"operator\":\"Times\"", SnapshotDecodeFailure.OutOfRange, "$.effects.definitions[1].changes[1].operator");
        Fails("\"attribute\":0,\"operator\":\"Add\",\"value\":2", "\"attribute\":28,\"operator\":\"Add\",\"value\":2", SnapshotDecodeFailure.OutOfRange,
            "$.effects.definitions[0].changes[0].attribute");
        Fails("{\"effect\":122,\"changes\"", "{\"effect\":120,\"changes\"", SnapshotDecodeFailure.NotCanonical, "$.effects.definitions[1].effect");

        // An entry without its rule, a rule in no entry, a cause the domain does not own, a rule the session refuses, and
        // effects without a hero.
        Refused("{\"effect\":122,\"cause\":\"Spell\"}", "{\"effect\":121,\"cause\":\"Spell\"}", GameSessionFailure.InvalidEffect);
        Refused("{\"effect\":122,\"cause\":\"Spell\"},", "", GameSessionFailure.InvalidEffect);
        Refused("\"cause\":\"Item\"", "\"cause\":\"Injury\"", GameSessionFailure.InvalidEffect);
        Refused("\"value\":2}", "\"value\":1001}", GameSessionFailure.InvalidEffect);
        Refused("{\"effect\":120,\"changes\"", "{\"effect\":67,\"changes\"", GameSessionFailure.InvalidEffect);
        var heroless = GameSessionSnapshotCodec.Encode(Owner(hero: false).Capture());
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(Decode(heroless.Replace(
            NoEffects, EffectsJson, StringComparison.Ordinal)), RestoreOptions()).Failure);
        // An END without a hero, of an innate number, or one whose deadline is not its duration after its start.
        var endJson = $"{{\"sequence\":1,\"kind\":\"effect.hero.end\",\"deadlineMs\":{T0 + 8000},\"entities\":[],\"values\":[{T0},8000,120,1]}}";
        Assert.AreEqual(GameSessionFailure.InvalidEvents, GameSessionOwner.Restore(Decode(heroless.Replace(
            "\"nextSequence\":1,\"pending\":[]", "\"nextSequence\":2,\"pending\":[" + endJson + "]", StringComparison.Ordinal)), RestoreOptions()).Failure);
        Refused($"\"values\":[{T0},8000,120,1]", $"\"values\":[{T0},8000,67,1]", GameSessionFailure.InvalidEvents);
        Refused($"\"values\":[{T0},8000,120,1]", $"\"values\":[{T0},8001,120,1]", GameSessionFailure.InvalidEvents);
        Refused($"\"values\":[{T0},8000,120,1]", $"\"values\":[{T0},8000,120,0]", GameSessionFailure.InvalidEvents);
    }

    private static void Advance(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed,
            owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);
}
