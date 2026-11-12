using Arcanum.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-COMBAT-1141 (GT198, GT210, GT211): the collision damage of a blocked knockback step (0x42EF60) asks the pushed NPC's taking-damage script (point 0x17) first, with no attacker, and a veto cancels the bump:
/// the amounts are drawn all the same, nothing is written. Invented rows, no original data.
/// </summary>
[TestClass]
public sealed class AreaKnockbackBumpScriptTests
{
    [TestMethod]
    public void ABumpAsksThePushedNpcsScriptWithNoAttackerAndAVetoWritesNothing()
    {
        var asked = new List<(string Victim, string? Attacker)>();
        var allow = false;
        var scene = AreaAiRouteTests.Open(AreaAiRouteTests.Npc(0, [], 0, (10, 20), (10, 20)), takingDamage: (victim, attacker) => { asked.Add((victim, attacker)); return allow; });
        using var _ = scene.Session;
        var before = scene.Session.Version;
        scene.Host.PushBump(scene.Npc, 5);
        Assert.AreEqual((scene.Npc, (string?)null), asked.Single(), "the pushed NPC, no attacker");
        Assert.IsNull(scene.Session.QueryLifeRecord(scene.Npc), "a veto writes no life record");
        Assert.AreEqual(before, scene.Session.Version, "the session is not touched");

        allow = true;
        scene.Host.PushBump(scene.Npc, 5);
        Assert.AreEqual(2, asked.Count);
        var state = scene.Session.QueryLifeRecord(scene.Npc)!.Value.State;
        Assert.IsTrue(state.HitPointDamage is >= 1 and <= 5 || state.FatigueDamage is >= 1 and <= 5, $"the bump's damage went on: {state}");
    }
}
