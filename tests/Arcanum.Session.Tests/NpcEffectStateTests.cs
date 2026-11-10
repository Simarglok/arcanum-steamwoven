using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-348 (E1-d, magic plan P1, schema 85): the effect list of an NPC and the generic spell flags it carries. Effect numbers and changes are invented; the default
/// life facts give 30 hit points and 30 fatigue (ledger EF1, EF2, EF3, EF5, EF10).</summary>
[TestClass]
public sealed class NpcEffectStateTests
{
    private const string Wolf = "oid:wolf", Bear = "oid:bear";

    private static EffectChange Special(int attribute, int value, EffectChangeOperator op = EffectChangeOperator.Add) => new(EffectAttributeKind.Special, attribute, op, value);

    // Maximum hit points +10 and maximum fatigue −50 %.
    private static readonly SessionEffectDefinition Hardy = new(122, [Special(0, 10), Special(1, -50, EffectChangeOperator.Percent)]);
    // Maximum hit points −5.
    private static readonly SessionEffectDefinition Weak = new(125, [Special(0, -5)]);
    // Only a stat (strength +2): kept in the list, no maximum moves.
    private static readonly SessionEffectDefinition Strong = new(120, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 2)]);

    private static SessionLifeTarget Target(string entity = Wolf, LifeState loaded = default, params SessionEffect[] stored) =>
        SessionLifeTarget.Critter(entity, new LifeFacts(), loaded, stored);

    private static void Ok(SessionEffectResult result, int entries)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, entries), (result.Disposition, result.Failure, result.Entries));
    }

    private static void Refused(SessionEffectResult result, GameSessionFailure failure) =>
        Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));

    private static LifeStatus Status(GameSessionOwner owner, SessionLifeTarget target)
    {
        var life = owner.QueryLife(target);
        Assert.AreEqual(GameSessionFailure.None, life.Failure);
        return life.View!.Status;
    }

    [TestMethod]
    public void An_add_keeps_the_current_values_by_moving_the_damage_by_the_change_of_the_maxima()
    {
        using var owner = Create();
        var wolf = Target();
        var start = owner.QueryNpcEffects(wolf)!;
        Assert.AreEqual((Wolf, 0, 0, false), (start.Entity, start.Entries.Count, start.Definitions.Count, start.Recorded));
        Ok(owner.AddNpcEffect(owner.Version, wolf, Hardy, EffectCause.Spell), 1);
        var status = Status(owner, wolf);
        // Hit points: the maximum rises by 10 and so does the damage (the current value stays). Fatigue: the maximum halves and the damage, already 0, stays at 0 (the clamp).
        Assert.AreEqual((40, 30, 15, 15), (status.MaxHitPoints, status.HitPoints, status.MaxFatigue, status.Fatigue));
        var view = owner.QueryNpcEffects(wolf)!;
        Assert.IsTrue(view.Recorded);
        CollectionAssert.AreEqual(new[] { new SessionEffect(122, EffectCause.Spell) }, view.Entries.ToArray());
        Assert.AreEqual(1, view.Count(122));
        Assert.AreEqual(0, view.Count(120));
        // The stored record holds the damage the add moved.
        Assert.AreEqual(10, owner.QueryLifeRecord(Wolf)!.Value.State.HitPointDamage);
        // Another NPC and the hero's list are not touched.
        Assert.IsNull(owner.QueryHeroEffects());
        Assert.IsFalse(owner.QueryNpcEffects(Target(Bear))!.Recorded);
    }

    [TestMethod]
    public void A_removal_takes_the_first_entry_of_the_number_whatever_its_cause_and_moves_the_damage_back()
    {
        using var owner = Create();
        var wolf = Target();
        Ok(owner.AddNpcEffect(owner.Version, wolf, Hardy, EffectCause.Spell), 1);
        Ok(owner.AddNpcEffect(owner.Version, wolf, Weak, EffectCause.Item, 2), 2);
        Ok(owner.AddNpcEffect(owner.Version, wolf, Hardy, EffectCause.Item), 1);
        CollectionAssert.AreEqual(new[] { new SessionEffect(122, EffectCause.Spell), new(125, EffectCause.Item), new(125, EffectCause.Item), new(122, EffectCause.Item) },
            owner.QueryNpcEffects(wolf)!.Entries.ToArray());
        Assert.AreEqual(40, Status(owner, wolf).MaxHitPoints, "+10 −5 −5 +10");
        Assert.AreEqual(30, Status(owner, wolf).HitPoints, "the current value never moved");
        // The first entry of 122 is the spell's.
        Ok(owner.RemoveNpcEffect(owner.Version, wolf, 122), 1);
        CollectionAssert.AreEqual(new[] { new SessionEffect(125, EffectCause.Item), new(125, EffectCause.Item), new(122, EffectCause.Item) }, owner.QueryNpcEffects(wolf)!.Entries.ToArray());
        Assert.AreEqual(30, Status(owner, wolf).MaxHitPoints);
        Assert.AreEqual(30, Status(owner, wolf).HitPoints);
        // By cause: the first one, then all.
        Ok(owner.RemoveNpcEffectsCausedBy(owner.Version, wolf, EffectCause.Item), 1);
        Assert.AreEqual(2, owner.QueryNpcEffects(wolf)!.Entries.Count);
        Ok(owner.RemoveNpcEffectsCausedBy(owner.Version, wolf, EffectCause.Item, all: true), 2);
        Assert.AreEqual(0, owner.QueryNpcEffects(wolf)!.Entries.Count);
        Assert.IsTrue(owner.QueryNpcEffects(wolf)!.Recorded, "an empty list is still the session's");
        Assert.AreEqual(0, owner.QueryNpcEffects(wolf)!.Definitions.Count, "rules of numbers no longer in use are dropped");
        Assert.AreEqual((30, 30), (Status(owner, wolf).MaxHitPoints, Status(owner, wolf).HitPoints));
        // Nothing matches: no change, no revision.
        var revision = owner.Version.Revision;
        var none = owner.RemoveAllNpcEffects(owner.Version, wolf, 122);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None), (none.Disposition, none.Failure));
        Assert.AreEqual(revision, owner.Version.Revision);
        Ok(owner.AddNpcEffect(owner.Version, wolf, Hardy, EffectCause.Spell, 3), 3);
        Ok(owner.RemoveAllNpcEffects(owner.Version, wolf, 122), 3);
    }

    [TestMethod]
    public void A_maximum_pushed_to_nothing_leaves_the_critter_at_no_fatigue_and_no_faint_runs_EF10()
    {
        using var owner = Create();
        var wolf = Target(loaded: new(0, 20, 0, LifeConditions.None, 0));
        // Fatigue −99 % of 30: the maximum 0 (30 × 1 / 100 = 0); the damage 20 − 30 clamps at 0 → no fatigue left, nothing else happens.
        var drained = new SessionEffectDefinition(130, [Special(1, -100, EffectChangeOperator.Percent)]);
        Ok(owner.AddNpcEffect(owner.Version, wolf, drained, EffectCause.Spell), 1);
        var status = Status(owner, wolf);
        Assert.AreEqual((0, 0), (status.MaxFatigue, status.Fatigue));
        Assert.IsFalse(status.IsDead);
    }

    [TestMethod]
    public void A_removal_that_hits_the_rest_of_the_list_never_refuses_and_a_refusal_changes_nothing()
    {
        using var owner = Create();
        var wolf = Target();
        Refused(owner.AddNpcEffect(owner.Version, wolf, Hardy, EffectCause.Spell, 0), GameSessionFailure.InvalidEffect);
        Refused(owner.AddNpcEffect(owner.Version, wolf, Hardy, (EffectCause)99), GameSessionFailure.InvalidEffect);
        Refused(owner.AddNpcEffect(owner.Version, wolf, new(400, []), EffectCause.Spell), GameSessionFailure.InvalidEffect);
        Refused(owner.AddNpcEffect(new(owner.Identity, owner.Version.Revision + 1), wolf, Hardy, EffectCause.Spell), GameSessionFailure.StaleVersion);
        Refused(owner.AddNpcEffect(owner.Version, SessionLifeTarget.Hero, Hardy, EffectCause.Spell), GameSessionFailure.InvalidLifeTarget);
        Refused(owner.AddNpcEffect(owner.Version, Target("static:5"), Hardy, EffectCause.Spell), GameSessionFailure.InvalidLifeTarget);
        Refused(owner.AddNpcEffect(owner.Version, Target(), Hardy, EffectCause.Spell, SessionNpcEffectLimits.MaxEntries + 1), GameSessionFailure.EffectCapacityExceeded);
        Ok(owner.AddNpcEffect(owner.Version, wolf, Hardy, EffectCause.Spell), 1);
        // A number in use with other changes.
        Refused(owner.AddNpcEffect(owner.Version, wolf, new(122, [Special(0, 11)]), EffectCause.Spell), GameSessionFailure.EffectMismatch);
        Assert.AreEqual(1, owner.QueryNpcEffects(wolf)!.Entries.Count);
        // A target with facts or a loaded state the life rules refuse.
        Refused(owner.AddNpcEffect(owner.Version, SessionLifeTarget.Critter(Wolf, new LifeFacts { Level = -4 }, default), Weak, EffectCause.Spell), GameSessionFailure.InvalidLifeFacts);
        Refused(owner.AddNpcEffect(owner.Version, SessionLifeTarget.Critter(Wolf, new LifeFacts(), new(-1, 0, 0, LifeConditions.None, 0)), Weak, EffectCause.Spell), GameSessionFailure.InvalidLifeState);
        // A change that would push the maxima past the life rules' bound.
        var huge = new SessionEffectDefinition(131, [Special(0, 999_999, EffectChangeOperator.Add)]);
        Refused(owner.AddNpcEffect(owner.Version, wolf, huge, EffectCause.Spell), GameSessionFailure.InvalidEffect);
        // The list is capped.
        Ok(owner.AddNpcEffect(owner.Version, wolf, Strong, EffectCause.Spell, SessionNpcEffectLimits.MaxEntries - 1), SessionNpcEffectLimits.MaxEntries - 1);
        Refused(owner.AddNpcEffect(owner.Version, wolf, Strong, EffectCause.Spell), GameSessionFailure.EffectCapacityExceeded);
    }

    [TestMethod]
    public void The_list_the_map_data_stores_is_the_start_and_the_sessions_list_wins_once_it_changes()
    {
        using var owner = Create();
        owner.NpcEffectRules = number => number == 122 ? Hardy : null;
        var wolf = Target(stored: [new(122, EffectCause.Item), new(77, EffectCause.Race)]);
        var loaded = owner.QueryNpcEffects(wolf)!;
        Assert.IsFalse(loaded.Recorded);
        Assert.AreEqual(2, loaded.Entries.Count);
        CollectionAssert.AreEqual(new[] { 77, 122 }, loaded.Definitions.Select(d => d.Number).ToArray());
        Assert.IsTrue(Hardy.SameAs(loaded.Definitions.Single(d => d.Number == 122)));
        Assert.AreEqual(0, loaded.Definitions.Single(d => d.Number == 77).Changes.Length, "a number the host has no rule for has no changes");
        // Removing a loaded entry (whatever its cause) adopts the list: the maxima it gave are in the facts from then on, so the removal moves the damage back.
        Ok(owner.RemoveNpcEffect(owner.Version, wolf, 122), 1);
        // The NPC had the maxima 40 and 15 with no damage, so it had 40 and 15 points; the removal moves the damage by the change of each maximum (hit points −10 clamp at 0, fatigue +15):
        // 30 hit points, 15 fatigue left of 30.
        var status = Status(owner, wolf);
        Assert.AreEqual((30, 30, 30, 15), (status.MaxHitPoints, status.HitPoints, status.MaxFatigue, status.Fatigue));
        CollectionAssert.AreEqual(new[] { new SessionEffect(77, EffectCause.Race) }, owner.QueryNpcEffects(wolf)!.Entries.ToArray());
        // Another read of the same loaded list no longer matters: the session's wins.
        Assert.IsTrue(owner.QueryNpcEffects(wolf)!.Recorded);
        // A loaded list the session does not admit: an undefined cause or a number past the tables.
        Refused(owner.AddNpcEffect(owner.Version, Target(Bear, stored: [new(5, (EffectCause)99)]), Weak, EffectCause.Spell), GameSessionFailure.InvalidEffect);
        Refused(owner.AddNpcEffect(owner.Version, Target(Bear, stored: [new(400, EffectCause.Item)]), Weak, EffectCause.Spell), GameSessionFailure.InvalidEffect);
        // A number the host had no rule for takes the rule of an add (it was an unknown placeholder).
        var plain = Target("oid:fox", stored: [new(120, EffectCause.Item)]);
        Ok(owner.AddNpcEffect(owner.Version, plain, Strong, EffectCause.Spell), 1);
        Assert.AreEqual(2, owner.QueryNpcEffects(plain)!.Entries.Count);
        Assert.IsTrue(Strong.SameAs(owner.QueryNpcEffects(plain)!.Definitions.Single()));
    }

    [TestMethod]
    public void The_lists_survive_the_snapshot_and_a_damaged_list_is_refused()
    {
        using var owner = Create();
        var wolf = Target();
        Ok(owner.AddNpcEffect(owner.Version, wolf, Hardy, EffectCause.Spell, 2), 2);
        Ok(owner.AddNpcEffect(owner.Version, wolf, Strong, EffectCause.Item), 1);
        Ok(owner.AddNpcEffect(owner.Version, Target(Bear), Weak, EffectCause.Curse), 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"critters\":[{\"entity\":\"oid:bear\",\"entries\":[{\"effect\":125,\"cause\":\"Curse\"}],\"definitions\":[{\"effect\":125,");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");
        CollectionAssert.AreEqual(owner.QueryNpcEffects(wolf)!.Entries.ToArray(), restored.QueryNpcEffects(wolf)!.Entries.ToArray());
        var before = owner.QueryNpcEffects(wolf)!.Definitions;
        var again = restored.QueryNpcEffects(wolf)!.Definitions;
        Assert.AreEqual(before.Count, again.Count);
        for (var i = 0; i < before.Count; i++) Assert.IsTrue(before[i].SameAs(again[i]));
        // The facts the session reads carry the restored list's maxima.
        var after = restored.QueryLife(wolf).View!.Status;
        Assert.AreEqual((50, 30), (after.MaxHitPoints, after.HitPoints), "two entries of +10");

        void Fails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        void Refuses(string damaged, GameSessionFailure failure)
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure);
            Assert.AreEqual(failure, GameSessionOwner.Restore(decoded.Snapshot!, RestoreOptions()).Failure);
        }
        // Names and shapes (decode).
        Fails(text.Replace("\"entries\":[{\"effect\":125", "\"entries\":[{\"effect\":400"), SnapshotDecodeFailure.OutOfRange, "$.effects.critters[0].entries[0].effect");
        Fails(text.Replace("\"cause\":\"Curse\"", "\"cause\":\"Nope\""), SnapshotDecodeFailure.OutOfRange, "$.effects.critters[0].entries[0].cause");
        Fails(text.Replace("\"entity\":\"oid:bear\",\"entries\"", "\"entity\":\"oid:bear\",\"x\":1,\"entries\""), SnapshotDecodeFailure.UnknownField, "$.effects.critters[0].x");
        Fails(text.Replace("\"entity\":\"oid:bear\"", "\"entity\":\"oid:zzz\""), SnapshotDecodeFailure.NotCanonical, "$.effects.critters[1].entity");
        // What only the restore knows: an entity that is no mobile NPC, an entry with no rule, a rule in no entry.
        Refuses(text.Replace("\"entity\":\"oid:bear\"", "\"entity\":\"item:5\""), GameSessionFailure.InvalidSnapshot);
        Refuses(text.Replace("\"definitions\":[{\"effect\":125", "\"definitions\":[{\"effect\":124"), GameSessionFailure.InvalidEffect);
    }

    [TestMethod]
    public void An_NPC_carries_spell_flags_of_any_bit_written_by_the_session_and_they_survive_the_snapshot()
    {
        using var owner = Create();
        // Bits beyond the four the hold spells use (invisible 0x2, shrunk 0x4, water walking 0x20000, charm 0x400... are examples of the field's own bits).
        foreach (var mask in new[] { 0x2, 0x4, 0x20000, 0x40000000 })
            Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteObjectFlag(owner.Version, Wolf, SessionObjectFlagField.Spell, mask, true).Disposition);
        Assert.AreEqual(0x40020006 | 0x8, owner.QueryObjectFlags(Wolf).Spell(0x8));
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(owner.QueryObjectFlags(Wolf), restored.QueryObjectFlags(Wolf));
    }
}
