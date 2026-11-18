using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-1520 (VBG9; ledger AW18, VB3-16, VB3-17, VB3-18; 0x4AF260, 0x4AF470, 0x4AF640): the sight line and the sound line start at the
/// observer's tile centre plus its pixel offset (object fields 3 and 4): the offset of its map data and, mid-step, the part of the step it
/// has left. Invented map with crates and a wall on the tiles straight ahead of the NPC, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostObserverOffsetTests
{
    private static readonly string Pc = AiSchedulerRules.Pc;
    private static readonly CellCoord Observer = new(62, 10);
    private const uint Opaque = 0;

    // A map of crates (opaque objects) and a wall piece, with nothing else on it; every object is the same invented record.
    private sealed class Map(AreaObjectRecord thing) : ILineOfFireMap<AreaObjectRecord>
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public HashSet<CellCoord> Crates { get; } = [];
        public HashSet<(CellCoord Tile, int Edge)> Walls { get; } = [];
        public bool Contains(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < 200 && tile.Y < 200;
        public CellCoord? Step(CellCoord from, int rotation) => new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y) is var next && Contains(next) ? next : null;
        public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge) =>
            Walls.Contains((tile, edge)) ? [new LineOfFirePiece<AreaObjectRecord>(thing, LineOfFirePieceKind.Solid, 0)] : [];
        public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile) =>
            Crates.Contains(tile) ? [new LineOfFireObject<AreaObjectRecord>(thing, false, Opaque)] : [];
    }

    private static AreaFieldValue Offset(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host, Map Map) Setup(CellCoord hero, int offsetX = 0, int offsetY = 0, Func<string, (int X, int Y)?>? step = null)
    {
        var f = AreaAiHostRebindTests.Open(perceptionA: 8);
        var row = f.RowA with { EffectiveFields = [.. f.RowA.EffectiveFields.Where(field => field.Slot is not (2 or 3)), Offset(2, offsetX), Offset(3, offsetY)] };
        var map = new Map(f.RowB);
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f, row)]) with { HeroCell = () => hero, LineOfFire = () => map, ObserverStep = step };
        return (f, f.Build(scene), map);
    }

    // One tile step in +x on the screen: the offset that moves the line's start one tile to the +x side.
    private static (int X, int Y) StepX => (-40, 20);

    [TestMethod]
    public void A_sight_line_starts_at_the_offset_of_the_observers_map_data()
    {
        var hero = new CellCoord(62, 13);
        var (f, host, map) = Setup(hero);
        using var _ = f;
        map.Crates.Add(new(62, 11));
        map.Crates.Add(new(62, 12));
        Assert.AreEqual(new AwarenessResult(1, AwarenessReason.Blocked), host.SeeToward(f.A, Pc), "from the tile centre the crates are on the line");

        var (g, shifted, gMap) = Setup(hero, StepX.X, StepX.Y);
        using var __ = g;
        gMap.Crates.Add(new(62, 11));
        gMap.Crates.Add(new(62, 12));
        Assert.AreEqual(new AwarenessResult(0, AwarenessReason.Perceived), shifted.SeeToward(g.A, Pc), "a line that starts one tile to the side passes the crates");
    }

    [TestMethod]
    public void A_sound_line_starts_at_the_offset_of_the_observers_map_data()
    {
        var hero = new CellCoord(62, 16);
        var (f, host, map) = Setup(hero);
        using var _ = f;
        map.Walls.Add((Observer, 3));
        var walled = host.Hear(f.A, Pc, Loudness.Normal);
        Assert.IsFalse(walled.Perceived, "the wall on the observer's edge muffles the sound");

        var (g, shifted, gMap) = Setup(hero, StepX.X, StepX.Y);
        using var __ = g;
        gMap.Walls.Add((Observer, 3));
        var open = shifted.Hear(g.A, Pc, Loudness.Normal);
        var (h, plain, hMap) = Setup(hero);
        using var ___ = h;
        Assert.IsTrue(open.Excess < walled.Excess, $"the shifted line misses the wall (excess {open.Excess} against {walled.Excess}); without the wall: {plain.Hear(h.A, Pc, Loudness.Normal).Excess}");
        Assert.AreEqual(plain.Hear(h.A, Pc, Loudness.Normal).Excess, open.Excess, "and costs what an open line costs");
    }

    [TestMethod]
    public void A_walking_observer_starts_the_line_where_its_body_is_on_the_screen()
    {
        var hero = new CellCoord(62, 13);
        var (f, host, map) = Setup(hero);
        using var _ = f;
        // One crate beside the straight line from the NPC's tile to the hero, on the line from the tile it steps to.
        map.Crates.Add(new(63, 11));
        var eyes = new SightFacts { Perception = 8, Rotation = 3 };
        var next = new CellCoord(63, 10);
        Assert.AreEqual(new AwarenessResult(0, AwarenessReason.Perceived), host.SeeToward(f.A, Pc), "standing on its tile: the crate is off the line");
        Assert.AreEqual(AwarenessReason.Blocked, AwarenessRules.See(eyes, next, 0, 0, hero, map, CombatAnomalyProfile.Steamwoven).Reason,
            "a line from the next tile's centre meets the crate");

        // The NPC steps +x at once and slides over 400 ms: at the start of the step its body is still on its old tile, so its offset is
        // the negative projection of the step (+40, -20) and the line starts where the body is, clear of the crate.
        host.Frame(1000);
        host.Movers.Step(f.A, f.RowA.Key, Observer, next, 1000, 400);
        Assert.AreEqual(new AwarenessResult(0, AwarenessReason.Perceived), host.SeeToward(f.A, Pc), "at the start of the step the line is the one from the old tile");

        // At the end of the step the offset is the map data's own (0, 0) and the line starts at the next tile's centre.
        host.Frame(1400);
        Assert.AreEqual(new AwarenessResult(1, AwarenessReason.Blocked), host.SeeToward(f.A, Pc));
    }

    /// <summary>
    /// ARC-COMBAT-1934 (0x4ADE00, ledger RA7, AW18, VC56): the line of fire an NPC's attack, throw or cast reads starts at the shooter's tile centre plus its pixel offset, as the sight line does.
    /// </summary>
    [TestMethod]
    public void A_shooters_line_of_fire_starts_at_the_offset_of_its_map_data()
    {
        var hero = new CellCoord(62, 13);
        var (f, host, map) = Setup(hero);
        using var _ = f;
        map.Crates.Add(new(62, 11));
        map.Crates.Add(new(62, 12));
        var straight = host.LineOfFire(f.A, Pc);
        Assert.IsNotNull(straight.Blocker, "from the tile centre the crates are on the line");

        var (g, shifted, gMap) = Setup(hero, StepX.X, StepX.Y);
        using var __ = g;
        gMap.Crates.Add(new(62, 11));
        gMap.Crates.Add(new(62, 12));
        Assert.AreEqual((StepX.X, StepX.Y), shifted.ShooterOffset(g.A));
        var open = shifted.LineOfFire(g.A, Pc);
        Assert.IsNull(open.Blocker, "a line that starts one tile to the side passes the crates");
    }

    [TestMethod]
    public void A_shooters_offset_is_zero_for_the_hero_and_an_unknown_object()
    {
        var (f, host, _) = Setup(new CellCoord(62, 13), StepX.X, StepX.Y);
        using var _ = f;
        Assert.AreEqual((0, 0), host.ShooterOffset(Pc));
        Assert.AreEqual((0, 0), host.ShooterOffset("npc:unknown"));
    }

    [TestMethod]
    public void A_walking_shooter_starts_its_line_of_fire_where_its_body_is()
    {
        var (f, host, _) = Setup(new CellCoord(62, 13));
        using var _ = f;
        // The NPC steps +x at once and slides over 400 ms: at the start of the step its body is still on its old tile (the negative projection of the step), at the end the offset is the map data's own.
        host.Frame(1000);
        host.Movers.Step(f.A, f.RowA.Key, Observer, new CellCoord(63, 10), 1000, 400);
        Assert.AreEqual((40, -20), host.ShooterOffset(f.A));
        host.Frame(1400);
        Assert.AreEqual((0, 0), host.ShooterOffset(f.A));
    }

    /// <summary>ARC-AI-1605 (ledger AW18, VBG9): the hero's and a companion's sight, sound and fire lines start at the rest of the step they are walking, which the scene reports; none reported (a critter at rest, or no provider) is (0, 0) as before.</summary>
    [TestMethod]
    public void The_hero_and_a_companion_start_their_lines_at_the_step_offset_the_scene_reports()
    {
        var hero = new CellCoord(62, 13);
        var (f, host, map) = Setup(hero, step: id => id == Pc ? StepX : id == "companion:1" ? (40, -20) : null);
        using var _ = f;
        Assert.AreEqual(StepX, host.ShooterOffset(Pc));
        Assert.AreEqual((40, -20), host.ShooterOffset("companion:1"));
        Assert.AreEqual((0, 0), host.ShooterOffset("companion:2"), "a critter the scene does not track");
        map.Crates.Add(new(62, 11));
        map.Crates.Add(new(62, 12));
        Assert.IsNull(host.LineOfFire(Pc, f.A).Blocker, "the hero's line starts one tile to the side and passes the crates");

        var (g, still, gMap) = Setup(hero);
        using var __ = g;
        gMap.Crates.Add(new(62, 11));
        gMap.Crates.Add(new(62, 12));
        Assert.AreEqual((0, 0), still.ShooterOffset(Pc));
        Assert.IsNotNull(still.LineOfFire(Pc, g.A).Blocker, "from the tile centre the crates are on the line");
    }

    [TestMethod]
    public void The_step_offset_of_a_critter_is_the_projected_distance_of_its_anchor_from_the_committed_cell_centre()
    {
        var cell = new CellCoord(10, 20);
        Assert.AreEqual((0, 0), AreaCritterLights.StepOffset(ExactScalar.Integer(10), ExactScalar.Integer(20), cell), "at rest on the centre");
        Assert.AreEqual((-20, 10), AreaCritterLights.StepOffset(ExactScalar.Create(21, 2), ExactScalar.Integer(20), cell), "half a cell on in +x");
        Assert.AreEqual((20, 10), AreaCritterLights.StepOffset(ExactScalar.Integer(10), ExactScalar.Create(41, 2), cell), "half a cell on in +y");
        Assert.AreEqual((40, -20), AreaCritterLights.StepOffset(ExactScalar.Integer(10), ExactScalar.Integer(20), new CellCoord(11, 20)), "one cell short of the committed cell (the offset stands for the cell it has not left)");
    }
}
