using Arcanum.Session;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-MAGIC-1142: the AI host reads an NPC's NPC flags and critter flags as the session's layer left them (a charm's kill-on-sight override, the undead bit a spell wrote), and the attack event
/// takes the override off an NPC victim (0x4A9650, NPC flag 0x800 cleared). Invented sectors and NPCs (the rebind tests' fixture), no original data.
/// </summary>
[TestClass]
public sealed class AreaAiNpcFlagTests
{
    private const int KosOverride = 0x800, Undead = 0x4;

    private static AreaAiHost Host(AreaAiHostRebindTests.Fixture f) =>
        f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));

    [TestMethod]
    public void The_hostility_facts_read_the_flags_the_session_wrote_over_the_datas()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = Host(f);
        Assert.IsFalse(host.Find(f.A)!.KosOverride, "the data has no override");
        Assert.IsFalse(host.Find(f.A)!.Undead);

        Assert.AreNotEqual(GameSessionDisposition.Rejected, f.Session.WriteObjectFlag(f.Session.Version, f.A, SessionObjectFlagField.Npc, KosOverride, true).Disposition);
        Assert.AreNotEqual(GameSessionDisposition.Rejected, f.Session.WriteObjectFlag(f.Session.Version, f.A, SessionObjectFlagField.Critter, Undead, true).Disposition);

        Assert.IsTrue(host.Find(f.A)!.KosOverride, "the charm's NPC flag is read");
        Assert.IsTrue(host.Find(f.A)!.Undead, "the undead critter flag a spell wrote is read");
    }

    [TestMethod]
    public void An_attack_on_an_npc_takes_the_kill_on_sight_override_off_it()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = Host(f);
        f.Session.WriteObjectFlag(f.Session.Version, f.A, SessionObjectFlagField.Npc, KosOverride, true);
        Assert.IsTrue(host.Find(f.A)!.KosOverride);

        host.HeroAttacked(f.A);

        Assert.IsFalse(host.Find(f.A)!.KosOverride, "the attack event cleared NPC flag 0x800 (0x4A9650)");
        Assert.IsTrue(f.Session.QueryObjectFlags(f.A).IsEmpty, "the data lacks the bit, so the clear leaves no change behind");
    }

    [TestMethod]
    public void An_attack_on_an_npc_without_the_override_writes_nothing()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = Host(f);

        host.HeroAttacked(f.A);

        Assert.IsTrue(f.Session.QueryObjectFlags(f.A).IsEmpty);
        Assert.IsFalse(host.Find(f.A)!.KosOverride);
    }
}
