using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-1679 (ledger TI6, TI12, VB2-2 gap d): the script action attack (36) through the real <see cref="AreaAiHost.ScriptAttack"/> and not a stub delegate: the use-script host's heartbeat, run through the AI host's own script stage
/// (<see cref="AreaAiHost.RunScript"/>, the pipeline's heartbeat point), raises the attack event of an NPC attacker on the PC victim and of the PC on an NPC victim, and a scenery's taking-damage script makes an NPC victim fight the object
/// (the scene's <see cref="AreaAiScene.ObjectFacts"/>, #1619). Invented NPCs and scripts (the rebind and heartbeat tests' fixtures), no original data.
/// </summary>
[TestClass]
public sealed class AreaScriptAttackAiHostTests
{
    private static readonly CellCoord SceneryCell = new(61, 14);
    private static string Scenery => AreaHeartbeatScriptTests.Entity(5);

    private sealed record Setup(AreaAiHostRebindTests.Fixture F, AreaAiHost Host, AreaUseScriptHost Use, List<AreaScriptedUseOutcome> Outcomes) : IDisposable
    {
        public void Dispose() => F.Dispose();
    }

    // The AI host of the rebind fixture (two NPCs, the hero) with the scenery known to the scene; the use host reads rows whose entities are the same ids (the fixtures share the key formula), its world's script attack is the AI host's, and the scene's
    // heartbeat hook is the viewer's (TerrainViewerAi.AiHeartbeat) in small: the NPC's heartbeat slot through the use host, asked from the AI host's own script stage.
    private static void WithHosts(Action<Setup> test)
    {
        AreaHeartbeatScriptTests.WithContent(content =>
        {
            var f = AreaAiHostRebindTests.Open();
            AreaAiHost? ai = null;
            AreaUseScriptHost? use = null;
            var outcomes = new List<AreaScriptedUseOutcome>();
            var rows = new[]
            {
                AreaHeartbeatScriptTests.Row(0, 16, 809), AreaHeartbeatScriptTests.Row(1, 16, 810),
                AreaHeartbeatScriptTests.Row(5, 3, 811, point: ScrPoint.TakingDamage),
            };
            var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f), new(f.B, f.RowB, new(130, 10))]) with
            {
                ObjectFacts = id => id == Scenery ? new AreaAiObjectFacts(3, SceneryCell) : null,
                Heartbeat = (point, npc) =>
                {
                    var ran = use!.RunHeartbeat(rows.First(r => AreaUseScriptHost.EntityId(r) == npc), ScrPoint.Heartbeat, f.Session.Rules.Scripts);
                    outcomes.Add(ran.Outcome);
                    return true;
                },
            };
            ai = f.Build(scene);
            var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ScriptAttack: (attacker, victim) => $"{ai.ScriptAttack(attacker, victim)}");
            use = new AreaUseScriptHost(f.Session, 1, rows, [], content, world);
            using var setup = new Setup(f, ai, use, outcomes);
            test(setup);
        });
    }

    [TestMethod]
    public void An_npc_attackers_heartbeat_script_raises_the_event_on_the_pc_through_the_real_ai_host()
    {
        WithHosts(s =>
        {
            // Script 809 on NPC A has the attachee attack the player: the pipeline's heartbeat stage (the scene's hook, here the use host's run) reaches the AI host's own event, flags 2.
            Assert.IsTrue(s.Host.RunScript(AiScriptPoint.Heartbeat, s.F.A, null));
            CollectionAssert.AreEqual(new[] { AreaScriptedUseOutcome.NoTeleport }, s.Outcomes);
            Assert.AreEqual(nameof(AttackEventStop.PcVictim), s.Host.TraceOf(AiSchedulerRules.Pc)?.Event, "the PC victim stops the event: it does not respond with flags 2");
            Assert.AreEqual(nameof(AttackEventStop.PcVictim), s.Use.LastAttack);
            Assert.IsFalse(s.F.Session.QueryCombatMode(SessionCombatant.Hero), "no fan-out: a PC victim does not enter combat mode");
        });
    }

    [TestMethod]
    public void The_pcs_attack_on_an_npc_victim_turns_it_and_records_who_hit_it_through_the_real_ai_host()
    {
        WithHosts(s =>
        {
            // Script 810 on NPC B has the player attack the attachee (the shape of the dialog script 1000's lines).
            Assert.AreEqual(DangerState.None, s.F.Session.QueryDanger(s.F.B).State);
            Assert.IsTrue(s.Host.RunScript(AiScriptPoint.Heartbeat, s.F.B, null));
            CollectionAssert.AreEqual(new[] { AreaScriptedUseOutcome.NoTeleport }, s.Outcomes);
            Assert.AreEqual(nameof(AttackEventStop.None), s.Host.TraceOf(s.F.B)?.Event);
            Assert.AreEqual(nameof(AttackEventStop.None), s.Use.LastAttack);
            Assert.AreEqual(SessionCombatant.Hero, s.F.Session.QueryDanger(s.F.B).HitBy, "the victim recorded who hit it");
        });
    }

    [TestMethod]
    public void A_scenery_attackers_script_makes_an_npc_victim_fight_the_object_and_a_pc_victim_stops()
    {
        WithHosts(s =>
        {
            // The scenery's taking-damage script (811) sets a flag and has the attachee attack the player: the event stops at the object attacker (PC victim: nothing more), applied whole.
            var run = s.Use.RunTakingDamage(Scenery, null, s.F.Session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, run.Outcome, run.Detail);
            Assert.AreEqual(nameof(AttackEventStop.ObjectAttacker), s.Use.LastAttack);
            Assert.AreEqual(nameof(AttackEventStop.ObjectAttacker), s.Host.TraceOf(AiSchedulerRules.Pc)?.Event);
            Assert.AreEqual(DangerState.None, s.F.Session.QueryDanger(s.F.B).State, "the PC victim got no focus and the NPC was not asked");
            // An NPC victim of the same object is asked into COMBAT_FOCUS on it (0x4AABE0), the event's whole effect.
            s.Use.ApplyScriptAttack(new(ScrObjectStateChange.Attack, new(1, s.F.B), Other: new(1, Scenery)));
            var danger = s.F.Session.QueryDanger(s.F.B);
            Assert.AreEqual(DangerState.CombatFocus, danger.State);
            Assert.AreEqual(SessionCombatant.Critter(Scenery), danger.Focus);
            Assert.AreEqual(nameof(AttackEventStop.ObjectAttacker), s.Use.LastAttack);
            Assert.IsNull(danger.HitBy, "no who-hit-me-last write: the event returned");
        });
    }

    [TestMethod]
    public void An_object_the_scene_does_not_describe_ends_the_event_as_missing_and_changes_nothing()
    {
        WithHosts(s =>
        {
            s.Use.ApplyScriptAttack(new(ScrObjectStateChange.Attack, new(1, s.F.B), Other: new(1, AreaHeartbeatScriptTests.Entity(6))));
            Assert.AreEqual(nameof(AttackEventStop.Missing), s.Use.LastAttack);
            Assert.AreEqual(DangerState.None, s.F.Session.QueryDanger(s.F.B).State);
        });
    }
}
