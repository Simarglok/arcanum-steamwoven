using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-2820 (TQ12, 0x4A8570, 0x4B7630): the AI host hands the start-combat (15) and end-combat (16) points of an NPC to the scene's combat script with the NPC as attachee and the given source as triggerer, never stops the pass (the callers ignore the answer),
/// and a subturn end of a critter asks point 16 with the critter itself as the triggerer while the hero's subturn end asks nothing. Invented sectors and NPCs, no original data.</summary>
[TestClass]
public sealed class AreaAiCombatScriptTests
{
    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host, List<(AiScriptPoint Point, string Npc, string? Triggerer)> Calls) Open(bool withScript = true)
    {
        var f = AreaAiHostRebindTests.Open();
        var calls = new List<(AiScriptPoint, string, string?)>();
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]);
        if (withScript) scene = scene with { CombatScript = (point, npc, triggerer) => calls.Add((point, npc, triggerer)) };
        return (f, f.Build(scene), calls);
    }

    [TestMethod]
    public void The_combat_points_reach_the_scene_with_the_source_and_continue_the_pass()
    {
        var (f, host, calls) = Open();
        using (f)
        {
            Assert.IsTrue(host.RunScript(AiScriptPoint.EndCombat, f.A, "pc"));
            Assert.IsTrue(host.RunScript(AiScriptPoint.StartCombat, f.A, "pc"));
            Assert.IsTrue(host.RunScript(AiScriptPoint.StartCombat, f.A, null));
            CollectionAssert.AreEqual(new (AiScriptPoint, string, string?)[]
            {
                (AiScriptPoint.EndCombat, f.A, "pc"), (AiScriptPoint.StartCombat, f.A, "pc"), (AiScriptPoint.StartCombat, f.A, null),
            }, calls, "16 then 15, as the gate calls them");
        }
    }

    [TestMethod]
    public void A_critters_subturn_end_runs_point_16_with_itself_as_the_triggerer_and_the_heros_asks_nothing()
    {
        var (f, host, calls) = Open();
        using (f)
        {
            host.SubturnsEnded([new(SessionCombatEventKind.SubturnEnded, SessionCombatant.Hero, 0), new(SessionCombatEventKind.SubturnStarted, SessionCombatant.Critter(f.A), 0),
                new(SessionCombatEventKind.SubturnEnded, SessionCombatant.Critter(f.A), 0)]);
            CollectionAssert.AreEqual(new (AiScriptPoint, string, string?)[] { (AiScriptPoint.EndCombat, f.A, f.A) }, calls);
            Assert.AreEqual(1, host.TraceOf(f.A)!.EndCombatScripts);
        }
    }

    [TestMethod]
    public void A_scene_without_a_combat_script_runs_nothing_and_continues()
    {
        var (f, host, calls) = Open(withScript: false);
        using (f)
        {
            Assert.IsTrue(host.RunScript(AiScriptPoint.StartCombat, f.A, "pc"));
            host.SubturnsEnded([new(SessionCombatEventKind.SubturnEnded, SessionCombatant.Critter(f.A), 0)]);
            Assert.AreEqual(0, calls.Count);
        }
    }
}
