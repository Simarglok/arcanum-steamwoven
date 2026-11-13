using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-1773 (ledger CS5, CS6, CS11; 0x4AF260 and 0x4AF470 through the script conditions 28 and 29): the AI host answers a script's obj can see obj and obj can hear obj with the hero as the observer. Sight takes the hero's facing from the scene
/// (<see cref="AreaAiScene.HeroRotation"/>) and its stunned, blinded and sleeping state from its life state and the session's flags; a host without the hero's facing, a concealed or prowling target, the hero toward itself and a scene without a line-of-fire map
/// refuse (null). Invented open map and an NPC of Perception 8 two tiles from the hero, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostHeroObserverTests
{
    private static readonly string Pc = AiSchedulerRules.Pc;
    private const int StunnedCritterFlag = 0x20, SleepingCritterFlag = 0x1000, ProwlingCritterFlag = 0x1;
    // The octants of the object rotation toward +x (the NPC stands two cells east of the hero) and the opposite way.
    private const int TowardNpc = 5, AwayFromNpc = 1;

    private sealed class OpenMap : ILineOfFireMap<AreaObjectRecord>
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public bool Contains(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < 200 && tile.Y < 200;
        public CellCoord? Step(CellCoord from, int rotation) => new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y) is var next && Contains(next) ? next : null;
        public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge) => [];
        public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile) => [];
    }

    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host) Setup(Func<int?>? heroRotation, bool lineOfFire = true)
    {
        var f = AreaAiHostRebindTests.Open(perceptionA: 8);
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)])
            with { HeroCell = () => new CellCoord(62 - 2, 10), LineOfFire = lineOfFire ? () => new OpenMap() : null, HeroRotation = heroRotation };
        return (f, f.Build(scene));
    }

    private static void Write(AreaAiHostRebindTests.Fixture f, string who, SessionObjectFlagField field, int mask) =>
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.WriteObjectFlag(f.Session.Version, who, field, mask, true).Disposition);

    [TestMethod]
    public void The_hero_sees_what_is_in_front_of_it_and_not_what_is_behind()
    {
        var facing = TowardNpc;
        var (f, host) = Setup(() => facing);
        using var _ = f;
        Assert.IsTrue(host.ScriptCanSee(Pc, f.A), "the NPC two tiles east, the hero turned east");
        facing = AwayFromNpc;
        Assert.IsFalse(host.ScriptCanSee(Pc, f.A), "the NPC is behind the hero (the facing test of 0x45EFF0)");
        facing = TowardNpc + 8;
        Assert.IsTrue(host.ScriptCanSee(Pc, f.A), "the rotation is taken as an octant");
    }

    [TestMethod]
    public void A_hero_whose_facing_the_scene_does_not_give_stops_the_sight_check_but_still_hears()
    {
        var (f, host) = Setup(null);
        using var _ = f;
        Assert.IsNull(host.ScriptCanSee(Pc, f.A), "no facing is not a guess");
        Assert.IsTrue(host.ScriptCanHear(Pc, f.A), "hearing is not a facing test (AW5)");
        var (g, silent) = Setup(() => null);
        using var __ = g;
        Assert.IsNull(silent.ScriptCanSee(Pc, g.A), "a provider that cannot tell");
    }

    [TestMethod]
    public void A_stunned_hero_neither_sees_nor_hears_and_a_sleeping_one_does_not_see()
    {
        var (f, host) = Setup(() => TowardNpc);
        using var _ = f;
        Write(f, Pc, SessionObjectFlagField.Critter, SleepingCritterFlag);
        Assert.IsFalse(host.ScriptCanSee(Pc, f.A), "critter flag 0x1000 is in the sight refusal mask 0x10A0");
        var (g, stunned) = Setup(() => TowardNpc);
        using var __ = g;
        Write(g, Pc, SessionObjectFlagField.Critter, StunnedCritterFlag);
        Assert.IsFalse(stunned.ScriptCanSee(Pc, g.A), "critter flag 0x20");
        Assert.IsFalse(stunned.ScriptCanHear(Pc, g.A), "hearing refuses 0x20 too");
    }

    [TestMethod]
    public void A_hero_blinded_by_the_critter_flag_does_not_see_either_and_the_pick_pocket_start_reads_the_same_check()
    {
        var (f, host) = Setup(() => TowardNpc);
        using var _ = f;
        Assert.IsTrue(host.SeeToward(Pc, f.A).Perceived, "the pick-pocket start turns the hero toward the target");
        Write(f, Pc, SessionObjectFlagField.Critter, 0x80);
        Assert.IsFalse(host.ScriptCanSee(Pc, f.A), "critter flag 0x80 is in the sight refusal mask 0x10A0");
        Assert.IsFalse(host.SeeToward(Pc, f.A).Perceived, "a blinded hero no longer opens a pick-pocket (the same check)");
        Assert.IsTrue(host.ScriptCanHear(Pc, f.A));
    }

    [TestMethod]
    public void A_dead_hero_neither_sees_nor_hears()
    {
        var (f, host) = Setup(() => TowardNpc);
        using var _ = f;
        Assert.IsTrue(host.ScriptCanSee(Pc, f.A));
        var life = f.Session.QueryHeroLife()!;
        f.Session.ApplyDamage(f.Session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = life.Status.MaxHitPoints + 100 }, new SessionDamageParties(null, null));
        Assert.IsTrue(f.Session.QueryHeroLife()!.Status.IsDead);
        Assert.IsFalse(host.ScriptCanSee(Pc, f.A), "0x4AF260 starts with the unconscious-or-dead test of the observer (0x45D800)");
        Assert.IsFalse(host.ScriptCanHear(Pc, f.A));
    }

    [TestMethod]
    public void A_blinded_hero_does_not_see_but_hears()
    {
        var (f, host) = Setup(() => TowardNpc);
        using var _ = f;
        Assert.IsTrue(host.ScriptCanSee(Pc, f.A));
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.SetBlinded(f.Session.Version, SessionLifeTarget.Hero, true).Disposition);
        Assert.IsFalse(host.ScriptCanSee(Pc, f.A));
        Assert.IsTrue(host.ScriptCanHear(Pc, f.A), "blindness (0x80) is not in hearing's mask");
    }

    [TestMethod]
    public void The_hero_stops_for_a_prowling_target_and_for_itself_and_a_scene_without_a_line_of_fire_map_stops_both()
    {
        var (f, host) = Setup(() => TowardNpc);
        using var _ = f;
        Assert.IsTrue(host.ScriptCanSee(Pc, f.A), "the same NPC before it prowls");
        Write(f, f.A, SessionObjectFlagField.Critter, ProwlingCritterFlag);
        Assert.IsNull(host.ScriptCanSee(Pc, f.A), "the hero's check reads the prowling margin of a prowling target, which is not built for the hero");
        Assert.IsNull(host.ScriptCanHear(Pc, f.A));
        var (g, plain) = Setup(() => TowardNpc);
        using var __ = g;
        Assert.IsNull(plain.ScriptCanSee(Pc, Pc), "the hero toward itself is not modelled");
        Assert.IsNull(plain.ScriptCanHear(Pc, Pc));
        var (h, blind) = Setup(() => TowardNpc, lineOfFire: false);
        using var ___ = h;
        Assert.IsNull(blind.ScriptCanSee(Pc, h.A), "no line-of-fire map: the answer is null, not false");
        Assert.IsNull(blind.ScriptCanHear(Pc, h.A));
        Assert.IsNull(blind.ScriptCanSee(h.A, Pc));
    }

    [TestMethod]
    public void An_NPC_observer_does_not_read_the_hero_s_facing()
    {
        var facing = AwayFromNpc;
        var (f, host) = Setup(() => facing);
        using var _ = f;
        var turnedAway = host.ScriptCanSee(f.A, Pc);
        facing = TowardNpc;
        Assert.AreEqual(turnedAway, host.ScriptCanSee(f.A, Pc), "an NPC's sight takes its own mover's facing");
        Assert.AreEqual(host.See(f.A, Pc).Excess == 0, turnedAway);
    }
}
