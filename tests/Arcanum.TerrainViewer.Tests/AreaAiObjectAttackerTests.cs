using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-TECHNOLOGY-1619 (VB2-2 gap d, TT15): the attack event (0x4A9650) of an object that is no critter (a trap's shooter): the host describes the object to the hostility rules (<see cref="AreaAiScene.ObjectFacts"/>) and an NPC
/// victim is asked into COMBAT_FOCUS on it through the setter (0x4AABE0), which is the event's whole effect; a victim of any other kind does nothing more, an object the scene does not know ends the event at once. Invented NPCs (the
/// rebind tests' fixture), no original data.
/// </summary>
[TestClass]
public sealed class AreaAiObjectAttackerTests
{
    private static readonly CellCoord Far = new(2, 2), Near = new(60, 14), ShooterCell = new(61, 14);
    private const string Shooter = "oid:shooter";

    private static (AreaAiHostRebindTests.Fixture F, AreaAiHost Host) Open(AreaAiObjectFacts? facts)
    {
        var f = AreaAiHostRebindTests.Open();
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [new(f.A, f.RowA, Far), new(f.B, f.RowB, Near)])
            with { ObjectFacts = id => id == Shooter ? facts : null };
        return (f, f.Build(scene));
    }

    [TestMethod]
    public void An_npc_victim_of_a_shooter_object_goes_to_combat_focus_on_it_and_the_event_returns()
    {
        var (f, host) = Open(new(3, ShooterCell));
        using var _ = f;
        Assert.AreEqual(DangerState.None, f.Session.QueryDanger(f.B).State);

        host.SpellAttacked(Shooter, f.B);

        var danger = f.Session.QueryDanger(f.B);
        Assert.AreEqual((DangerState.CombatFocus, SessionCombatant.Critter(Shooter)), (danger.State, danger.Focus), "COMBAT_FOCUS on the object: " + host.TraceOf(f.B)?.Setter);
        Assert.AreEqual(nameof(AttackEventStop.ObjectAttacker), host.TraceOf(f.B)?.Event);
        Assert.IsNull(danger.HitBy, "the event returned: no who-hit-me-last write");
        Assert.AreEqual(0, danger.Enemies.Count, "and no team added");
        Assert.IsNull(host.TraceOf(f.B)?.Reaction, "and no reaction to the attack");
    }

    [TestMethod]
    public void A_victim_that_is_not_an_npc_gets_no_focus()
    {
        var (f, host) = Open(new(3, ShooterCell));
        using var _ = f;

        host.SpellAttacked(Shooter, "pc");

        Assert.AreEqual(nameof(AttackEventStop.ObjectAttacker), host.TraceOf("pc")?.Event);
        Assert.AreEqual(DangerState.None, f.Session.QueryDanger(f.B).State, "the other NPC was not asked");
    }

    [TestMethod]
    public void An_object_the_scene_does_not_know_ends_the_event_as_missing()
    {
        var (f, host) = Open(null);
        using var _ = f;

        host.SpellAttacked(Shooter, f.B);

        Assert.AreEqual(nameof(AttackEventStop.Missing), host.TraceOf(f.B)?.Event);
        Assert.AreEqual(DangerState.None, f.Session.QueryDanger(f.B).State);
    }

    [TestMethod]
    public void A_dead_or_gone_victim_stops_the_event_before_the_object_rule()
    {
        var (f, host) = Open(new(3, ShooterCell));
        using var _ = f;
        var target = f.Combat.Build(f.Session, f.B).Combat?.Target ?? throw new AssertFailedException("the NPC has combat values");
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ApplyDamage(f.Session.Version, target, new LifeDamage { HitPoints = 10_000, PlainDeath = true }).Disposition);

        host.SpellAttacked(Shooter, f.B);

        Assert.AreEqual(nameof(AttackEventStop.VictimDead), host.TraceOf(f.B)?.Event);
        Assert.AreEqual(DangerState.None, f.Session.QueryDanger(f.B).State);
    }

    [TestMethod]
    public void The_object_is_a_valid_focus_unless_it_is_a_wall_or_busted_and_its_tile_is_known()
    {
        var (f, host) = Open(new(3, ShooterCell));
        using var _ = f;
        Assert.AreEqual(ShooterCell, host.Tile(Shooter), "the object stands on its own tile");
        var kinds = host.Find(Shooter)!;
        Assert.AreEqual(HostilityKind.Object, kinds.Kind);
        var validity = HostilityRules.Validate(host, f.B, Shooter, f.Session.Rules.Combat);
        Assert.AreEqual(TargetValidity.Valid, validity);
        foreach (var (facts, expected) in new[]
        {
            (new AreaAiObjectFacts(0, ShooterCell), TargetValidity.Wall), (new AreaAiObjectFacts(3, ShooterCell) { Busted = true }, TargetValidity.Busted),
            (new AreaAiObjectFacts(3, ShooterCell) { Destroyed = true }, TargetValidity.Gone), (new AreaAiObjectFacts(3, ShooterCell) { Invulnerable = true }, TargetValidity.Gone),
        })
        {
            var (f2, host2) = Open(facts);
            using var __ = f2;
            Assert.AreEqual(expected, HostilityRules.Validate(host2, f2.B, Shooter, f2.Session.Rules.Combat), $"{facts}");
        }
    }
}
