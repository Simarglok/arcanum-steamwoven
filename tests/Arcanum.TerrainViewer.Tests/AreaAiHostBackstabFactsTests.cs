using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-COMBAT-1929 (0x45F9D0, 0x45EFF0, 0x4AF260, 0x4AF470 from the context builder 0x4B2210): the AI host's backstab facts of the hero's strike on an NPC and of an NPC's strike on the hero.
/// The NPC faces +x (its mover's default) two tiles from the hero's tile; a hero west of it stands behind it, one east in front. A strike from behind is a plain backstab while the NPC hears the hero
/// (silent radius 2: Perception 8 reaches two tiles) and a true one when its Perception is too low to hear. Invented open map, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostBackstabFactsTests
{
    private static readonly string Pc = AiSchedulerRules.Pc;

    private sealed class OpenMap : ILineOfFireMap<AreaObjectRecord>
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public bool Contains(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < 200 && tile.Y < 200;
        public CellCoord? Step(CellCoord from, int rotation) => new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y) is var next && Contains(next) ? next : null;
        public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge) => [];
        public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile) => [];
    }

    // The NPC stands at x 62; the hero two tiles west of it (behind its +x facing) or two east (in front).
    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host) Setup(int heroX, int perception)
    {
        var f = AreaAiHostRebindTests.Open(perceptionA: perception);
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)])
            with { HeroCell = () => new CellCoord(heroX, 10), LineOfFire = () => new OpenMap(), HeroRotation = () => 5 };
        return (f, f.Build(scene));
    }

    [TestMethod]
    public void A_hero_behind_an_npc_that_hears_it_makes_a_plain_backstab_and_one_it_cannot_hear_a_true_one()
    {
        var (f, host) = Setup(60, 8);
        using var _ = f;
        var heard = host.HeroBackstabFacts(f.A)!;
        Assert.IsFalse(heard.TargetFacesAttacker(), "the NPC faces +x, the hero stands west of it");
        Assert.IsTrue(heard.TargetPerceivesAttacker(), "silent noise reaches two tiles at Perception 8 (0x4AF470)");
        var (g, deaf) = Setup(60, 3);
        using var __ = g;
        var unheard = deaf.HeroBackstabFacts(g.A)!;
        Assert.IsFalse(unheard.TargetFacesAttacker());
        Assert.IsFalse(unheard.TargetPerceivesAttacker(), "Perception 3 neither sees (it faces away) nor hears the hero two tiles off");
    }

    [TestMethod]
    public void A_hero_in_front_of_the_npc_is_faced()
    {
        var (f, host) = Setup(64, 8);
        using var _ = f;
        Assert.IsTrue(host.HeroBackstabFacts(f.A)!.TargetFacesAttacker());
    }

    [TestMethod]
    public void An_npc_behind_the_hero_is_not_seen_by_the_hero_and_a_pair_without_a_tile_gives_no_facts()
    {
        var (f, host) = Setup(64, 8);
        using var _ = f;
        var behind = host.BackstabFacts(f.A, Pc)!;
        Assert.IsFalse(behind.TargetFacesAttacker(), "the hero turned +x (rotation 5), the NPC west of it is behind");
        Assert.IsTrue(behind.TargetPerceivesAttacker(), "the hero (Perception 8 from the fixture descriptor) hears the silent NPC two tiles away; its sight faces away");
        Assert.IsNull(host.BackstabFacts(f.A, f.A), "an attacker is not its own target");
        Assert.IsNull(host.BackstabFacts(f.A, "oid:nobody"), "an unknown critter has no tile");
        Assert.IsNull(host.HeroBackstabFacts("oid:nobody"));
    }
}
