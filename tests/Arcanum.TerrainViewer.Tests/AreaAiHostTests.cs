using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-092 (NPC AI slice I10, first fight): the AI host's movers (synthetic) and, on the installed original (opt-in,
/// <c>ARC_AREA_CONVERSATION_GAME_ROOT</c> and the cave's mobile folder in <c>ARC_CAVE_MOBILE</c>), the first fight in the Crash Site
/// cave: the hero hits an animal, which reacts, focuses the hero through the AI tick's danger update, starts its attack run and bites
/// back through the session's NPC hit step. Numbers only; no original text is asserted or printed.
/// </summary>
[TestClass]
public sealed class AreaAiHostTests
{
    private static readonly AreaObjectKey Rat = new("s", 1, AreaSourceKind.Mobile, 0, 0, 1, "rat", 1);

    [TestMethod]
    public void A_mover_steps_tile_by_tile_and_its_body_slides_over_the_step()
    {
        var movers = new AreaAiMovers();
        var origin = new CellCoord(100, 100);
        Assert.AreEqual(origin, movers.CellOf("rat", origin), "an NPC never moved stands on its map data's cell");
        Assert.AreEqual(0, movers.Moved.Count());

        movers.Step("rat", Rat, origin, new(101, 100), nowMs: 1000, stepMs: 500);
        var mover = movers.Of("rat")!;
        Assert.AreEqual((new CellCoord(101, 100), NeighborDirection.XPlus, (CellCoord?)origin), (mover.Cell, mover.Facing, mover.From));
        Assert.AreEqual(new CellCoord(101, 100), movers.CellOf("rat", origin), "the goal machine's next step finds it on the new cell");
        CollectionAssert.AreEquivalent(new[] { new CellCoord(101, 100), origin }, movers.Held().ToArray(), "a step holds both cells");
        Assert.AreEqual(0.5, mover.Progress(1250), 1e-9);

        // The T0 body slides: a step along +x moves the drawing by (−40, +20) pixels when done, half of it halfway.
        var frame = new AreaFrameKey(new(new(AreaSourceRole.Resource1), 1, 0, 1, 1, 0, new string('a', 64)), 0, 0, 0, "map", new string('a', 64), 1, new string('a', 64));
        var body = new AreaDrawCommand(default, frame, Rat, new TerrainRect(0, 0, 10, 10), new TerrainRect(500, 300, 10, 10), false, false, 0,
            origin.X, origin.Y, 0, 0, 255, false);
        Assert.AreEqual((480, 310), Destination(movers.Commands(key => key == Rat ? (AreaDrawCommand?)body : null, 1250).Single()));
        var done = movers.Commands(key => key == Rat ? (AreaDrawCommand?)body : null, 2000).Single();
        Assert.AreEqual((460, 320), Destination(done));
        Assert.AreEqual((101, 100), (done.MapX, done.MapY), "ordered at the cell it stands on");

        movers.Face("rat", Rat, origin, new(101, 99));
        Assert.AreEqual((NeighborDirection.YMinus, (CellCoord?)null), (movers.Of("rat")!.Facing, movers.Of("rat")!.From), "a turn ends the step's slide");
        movers.Clear();
        Assert.AreEqual(origin, movers.CellOf("rat", origin), "a reset stands it on its map data's cell again");
    }

    private static (int X, int Y) Destination(AreaDrawCommand command) => (command.Destination.X, command.Destination.Y);

    [TestMethod, TestCategory("RealData")]
    public void In_the_cave_an_animal_the_hero_hits_bites_back() => Fight(1);

    [TestMethod, TestCategory("RealData")]
    public void In_the_cave_an_animal_hit_from_afar_walks_up_and_bites() => Fight(4);

    // The hero stands `distance` tiles from the first living animal that has such a free admitted cell (straight along an axis).
    private static void Fight(int distance)
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        var cave = Environment.GetEnvironmentVariable("ARC_CAVE_MOBILE");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(cave))
            Assert.Inconclusive("ARC_AREA_CONVERSATION_GAME_ROOT and ARC_CAVE_MOBILE are required.");
        string P(string relative) => Path.Combine(root, relative);
        var module = P("modules/Arcanum.dat");
        using var data = AreaOriginalValidator.OpenCaveFiles(module, P("arcanum1.dat"), P("arcanum2.dat"), cave, P("data/proto"));
        var snapshot = data.LoadStrict();
        var steps = AreaStepInspector.ValidateOriginalData(snapshot, module, P("arcanum2.dat"), P("arcanum3.dat"), P("modules/Arcanum.PATCH0"), cave, 3);
        var inventory = AreaInventoryMap.Build(snapshot, steps).Map!;
        var combat = new AreaCombatHost(new AreaInventoryHost([inventory]));
        var table = AreaCompanionData.Open(module).Data?.Table ?? throw new AssertFailedException("AI parameter table");
        var map = snapshot.RuntimeMap;

        // The first living monstrous NPC with a free admitted cell beside it for the hero.
        var occupied = snapshot.Objects.Where(o => o.WorldX is not null).Select(o => (o.WorldX!.Value, o.WorldY!.Value)).ToHashSet();
        (string Entity, AreaObjectRecord Row, AreaStepCell Hero)? Pick()
        {
            foreach (var row in snapshot.Objects.Where(o => o.Type == 16 && o.WorldX is not null))
            {
                if (combat.Entity(map, row.Key) is not { } entity || combat.Build(map, entity).Combat is not { Monstrous: true } npc
                    || LifeRules.IsDead(npc.Loaded, npc.Facts)) continue;
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
                {
                    var cell = new AreaStepCell(row.WorldX!.Value + dx * distance, row.WorldY!.Value + dy * distance);
                    if (steps.IsAdmitted(cell) && !occupied.Contains((cell.X, cell.Y))) return (entity, row, cell);
                }
            }
            return null;
        }
        var (rat, ratRow, heroCell) = Pick() ?? throw new AssertFailedException("no animal in the cave");
        var hero = new CellCoord(checked((int)heroCell.X), checked((int)heroCell.Y));
        var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(), heroCell, map,
            AreaStartingHero.Load(module, AreaStartingHero.Premade[0])).Owner!;
        Assert.AreEqual(GameSessionFailure.None, session.AttachMapInventory(new SessionMapId(map), inventory.Seed).Failure);

        AreaAiHost? host = null;
        var npcs = snapshot.Objects.Where(o => o.Type == 16 && o.WorldX is not null && combat.Entity(map, o.Key) is not null)
            .Select(o => new AreaAiNpc(combat.Entity(map, o.Key)!, o, new(checked((int)o.WorldX!.Value), checked((int)o.WorldY!.Value)))).ToArray();
        var scene = new AreaAiScene
        {
            RuntimeMap = map, Chart = AreaChart.For(snapshot), Steps = steps, Npcs = npcs, Combat = combat,
            HeroCell = () => hero, Parameters = table,
            LineOfFire = () => new AreaLineOfFire(steps, snapshot.Objects, host!.Movers.Moved.ToHashSet(), new Dictionary<CellCoord, AreaObjectRecord>(),
                _ => false, row => combat.Entity(map, row.Key) is { } e && session.QueryNpcKilled(e), _ => null),
        };
        host = new AreaAiHost(session, scene, new AreaEventWorld(() => combat, () => null), new AreaCombatWorld(map, () => [], _ => null));
        Assert.AreEqual(0, host.ScheduleTicks(), "every living NPC of the cave gets its delayed tick");
        Assert.IsNotNull(session.QueryAiTick(rat));

        // The hero's hit and its attack event: the animal reacts and fights (or flees) the hero.
        if (distance == 1)
        {
            var hit = session.AttackCritter(session.Version, new SessionAttack(combat.Build(map, rat).Combat!));
            Assert.AreEqual(GameSessionFailure.None, hit.Failure);
        }
        host.HeroAttacked(rat);
        var danger = session.QueryDanger(rat);
        Trace($"reaction {host.TraceOf(rat)?.Reaction} setter {host.TraceOf(rat)?.Setter}");
        Assert.AreNotEqual(DangerState.None, danger.State, "the hit animal reacts");
        Assert.AreEqual(SessionCombatant.Hero, danger.State == DangerState.CombatFocus ? danger.Focus : danger.FleeingFrom);

        // The clock runs; the AI ticks fire through the host and the runs step on the session clock.
        var pump = new AreaEventPump(session, host);
        var notices = new List<AreaAiNotice>();
        var started = false;
        // A frame of 50 real milliseconds: the runs' animation clock; the game clock runs 8 times faster.
        for (var frame = 0; frame < 1200 && !notices.Any(n => n.Kind == AreaAiNoticeKind.Hit); frame++)
        {
            pump.Advance(8 * 50);
            host.Pump(50L * frame);
            // The attack goal replaces itself with the attempt when the target is in reach.
            started |= host.RunOf(rat) is { Bottom.Goal: AiGoalType.Attack or AiGoalType.AttemptAttack };
            notices.AddRange(host.DrainNotices().Where(n => n.Npc == rat));
        }
        var trace = host.TraceOf(rat);
        Trace($"danger {trace?.Danger} perform {trace?.Perform} step {trace?.Step} hit {trace?.Hit}");
        if (danger.State == DangerState.Flee) Assert.Inconclusive("this animal flees the hero: the fight is the next one's");
        Assert.IsTrue(started, "the pass's perform starts an attack run on the hero");
        Assert.IsTrue(notices.Any(n => n.Kind == AreaAiNoticeKind.Swing && n.Target == AiSchedulerRules.Pc), "the attack animation starts");
        var at = host.Tile(rat)!.Value;
        Assert.AreEqual(1, AiExecutorRules.Distance(at, hero), "it bites from the tile next to the hero");
        if (distance > 1) Assert.IsTrue(notices.Count(n => n.Kind == AreaAiNoticeKind.Step) >= distance - 1, "it walked up tile by tile");
        var bite = notices.First(n => n.Kind == AreaAiNoticeKind.Hit).Result!;
        Assert.AreNotEqual(GameSessionDisposition.Rejected, bite.Disposition, $"the session resolves the NPC's attack: {bite.Failure}");
        Assert.IsNotNull(bite.Resolution);
        Assert.IsTrue(session.QueryCombatMode(SessionCombatant.Critter(rat)), "the attempt puts the attacker into combat mode (goal 19 node 5)");
        Assert.IsTrue(session.QueryCombatMode(SessionCombatant.Hero), "and its PC target");
        Trace($"distance {distance} steps {notices.Count(n => n.Kind == AreaAiNoticeKind.Step)} bite {bite.Resolution!.Outcome} damage {bite.Life?.HitPointDamage} hero damage {session.QueryHeroLife()!.Record.State.HitPointDamage}");
    }

    private static void Trace(string line) => Console.WriteLine("ARC-WORLD-092 " + line);
}
