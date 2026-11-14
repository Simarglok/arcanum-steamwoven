using Arcanum.Data.Area;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-1514 (VBG3, 0x4A9F10): a neutral bystander that is no guard and perceives the victim of an attack is sent into FLEE from the attacker through the danger setter (0x4AABE0, state 2); the
/// host used to drop the witness rule's FleeFromAttacker. Invented NPCs (the rebind tests' fixture), no original data.
/// </summary>
[TestClass]
public sealed class AreaAiWitnessFleeTests
{
    private const int SocialClassSlot = 85, GuardClass = 5;
    private static readonly CellCoord Near = new(60, 14);

    private static AreaObjectRecord WithGuardClass(AreaObjectRecord row, int socialClass)
    {
        AreaFieldValue[] Swap(IReadOnlyList<AreaFieldValue> fields) =>
            [.. fields.Select(f => f.Slot == SocialClassSlot ? f with { Number = socialClass } : f)];
        return row with { DirectFields = Swap(row.DirectFields), EffectiveFields = Swap(row.EffectiveFields) };
    }

    // An open field: no wall, portal or object on any tile, so sight and hearing run over empty ground.
    private sealed class OpenField : ILineOfFireMap<AreaObjectRecord>
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public bool Contains(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < 128 && tile.Y < 128;
        public CellCoord? Step(CellCoord from, int rotation) { var next = new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y); return Contains(next) ? next : null; }
        public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge) => [];
        public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile) => [];
    }

    // The victim A beside the hero and a bystander B a few tiles off, both on the map's one sector.
    private static (AreaAiHostRebindTests.Fixture F, AreaAiHost Host) Open(int witnessClass, CellCoord? bystander = null)
    {
        var f = AreaAiHostRebindTests.Open(changeB: row => WithGuardClass(row, witnessClass));
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f), new(f.B, f.RowB, bystander ?? Near)]) with { LineOfFire = () => new OpenField() };
        return (f, f.Build(scene));
    }

    [TestMethod]
    public void A_neutral_bystander_that_perceives_the_victim_asks_the_setter_for_flee_from_the_attacker()
    {
        var (f, host) = Open(witnessClass: 0);
        using var _ = f;
        Assert.AreEqual(DangerState.None, f.Session.QueryDanger(f.B).State);

        host.HeroAttacked(f.A);

        var setter = host.TraceOf(f.B)?.Setter;
        Assert.IsNotNull(setter, $"the witness rule's flee reached the setter (event {host.TraceOf(f.A)?.Event})");
        StringAssert.StartsWith(setter, $"{DangerState.Flee} {AiSchedulerRules.Pc}", "FLEE from the attacker (the hero): " + setter);
        Assert.AreEqual(DangerState.Flee, f.Session.QueryDanger(f.B).State, "the setter applied it: " + setter);
    }

    // ARC-COMBAT-1930 (0x4B2810, 0x4A9650, VCG9): the weapon's loudness is the noise radius of the attack event's witnesses (2, 8 or 15 tiles): a bystander 14 tiles from the attacker and 10 from the victim is no witness of a normal attack and is one of a loud weapon's.
    [TestMethod]
    public void A_loud_weapons_attack_event_reaches_a_bystander_a_normal_one_does_not()
    {
        var far = new CellCoord(72, 14);
        var (normal, normalHost) = Open(0, far);
        using var _ = normal;
        normalHost.HeroAttacked(normal.A);
        Assert.AreEqual(DangerState.None, normal.Session.QueryDanger(normal.B).State, "normal noise carries 8 tiles");
        var (loud, loudHost) = Open(0, far);
        using var __ = loud;
        loudHost.HeroAttacked(loud.A, Loudness.Loud);
        Assert.AreEqual(DangerState.Flee, loud.Session.QueryDanger(loud.B).State, "loud noise carries 15 tiles: " + loudHost.TraceOf(loud.B)?.Setter);
    }

    [TestMethod]
    public void A_guard_bystander_is_not_sent_fleeing()
    {
        var (f, host) = Open(GuardClass);
        using var _ = f;

        host.HeroAttacked(f.A);

        Assert.AreNotEqual(DangerState.Flee, f.Session.QueryDanger(f.B).State, "a guard never takes the witness flee branch");
        Assert.IsNull(host.TraceOf(f.B)?.Setter, "a guard witness reaches no setter call: " + host.TraceOf(f.B)?.Setter);
    }

    [TestMethod]
    public void A_bystander_already_in_a_danger_state_is_left_alone()
    {
        var (f, host) = Open(0);
        using var _ = f;
        host.SpellFlee(f.B, f.A);
        var before = host.TraceOf(f.B)?.Setter;
        Assert.AreEqual(DangerState.Flee, f.Session.QueryDanger(f.B).State);

        host.HeroAttacked(f.A);

        Assert.AreEqual(before, host.TraceOf(f.B)?.Setter, "the witness rule passes a bystander that is not in NONE: no second setter call");
    }
}
