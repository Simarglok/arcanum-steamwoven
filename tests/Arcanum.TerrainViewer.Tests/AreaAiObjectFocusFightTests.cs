using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-1636 (VB2-2 gap d): an NPC whose COMBAT_FOCUS is an object that is no critter (a trap's shooter object, put there by the attack event 0x4A9650 → 0x4AABE0) fights it: the combat perform starts the attack goal on the object, the goal
/// walks to it and the hit step is the session's object attack (no skill roll), the attack event of the record goes out for the object as victim; a focus the object's break (busted) or removal (destroyed, off) made invalid is dropped by the
/// danger update and ends an attack in progress at the goal's attack-possible test (0x4294F0). Invented NPCs (the rebind tests' fixture) and an invented scenery, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiObjectFocusFightTests
{
    private static readonly CellCoord ShooterCell = new(61, 14), Adjacent = new(60, 14), Distant = new(54, 14);
    private const string Shooter = "oid:shooter";

    // A scenery (type 3) on the shooter's cell.
    private static AreaObjectRecord ShooterRow() => new(new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, 5, 500L, new string('f', 64), 100), 0, 3, 9999, "p", "p", "r", "s", AreaScope.Core, null,
        ShooterCell.X, ShooterCell.Y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    private static AreaObjectAttackTarget SceneryTarget(AreaObjectRecord row, int points = 6) => new(Shooter, row, DamageableObjectKind.Scenery,
        new SessionObjectTarget(new SessionMapId(1), Shooter, new SessionObjectBaseline { Kind = DamageableObjectKind.Scenery, Points = points, Material = ObjectMaterial.Cloth, Cell = ShooterCell }), null, null, null);

    // A line-of-fire map with one object on one tile.
    private sealed class Line(AreaObjectRecord? blocker, CellCoord at) : ILineOfFireMap<AreaObjectRecord>
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public bool Contains(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < 64 && tile.Y < 64;
        public CellCoord? Step(CellCoord from, int rotation) => new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y) is var next && Contains(next) ? next : null;
        public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge) => [];
        public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile) => blocker is not null && tile == at ? [new(blocker, false, 0)] : [];
    }

    private sealed class World
    {
        public required AreaAiObjectFacts? Facts { get; set; }
        public AreaObjectAttackTarget? Target { get; set; }
        public List<(string Who, AreaObjectAttackTarget? Target, SessionObjectAttackResult Result)> Struck { get; } = [];
    }

    private static (AreaAiHostRebindTests.Fixture F, AreaAiHost Host, World W) Open(CellCoord npcAt, AreaAiObjectFacts? facts = null, bool withTarget = true)
    {
        var f = AreaAiHostRebindTests.Open();
        var row = ShooterRow();
        var world = new World { Facts = facts ?? new AreaAiObjectFacts(3, ShooterCell), Target = withTarget ? SceneryTarget(row) : null };
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [new(f.A, f.RowA, new(2, 2)), new(f.B, f.RowB, npcAt)])
            with
            {
                ObjectFacts = id => id == Shooter ? world.Facts : null,
                ObjectAttackTarget = id => id == Shooter ? world.Target : null,
                ObjectStruck = (who, _, target, result) => world.Struck.Add((who, target, result)),
            };
        return (f, f.Build(scene), world);
    }

    // The attack event of the shooter puts the NPC into COMBAT_FOCUS on it; then one pass: the danger update and perform.
    private static void FocusAndPass(AreaAiHostRebindTests.Fixture f, AreaAiHost host)
    {
        host.SpellAttacked(Shooter, f.B);
        Assert.AreEqual(DangerState.CombatFocus, f.Session.QueryDanger(f.B).State, "the attack event gave the focus: " + host.TraceOf(f.B)?.Event);
        host.UpdateDanger(f.B);
        host.Perform(f.B);
    }

    // Steps the host frame by frame (50 ms) until the predicate holds or the frames run out.
    private static void StepUntil(AreaAiHost host, Func<bool> done, int frames = 400)
    {
        for (var frame = 0; frame < frames && !done(); frame++) host.Frames().AdvanceHost(50);
    }

    [TestMethod]
    public void An_object_is_never_a_dead_critter_and_is_gone_only_when_destroyed_or_off()
    {
        var (f, host, world) = Open(Adjacent);
        using var _ = f;
        Assert.IsFalse(host.DeadCritter(Shooter), "0x429450 and 0x4294F0 test death for a critter type only");
        Assert.IsTrue(host.DeadCritter("oid:nobody"), "an id the host knows nothing of is still answered dead");
        Assert.IsFalse(host.Gone(Shooter));
        world.Facts = new AreaAiObjectFacts(3, ShooterCell) { Destroyed = true };
        Assert.IsTrue(host.Gone(Shooter));
        world.Facts = new AreaAiObjectFacts(3, ShooterCell) { Off = true };
        Assert.IsTrue(host.Gone(Shooter));
        world.Facts = new AreaAiObjectFacts(3, ShooterCell) { Busted = true };
        Assert.IsFalse(host.Gone(Shooter), "a busted object is not gone: the focus test drops it, the goal's attack test does not");
    }

    [TestMethod]
    public void An_npc_next_to_the_object_attacks_it_with_the_session_object_attack_and_the_event_goes_out_for_the_object()
    {
        var (f, host, world) = Open(Adjacent);
        using var _ = f;
        var heroHp = f.Session.QueryHeroLife()!.Status.HitPoints;

        FocusAndPass(f, host);

        var run = host.RunOf(f.B) ?? throw new AssertFailedException("no run: " + host.TraceOf(f.B)?.Perform);
        Assert.AreEqual((GoalType.Attack, Shooter), (run.Bottom.Goal, run.Bottom.Target), "the attack goal on the object: " + host.TraceOf(f.B)?.Perform);
        StepUntil(host, () => world.Struck.Count > 0);

        Assert.IsTrue(world.Struck.Count > 0, "the swing's action frame hit the object: " + host.TraceOf(f.B)?.Hit);
        var (who, target, result) = world.Struck[0];
        Assert.AreEqual((f.B, Shooter), (who, target!.Entity));
        Assert.IsTrue(result.Object is { Damage: > 0 }, "it took damage with no skill roll: " + host.TraceOf(f.B)?.Hit);
        Assert.IsTrue(f.Session.QueryObject(world.Target!.Object!).Damage > 0);
        Assert.AreEqual(heroHp, f.Session.QueryHeroLife()!.Status.HitPoints, "the hero is not struck");
        Assert.AreEqual(nameof(AttackEventStop.ObjectVictim), host.TraceOf(Shooter)?.Event, "the record's attack event, the object being the victim");
        Assert.AreEqual(DangerState.CombatFocus, f.Session.QueryDanger(f.B).State, "the NPC keeps the focus while the object stands");
    }

    [TestMethod]
    public void An_npc_far_from_the_object_walks_to_it_and_then_attacks_it()
    {
        var (f, host, world) = Open(Distant);
        using var _ = f;

        FocusAndPass(f, host);
        Assert.AreEqual(((GoalType?)GoalType.Attack, (string?)Shooter), (host.RunOf(f.B)?.Bottom.Goal, host.RunOf(f.B)?.Bottom.Target), host.TraceOf(f.B)?.Perform);
        StepUntil(host, () => world.Struck.Count > 0);

        Assert.IsTrue(world.Struck.Count > 0, "it reached the object and struck it: " + host.TraceOf(f.B)?.Step + " / " + host.TraceOf(f.B)?.Hit);
        Assert.IsTrue(AiExecutorRules.Distance(host.Tile(f.B)!.Value, ShooterCell) <= 1, "from a tile next to it, at " + host.Tile(f.B));
    }

    [TestMethod]
    public void A_ranged_attack_on_the_object_focus_strikes_an_object_that_stops_the_line_instead()
    {
        // The line from the NPC (54, 14) to the shooter object (61, 14) is stopped by a chest on the next tile (55, 14), within the NPC's reach: as for a critter target (RA4) the chest takes the attack, the focus none.
        var chestCell = new CellCoord(55, 14);
        var chest = new AreaObjectRecord(new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, 6, 600L, new string('g', 64), 100), 0, 2, 9998, "p", "p", "r", "s", AreaScope.Core, null,
            chestCell.X, chestCell.Y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);
        var chestTarget = new AreaObjectAttackTarget("sector:1:6", chest, DamageableObjectKind.Container,
            new SessionObjectTarget(new SessionMapId(1), "sector:1:6", new SessionObjectBaseline
            {
                Kind = DamageableObjectKind.Container, Points = 40, Material = ObjectMaterial.Cloth, Cell = chestCell,
                Openable = new SessionOpenableBaseline(Arcanum.Simulation.Locks.OpenableKind.Container, 0, 0, 0),
            }), null, null, null);
        var f = AreaAiHostRebindTests.Open();
        using var _ = f;
        var focusTarget = SceneryTarget(ShooterRow());
        var struck = new List<AreaObjectAttackTarget?>();
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [new(f.A, f.RowA, new(2, 2)), new(f.B, f.RowB, Distant)])
            with
            {
                ObjectFacts = id => id == Shooter ? new AreaAiObjectFacts(3, ShooterCell) : null, ObjectAttackTarget = id => id == Shooter ? focusTarget : null,
                LineOfFire = () => new Line(chest, chestCell), ObjectTarget = row => row.Key == chest.Key ? chestTarget : null, ObjectStruck = (_, _, target, _) => struck.Add(target),
            };
        var host = f.Build(scene);

        for (var attempt = 0; attempt < 5 && struck.Count == 0; attempt++) host.Hit(f.B, Shooter);

        Assert.AreEqual(1, struck.Count, host.TraceOf(f.B)?.Hit);
        Assert.AreEqual("sector:1:6", struck[0]!.Entity, "the chest took the attack: " + host.TraceOf(f.B)?.Hit);
        Assert.AreEqual(0, f.Session.QueryObject(focusTarget.Object!).Damage, "the focus was not struck through it");
    }

    [TestMethod]
    public void A_busted_object_is_dropped_as_the_focus_by_the_danger_update()
    {
        var (f, host, world) = Open(Adjacent);
        using var _ = f;
        host.SpellAttacked(Shooter, f.B);
        host.UpdateDanger(f.B);
        Assert.AreEqual(Shooter, f.Session.QueryDanger(f.B).Focus?.Entity, "a whole object stays the focus: " + host.TraceOf(f.B)?.Danger);

        world.Facts = new AreaAiObjectFacts(3, ShooterCell) { Busted = true };
        host.UpdateDanger(f.B);

        Assert.AreEqual(DangerState.None, f.Session.QueryDanger(f.B).State, "0x4AB990 says a busted object is no focus and nothing else is to fight: the NPC calms down, " + host.TraceOf(f.B)?.Danger);
        StringAssert.StartsWith(host.TraceOf(f.B)?.Danger, nameof(DangerUpdatePath.CalmDown));
    }

    [TestMethod]
    public void A_destroyed_object_ends_an_attack_in_progress_at_the_attack_possible_test()
    {
        var (f, host, world) = Open(Distant);
        using var _ = f;
        FocusAndPass(f, host);
        Assert.IsNotNull(host.RunOf(f.B), host.TraceOf(f.B)?.Perform);

        world.Facts = new AreaAiObjectFacts(3, ShooterCell) { Destroyed = true };
        StepUntil(host, () => host.RunOf(f.B) is null, 40);

        Assert.IsNull(host.RunOf(f.B), "the run ended: " + host.TraceOf(f.B)?.Step);
        Assert.AreEqual(0, world.Struck.Count, "no blow reached the gone object");
    }

    [TestMethod]
    public void An_object_the_session_cannot_damage_is_swung_at_with_no_effect()
    {
        var (f, host, world) = Open(Adjacent, withTarget: false);
        using var _ = f;
        FocusAndPass(f, host);
        StepUntil(host, () => host.TraceOf(f.B)?.Hit is not null, 100);

        StringAssert.Contains(host.TraceOf(f.B)?.Hit, "nothing the session damages");
        Assert.AreEqual(0, world.Struck.Count);
        Assert.AreEqual(DangerState.CombatFocus, f.Session.QueryDanger(f.B).State, "the focus stays: nothing made it invalid");
    }
}
