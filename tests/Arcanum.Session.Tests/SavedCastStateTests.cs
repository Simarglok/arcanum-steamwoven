using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-289 (MIG14 S8, schema 83): a spell missile in flight and an NPC's casting goal are saved with the spell run they wait to begin; a cast host built after the load knows the
// missile's release, so the casting goal's end frees nothing and the arrival begins the run. Records, entities and numbers are invented.
public sealed partial class SessionSpellCasterTests
{
    private sealed class MissilePorts : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    private static readonly CellCoord MissileFrom = new(5, 5), MissileTo = new(6, 5);

    // A hero's cast at the box whose missile flies: the run launched, the missile object with the flight the host writes at its launch and its goal 28 run.
    private static (GameSessionOwner Owner, SessionSpellCaster Caster, SessionSpellRecord Record, int Run, string Missile) Flying()
    {
        var record = SelfBolt(beginTargets: 0x4);
        var (owner, caster, _) = MissileSession(RuleMode.Steamwoven, record);
        var run = Start(owner, caster, record, "box");
        caster.StartGoal(run, new NoCombat());
        Assert.IsTrue(caster.LaunchMissile(run));
        var missile = owner.CreateMissile(MissileFrom)!;
        Assert.IsTrue(owner.MoveMissile(missile, MissileFrom, 0, 0, Placeholder(run, record)));
        Assert.IsTrue(owner.StartGoal(new GoalStartRequest(missile, GoalStarters.Generic, AiExecutorRules.MissileFrame("box", MissileTo, run)), new MissilePorts()).Started);
        return (owner, caster, record, run, missile);
    }

    private static SessionMissileFlight Placeholder(int run, SessionSpellRecord record) =>
        new(SessionSpellCaster.Hero, run, record.Data.Number, "box", 0x1234, 3, 100, MissileFrom, MissileFrom, 1, 0, 0, 0);

    private static SessionSpellCaster CasterOver(GameSessionOwner owner, SessionSpellRecord record) =>
        new(owner, n => n == record.Data.Number ? record : null, new Map(owner, boxDestroyed: false), new Events());

    [TestMethod]
    public void A_missile_in_flight_is_saved_with_its_run_and_a_host_built_after_the_load_lets_it_arrive()
    {
        var (owner, _, record, run, missile) = Flying();
        using var _ = owner;
        var before = Hero(owner).Fatigue;
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"lastMissile\":1,\"missiles\":[{\"entity\":\"missile:1\"");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(owner.QueryMissile(missile), restored.QueryMissile(missile), "the object and its flight");
        Assert.AreEqual(owner.QuerySpellRuns()[run], restored.QuerySpellRuns()[run], "the run waiting for the arrival");
        Assert.AreEqual(1, restored.QueryGoals().RunsOf(missile).Length, "its flight run");
        Assert.AreEqual(Uncounted(text), Uncounted(GameSessionSnapshotCodec.Encode(restored.Capture())), "a second save of the loaded session is the same text but for the step's event sequence");
        var caster = CasterOver(restored, record);
        Assert.IsTrue(caster.MissileFlies(run), "the host built after the load knows the missile");
        Assert.IsNull(caster.Release(run), "its release frame was handled");
        Assert.IsNull(caster.EndGoal(run), "the casting goal's end frees nothing while the missile flies");
        Assert.AreEqual(GameSessionDisposition.Changed, caster.ArriveMissile(run)!.Disposition);
        Assert.AreEqual(before - 7, Hero(restored).Fatigue, "the BEGIN paid at the arrival, as before the save");
        Assert.AreEqual("missile:2", restored.CreateMissile(new(1, 1)), "the counter goes on");
    }

    [TestMethod]
    public void A_missile_without_its_run_or_a_freed_run_is_not_saved_and_a_restore_refuses_the_broken_pairs()
    {
        {
            var (owner, caster, _, run, missile) = Flying();
            using var _ = owner;
            caster.LoseMissile(run);
            Assert.AreEqual(0, owner.Capture().Flights.Missiles.Count, "a run freed under the missile: nothing to release");
            Assert.IsTrue(owner.KillGoals(missile).Succeeded);
            Assert.AreEqual(0, owner.Capture().Flights.Missiles.Count);
        }
        var (flying, _, _, live, _) = Flying();
        using (flying)
        {
            var text = GameSessionSnapshotCodec.Encode(flying.Capture());
            void Refused(string edited)
            {
                Assert.AreNotEqual(text, edited, "the edit applies");
                var decoded = GameSessionSnapshotCodec.TryDecode(edited);
                Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
                var result = GameSessionOwner.Restore(decoded.Snapshot!, RestoreOptions());
                Assert.IsNull(result.Owner);
                Assert.AreEqual(GameSessionFailure.InvalidSnapshot, result.Failure);
            }
            var runText = $"\"run\":{live},";
            Refused(text.Replace(runText, $"\"run\":{live + 1},"));
            Refused(text.Replace("\"lastMissile\":1,", "\"lastMissile\":0,"));
            Refused(text.Replace("\"lastMissile\":1,", $"\"lastMissile\":{int.MaxValue},"));
            Refused(text.Replace("\"next\":1,", "\"next\":2,"));
            Refused(text.Replace("\"paceMs\":100", "\"paceMs\":0"));
            Refused(text.Replace($"\"spellRun\":{live},", "\"spellRun\":null,"));
            // The flight is the cast's: its spell and target; a missile run names a saved missile.
            Refused(text.Replace("\"target\":\"box\",\"art\"", "\"target\":\"oid:other\",\"art\""));
            Refused(text.Replace("\"spell\":90,\"target\":\"box\",\"art\"", "\"spell\":91,\"target\":\"box\",\"art\""));
            Refused(text.Replace("\"lastMissile\":1,\"missiles\":[{\"entity\":\"missile:1\"", "\"lastMissile\":2,\"missiles\":[{\"entity\":\"missile:2\""));
        }
    }

    [TestMethod]
    public void A_missile_whose_run_was_freed_and_its_slot_taken_by_a_new_cast_releases_nothing_and_is_not_saved()
    {
        var (owner, caster, record, run, missile) = Flying();
        using var _ = owner;
        Assert.AreEqual(1, owner.Capture().Flights.Missiles.Count);
        caster.Act(new(SessionSpellRunActionKind.Free, run));
        var taken = Start(owner, caster, record, "box");
        Assert.AreEqual(run, taken, "the slot is reused");
        Assert.IsTrue(owner.StartGoal(new GoalStartRequest("npc:mage", GoalStarters.Generic, new GoalFrame(GoalType.CastSpell) { SpellRun = taken }), new MissilePorts()).Started);
        Assert.IsNotNull(owner.QueryMissile(missile), "the dropped missile still flies until its next tick");
        var captured = owner.Capture();
        Assert.AreEqual(0, captured.Flights.Missiles.Count, "the new cast holds the slot, not the missile");
        Assert.IsTrue(captured.Spells.Runs.Any(r => r.Id == taken), "the new cast's run is saved with its goal");
        Assert.IsFalse(CasterOver(owner, record).MissileFlies(taken), "a host built now gives the new cast no missile mark");
        // The new cast launches its own missile before the dropped one's next tick: the newer missile is the run's, the older one is not saved.
        Assert.IsTrue(owner.KillGoals("npc:mage").Succeeded);
        var newer = owner.CreateMissile(MissileFrom)!;
        Assert.IsTrue(owner.MoveMissile(newer, MissileFrom, 0, 0, Placeholder(taken, record)));
        Assert.IsTrue(owner.StartGoal(new GoalStartRequest(newer, GoalStarters.Generic, AiExecutorRules.MissileFrame("box", MissileTo, taken)), new MissilePorts()).Started);
        var both = owner.Capture();
        CollectionAssert.AreEqual(new[] { newer }, both.Flights.Missiles.Select(m => m.Entity).ToArray());
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(both)));
        Assert.IsNull(restored.QueryMissile(missile));
        Assert.IsNotNull(restored.QueryMissile(newer));
    }

    [TestMethod]
    public void An_npc_casting_goal_is_saved_with_the_run_it_names_and_a_restore_refuses_a_waiting_run_nothing_names()
    {
        var record = SelfBolt(beginTargets: 0x4);
        var (owner, caster, _) = MissileSession(RuleMode.Steamwoven, record);
        using var _ = owner;
        var run = Start(owner, caster, record, "box");
        var goal = owner.StartGoal(new GoalStartRequest("npc:mage", GoalStarters.Generic, new GoalFrame(GoalType.AttemptSpell) { SpellRun = run, Node = 2, RestMs = 40 }),
            new MissilePorts());
        Assert.IsTrue(goal.Started);
        var captured = owner.Capture();
        CollectionAssert.AreEqual(new[] { "npc:mage" }, captured.Goals.Runs.Select(r => r.Owner).ToArray());
        CollectionAssert.AreEqual(new[] { run }, captured.Spells.Runs.Select(r => r.Id).ToArray(), "the run waits for the goal's action frame");
        var text = GameSessionSnapshotCodec.Encode(captured);
        using (var restored = Restore(Decode(text)))
        {
            Assert.AreEqual(owner.QuerySpellRuns()[run], restored.QuerySpellRuns()[run]);
            var (live, back) = (owner.QueryGoals().RunsOf("npc:mage").Single().Top, restored.QueryGoals().RunsOf("npc:mage").Single().Top);
            Assert.AreEqual((live.Goal, live.Node, live.SpellRun, live.RestMs), (back.Goal, back.Node, back.SpellRun, back.RestMs));
            Assert.AreEqual(GameSessionDisposition.Changed, CasterOver(restored, record).Release(run)!.Disposition, "the restored goal's release begins the run");
        }
        var decoded = GameSessionSnapshotCodec.TryDecode(text.Replace($"\"spellRun\":{run},", "\"spellRun\":null,"));
        Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(decoded.Snapshot!, RestoreOptions()).Failure, "a waiting run nothing names");
        // The hero's own runs are not saved yet (S9): its waiting run is cut with them.
        Assert.IsTrue(owner.KillGoals("npc:mage").Succeeded);
        Assert.IsTrue(owner.StartGoal(new GoalStartRequest("pc", GoalStarters.Generic, new GoalFrame(GoalType.AttemptSpell) { SpellRun = run }), new MissilePorts()).Started);
        Assert.AreEqual(0, owner.Capture().Spells.Runs.Count);
    }

    [TestMethod]
    public void A_schema_82_save_is_upgraded_with_no_missile_and_refuses_one_already_there()
    {
        using var owner = Create();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema82(current);
        StringAssert.DoesNotMatch(older, new System.Text.RegularExpressions.Regex("lastMissile"));
        var upgraded = GameSessionSnapshotCodec.TryDecode(older);
        Assert.AreEqual(SnapshotDecodeFailure.None, upgraded.Failure, upgraded.Path);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded.Snapshot!));
        var twice = GameSessionSnapshotCodec.TryDecode(older.Replace("\"npcArrows\":[]", "\"npcArrows\":[],\"missiles\":[]"));
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.flights.missiles"), (twice.Failure, twice.Path));
    }
}
