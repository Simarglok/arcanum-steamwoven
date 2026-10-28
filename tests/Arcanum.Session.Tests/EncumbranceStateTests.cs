using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-087: the encumbrance level's consequences in the session (ledger WE5, WE7–WE9, WEA1, WEA2). The half-elf fixture hero has
// strength 8 (carry weight 4 000: levels start at 600, 1 200, 1 800, 2 400, 3 000, 3 600) and maximum fatigue 30; its kit weighs 1 500
// (level 2). Entities, prototypes and weights are invented; the effect's rule is the installed one's shape (speed −2, ledger WE5).
[TestClass]
public sealed class EncumbranceStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionEffectDefinition Slow = new(CarryRules.EffectNumber, [new(EffectAttributeKind.Stat, 11, EffectChangeOperator.Add, -2)]);

    private static SessionItemFacts Item(SessionItemType type, int prototype, int weight, int? units = null) =>
        new(new(type, prototype, 10, 20), weight, 0, 0, units, []) { AmmoType = type == SessionItemType.Ammo ? 2 : null };

    private static readonly SessionHeroKitItem[] Kit =
    [
        new(new(SessionItemType.Weapon, 6049, 4021, 6029)) { Weight = new(1_000) },
        new(new(SessionItemType.Armor, 8188, 2850, 8168)) { Weight = new(500) },
    ];

    private static SessionMapInventorySeed Seed() => new(
        [new("smith", SessionHolderKind.Npc, false, false, [], [])],
        [
            new("anvil", Item(SessionItemType.Generic, 5001, 2_300), new CellCoord(101, 200)),
            new("cells", Item(SessionItemType.Ammo, 7060, 8, 100), new CellCoord(102, 200)),
            new("more-cells", Item(SessionItemType.Ammo, 7060, 8, 100), new CellCoord(103, 200)),
            new("feather", Item(SessionItemType.Generic, 5002, 1), new CellCoord(104, 200)),
            new("block", Item(SessionItemType.Generic, 5003, 1_000), new CellCoord(105, 200)),
            new("brick", Item(SessionItemType.Generic, 5004, 1_200), new CellCoord(106, 200)),
        ]);

    private static GameSessionOwner Hero(RuleMode rules = RuleMode.Steamwoven, bool attach = true, int points = 0)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() with { Kit = Kit, UnspentPoints = points }, Rules = rules });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        var owner = result.Owner!;
        if (attach) owner.AttachEncumbranceEffect(Slow);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, Seed()).Disposition);
        return owner;
    }

    private static int Entries(GameSessionOwner owner) => owner.QueryHeroEffects()!.Count(CarryRules.EffectNumber);

    private static int Speed(GameSessionOwner owner) => owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Speed);

    private static string[] Drains(GameSessionOwner owner) => owner.QueryEvents().Where(e => e.Kind == SessionEventKinds.HeroEncumbrance)
        .Select(e => $"{e.Payload.Values[0]}+{e.DeadlineMs - e.Payload.Values[0]}").ToArray();

    private static long Now(GameSessionOwner owner) => owner.QueryClockSnapshot().GameTimestampMs;

    private static void Pick(GameSessionOwner owner, string item) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, item, Accept).Disposition, item);

    [TestMethod]
    public void A_new_heros_kit_takes_its_level_at_the_first_change_and_each_insertion_or_removal_recalculates()
    {
        using var owner = Hero();
        var speed = owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetBase(CharacterStat.Speed);
        Assert.AreEqual((0, null), (Entries(owner), owner.QueryHeroEncumbranceChange()), "attaching a seed is no change of state");
        Pick(owner, "feather");
        Assert.AreEqual(2, Entries(owner), "the kit at level 2: two entries, as the original's insertions of it would leave");
        Assert.AreEqual(new SessionEncumbranceChange(1, 2, 2), owner.QueryHeroEncumbranceChange());
        Assert.IsTrue(owner.QueryHeroEffects()!.Effects.All(e => e.Cause == EffectCause.Item));
        Assert.AreEqual(speed - 4, Speed(owner));
        Assert.AreEqual(0, Drains(owner).Length, "no timer below level 4");

        var revision = owner.Version.Revision;
        Pick(owner, "anvil");
        Assert.AreEqual(revision + 1, owner.Version.Revision, "the recalculation is part of the pickup's revision");
        Assert.AreEqual((6, 8), (owner.QueryHeroCarry()!.Level, Entries(owner)), "3 800: level 6 holds eight entries");
        Assert.AreEqual(new SessionEncumbranceChange(2, 6, 8), owner.QueryHeroEncumbranceChange());
        CollectionAssert.AreEqual(new[] { $"{Now(owner)}+5000" }, Drains(owner), "level 6 drains every 5 s");
        Assert.IsFalse(owner.QueryHeroCarry()!.CanRun, "no running from level 4");

        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "smith", "feather", Accept).Disposition);
        Assert.AreEqual(2, owner.QueryHeroEncumbranceChange()!.Sequence, "a removal that keeps the level changes nothing");

        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "smith", "anvil", Accept).Disposition);
        Assert.AreEqual((2, 2), (owner.QueryHeroCarry()!.Level, Entries(owner)), "the removal recalculates: back to two");
        Assert.AreEqual(new SessionEncumbranceChange(3, 2, 2), owner.QueryHeroEncumbranceChange());
        Assert.AreEqual(1, Drains(owner).Length, "a pending timer stays: it ends on its own when it finds no interval");
        Assert.IsTrue(owner.QueryHeroCarry()!.CanRun);
    }

    [TestMethod]
    public void A_strength_point_recalculates_with_the_new_carry_weight()
    {
        using var owner = Hero(points: 1);
        Pick(owner, "anvil");
        Assert.AreEqual(8, Entries(owner));
        owner.OpenHeroEditor();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SpendHeroPoint(owner.Version, AdvancementTrack.Stat, (int)CharacterStat.Strength).Disposition);
        // Strength 9: carry weight 4 500, so 3 800 lies between 3 375 (75 %) and 4 050 (90 %).
        Assert.AreEqual(new SessionHeroCarry(3_800, 4_500, 5), owner.QueryHeroCarry());
        Assert.AreEqual((5, new SessionEncumbranceChange(2, 5, 5)), (Entries(owner), owner.QueryHeroEncumbranceChange()));
    }

    [TestMethod]
    public void An_effect_that_changes_strength_recalculates_and_its_end_too()
    {
        using var owner = Hero();
        Pick(owner, "feather");
        var weak = new SessionEffectDefinition(120, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, -3)]);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, weak, EffectCause.Spell).Disposition);
        // Strength 5: carry weight 2 500, so 1 501 is past 1 500 (60 %).
        Assert.AreEqual((4, 4), (owner.QueryHeroCarry()!.Level, Entries(owner)));
        Assert.AreEqual(1, Drains(owner).Length);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveHeroEffect(owner.Version, 120).Disposition);
        Assert.AreEqual((2, 2), (owner.QueryHeroCarry()!.Level, Entries(owner)));
    }

    [TestMethod]
    public void The_recalculation_removes_entries_of_effect_158_whatever_added_them()
    {
        using var owner = Hero();
        Pick(owner, "feather");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, Slow, EffectCause.Spell).Disposition);
        Assert.AreEqual(3, Entries(owner), "a spell's entry beside the two of level 2");
        Pick(owner, "anvil");
        Assert.AreEqual(8, Entries(owner));
        Assert.IsTrue(owner.QueryHeroEffects()!.Effects.All(e => e.Cause == EffectCause.Item), "the spell's entry went too (0x4EA200)");
    }

    [TestMethod]
    public void A_stack_growing_in_place_recalculates_under_Steamwoven_and_lags_under_Original()
    {
        foreach (var (rules, expected) in new[] { (RuleMode.Steamwoven, 3), (RuleMode.Original, 2) })
        {
            using var owner = Hero(rules);
            Pick(owner, "cells");
            Assert.AreEqual((1_700, 2), (owner.QueryHeroCarry()!.Weight, Entries(owner)), $"{rules}: a new stack keeps level 2");
            Pick(owner, "more-cells");
            Assert.AreEqual((1_900, 3), (owner.QueryHeroCarry()!.Weight, owner.QueryHeroCarry()!.Level), $"{rules}: merged into the held stack");
            Assert.AreEqual(expected, Entries(owner), $"{rules}: WEA1");
            // Under Original the lag holds until a recalculation finds a change of level: an insertion that keeps level 3 changes nothing.
            Pick(owner, "feather");
            Assert.AreEqual(expected, Entries(owner), $"{rules}: still");
        }
    }

    [TestMethod]
    public void The_timer_drains_fatigue_down_to_four_and_follows_the_level_then()
    {
        using var owner = Hero();
        Pick(owner, "anvil");
        var world = new NoCritters();
        var max = owner.QueryHeroLife()!.Status.MaxFatigue;
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 5_000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        var pass = owner.ProcessEvents(world);
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        Assert.AreEqual(1, owner.QueryHeroLife()!.Record.State.FatigueDamage, "one step: 5 s is below 80 s");
        Assert.AreEqual(SessionEventKinds.HeroEncumbrance, pass.Life.Single().Event.Kind);
        CollectionAssert.AreEqual(new[] { $"{Now(owner)}+5000" }, Drains(owner), "scheduled again at level 6");
        // Every 5 s one more, never leaving less than 4 fatigue (the fatigue recovery's ticks raise it again in between).
        var lowest = int.MaxValue;
        for (var i = 0; i < 60; i++)
        {
            Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 5_000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
            Assert.AreEqual(GameSessionFailure.None, owner.ProcessEvents(world).Failure);
            lowest = Math.Min(lowest, max - owner.QueryHeroLife()!.Record.State.FatigueDamage);
        }
        Assert.AreEqual(4, lowest, "the floor is 4");

        // Below level 4 a firing still drains (it does not check the level) but is not scheduled again.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "smith", "anvil", Accept).Disposition);
        Assert.AreEqual(1, Drains(owner).Length);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 5_000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.AreEqual(GameSessionFailure.None, owner.ProcessEvents(world).Failure);
        Assert.AreEqual(0, Drains(owner).Length);
    }

    [TestMethod]
    public void A_pending_timer_keeps_its_older_delay()
    {
        // A rise from level 4 to 6 keeps the level-4 timer's 600 s.
        using var owner = Hero();
        Pick(owner, "block");
        Assert.AreEqual(4, owner.QueryHeroCarry()!.Level, "2 500: level 4");
        var pending = Drains(owner).Single();
        StringAssert.EndsWith(pending, "+600000");
        Pick(owner, "brick");
        Assert.AreEqual((6, 8), (owner.QueryHeroCarry()!.Level, Entries(owner)), "3 700: level 6");
        CollectionAssert.AreEqual(new[] { pending }, Drains(owner), "WEA2: the older, longer delay stays");
        using var other = Hero();
        Pick(other, "anvil");
        var first = Drains(other).Single();
        Assert.AreEqual(GameSessionDisposition.Changed, other.GiveItemToNpc(other.Version, "smith", "kit:2", Accept).Disposition);
        Assert.AreEqual(5, other.QueryHeroCarry()!.Level, "3 300: level 5");
        CollectionAssert.AreEqual(new[] { first }, Drains(other), "WEA2: the level-6 timer stays as it was");
    }

    [TestMethod]
    public void A_restore_keeps_the_entries_and_timer_and_recalculates_nothing()
    {
        using var owner = Hero();
        Pick(owner, "anvil");
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(8, Entries(restored));
        CollectionAssert.AreEqual(Drains(owner), Drains(restored));
        Assert.IsNull(restored.QueryHeroEncumbranceChange(), "a restore shows no change");
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        // A saved encumbrance timer with another delay is refused.
        var drain = owner.QueryEvents().Single(e => e.Kind == SessionEventKinds.HeroEncumbrance);
        var deadline = $"\"deadlineMs\":{drain.DeadlineMs}";
        StringAssert.Contains(text, deadline);
        Assert.AreEqual(GameSessionFailure.InvalidEvents,
            GameSessionOwner.Restore(Decode(text.Replace(deadline, $"\"deadlineMs\":{drain.DeadlineMs + 1}")), RestoreOptions()).Failure);
        // Without an attached rule the saved entries' rule serves (the host attaches its seed again: seeds are not saved).
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, Seed()).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, restored.GiveItemToNpc(restored.Version, "smith", "anvil", Accept).Disposition);
        Assert.AreEqual((2, new SessionEncumbranceChange(1, 2, 2)), (Entries(restored), restored.QueryHeroEncumbranceChange()));
    }

    [TestMethod]
    public void Without_the_rule_the_change_is_recorded_without_entries_and_a_wrong_rule_is_refused()
    {
        using var owner = Hero(attach: false);
        Pick(owner, "anvil");
        Assert.AreEqual((0, new SessionEncumbranceChange(1, 6, 0)), (Entries(owner), owner.QueryHeroEncumbranceChange()));
        Assert.AreEqual(1, Drains(owner).Length, "the timer does not need the rule");
        Assert.ThrowsExactly<ArgumentException>(() => owner.AttachEncumbranceEffect(Slow with { Number = 157 }));
        Assert.ThrowsExactly<ArgumentException>(() => owner.AttachEncumbranceEffect(new(CarryRules.EffectNumber, [new(EffectAttributeKind.Stat, 99, EffectChangeOperator.Add, 1)])));
        using var empty = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.IsNull(empty.QueryHeroEncumbranceChange());
    }

    private sealed class NoCritters : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }
}
