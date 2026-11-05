using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;
using Arcanum.Simulation.Goals;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-092 (NPC AI slice I10, first fight): the AI host's movers (synthetic) and, on the installed original (opt-in,
/// <c>ARC_AREA_CONVERSATION_GAME_ROOT</c> and the cave's mobile folder in <c>ARC_CAVE_MOBILE</c>), the first fight in the Crash Site
/// cave: the hero hits an animal, which reacts, focuses the hero through the AI tick's danger update, starts its attack run and bites
/// back through the session's NPC hit step; in a turn-based round (ARC-WORLD-095) it does so in its own subturns, paying action points.
/// Numbers only; no original text is asserted or printed.
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

    [TestMethod]
    public void A_restored_mover_stands_where_the_session_left_it_with_no_step_under_way()
    {
        // ARC-DEV-094 (MIG7c): the place the session recorded (the cell and facing the host last gave the NPC) comes back as a mover with no slide.
        var origin = new CellCoord(5, 5);
        var movers = new AreaAiMovers();
        var version = movers.Version;
        movers.Restore("rat", Rat, origin, new(9, 7), NeighborDirection.YPlus);
        Assert.IsTrue(movers.Version > version);
        Assert.AreEqual(new CellCoord(9, 7), movers.CellOf("rat", origin));
        Assert.AreEqual(NeighborDirection.YPlus, movers.FacingOf("rat", NeighborDirection.XPlus));
        Assert.IsTrue(movers.IsMoved("rat"));
        Assert.IsNull(movers.Of("rat")!.From);
        Assert.IsFalse(movers.Of("rat")!.Stepping(0));
        CollectionAssert.AreEqual(new[] { new CellCoord(9, 7) }, movers.Held().ToArray(), "it holds its cell, as a mover that finished its step");
        CollectionAssert.AreEqual(new[] { Rat }, movers.Moved.ToArray(), "its T0 record is the one moved");

        // A place on the map data's own cell (a turn) is a mover that is not moved: it keeps its facing only.
        movers.Restore("rat", Rat, origin, origin, NeighborDirection.XMinus);
        Assert.IsFalse(movers.IsMoved("rat"));
        Assert.AreEqual(NeighborDirection.XMinus, movers.FacingOf("rat", NeighborDirection.XPlus));
    }

    [TestMethod]
    public void A_settled_step_holds_one_cell_and_the_event_world_without_a_host_ends_the_tick()
    {
        var movers = new AreaAiMovers();
        var origin = new CellCoord(5, 5);
        movers.Step("rat", Rat, origin, new(6, 5), nowMs: 0, stepMs: 100);
        var version = movers.Version;
        movers.Settle(50);
        Assert.AreEqual(2, movers.Held().Count(), "a step under way holds both cells");
        movers.Settle(100);
        CollectionAssert.AreEqual(new[] { new CellCoord(6, 5) }, movers.Held().ToArray(), "a done slide holds the cell it reached");
        Assert.IsTrue(movers.Version > version && movers.IsMoved("rat"));
        movers.Face("rat", Rat, origin, new(6, 6));
        movers.Step("rat", Rat, origin, origin, nowMs: 200, stepMs: 100);
        movers.Settle(300);
        Assert.IsFalse(movers.IsMoved("rat"), "back on its map data's cell it is not moved");
        Assert.AreEqual(0, movers.Moved.Count());

        // ARC-WORLD-092: without an AI host the viewer's event world answers a tick so that it ends: the NPC is gone, its sector not loaded.
        var events = new AreaEventWorld(() => null, () => null);
        Assert.IsNull(events.AiLifeTarget("rat"));
        Assert.IsFalse(events.SectorLoaded("rat"));
        Assert.AreEqual(-1, events.DistanceToPc("rat"));
        Assert.IsTrue(events.RunScript(AiScriptPoint.Heartbeat, "rat", "rat"));
    }

    private static (int X, int Y) Destination(AreaDrawCommand command) => (command.Destination.X, command.Destination.Y);

    [TestMethod, TestCategory("RealData")]
    public void In_the_cave_an_animal_the_hero_hits_bites_back() => Fight(1);

    [TestMethod, TestCategory("RealData")]
    public void In_the_cave_an_animal_hit_from_afar_walks_up_and_bites() => Fight(4);

    // ARC-WORLD-105 (AC6): the AI gate's busy test reads the priority of the run's bottom goal (0x4233D0): an animal walking up to bite
    // is busy at its attack goal's priority 3, not its walk's 2, so its pass does not run mid-approach.
    [TestMethod, TestCategory("RealData")]
    public void An_animal_walking_up_to_bite_is_busy_at_its_attack_goals_priority()
    {
        var (session, host, _, _, rat, _, _, _) = Open(4);
        host.HeroAttacked(rat);
        if (session.QueryDanger(rat).State == DangerState.Flee) Assert.Inconclusive("this animal flees the hero");
        var pump = new FrameStep(session, host).WithAi(() => host);
        GoalRun? approach = null;
        for (var frame = 0; frame < 1200 && approach is null; frame++)
        {
            pump.AdvanceGame(8 * 50);
            host.DrainNotices();
            if (host.RunOf(rat) is { Bottom.Goal: GoalType.Attack } run && run.Frames.Length > 1) approach = run;
        }
        Assert.IsNotNull(approach, "the attack goal walks up under its approach");
        Trace($"approach {string.Join(",", approach.Frames.Select(f => f.Goal))} priority {host.AnimationPriority(rat)}");
        Assert.AreNotEqual(GoalType.Attack, approach.Top.Goal);
        Assert.AreEqual(3, host.AnimationPriority(rat), "the gate reads the bottom goal: busy (above 2 ends the pass)");
    }

    private sealed record Cave(GameSessionOwner Session, AreaAiHost Host, AreaCombatHost Combat, int Map, string Rat, AreaObjectRecord RatRow,
        CellCoord Hero, AreaCombatWorld Round)
    {
        /// <summary>ARC-RENDER-038: the attack ARTs the host asked the scene for (none is read here: the fixed timing plays).</summary>
        public List<(string Npc, AreaAiAttackArtRequest Wanted)> AttackArts { get; init; } = [];
    }

    // The hero stands `distance` tiles from the first living animal that has such a free admitted cell (straight along an axis).
    private static Cave Open(int distance)
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
        var attackArts = new List<(string Npc, AreaAiAttackArtRequest Wanted)>();
        var npcs = snapshot.Objects.Where(o => o.Type == 16 && o.WorldX is not null && combat.Entity(map, o.Key) is not null)
            .Select(o => new AreaAiNpc(combat.Entity(map, o.Key)!, o, new(checked((int)o.WorldX!.Value), checked((int)o.WorldY!.Value)))).ToArray();
        var scene = new AreaAiScene
        {
            RuntimeMap = map, Chart = AreaChart.For(snapshot), Steps = steps, Npcs = npcs, Combat = combat,
            HeroCell = () => hero, Parameters = table,
            AttackArt = (npc, wanted) => { attackArts.Add((npc.Entity, wanted)); return null; },
            LineOfFire = () => new AreaLineOfFire(steps, snapshot.Objects, host!.Movers.Moved.ToHashSet(), new Dictionary<CellCoord, AreaObjectRecord>(),
                _ => false, row => combat.Entity(map, row.Key) is { } e && session.QueryNpcKilled(e), _ => null),
        };
        // The round's scan: the hero and every NPC of the cave where it stands now, with its stored facts in its session life state.
        var round = new AreaCombatWorld(map,
            () => AreaCombatScan.Critters(npcs.Select(n => (n.Entity, n.Row.Key)), hero, e => host!.Tile(e), new AreaSectorEntries()),
            e => combat.Build(map, e).Combat is { } c && combat.Fields(map, e) is { } fields
                ? AreaCombatWorld.Facts(c, fields, session.QueryLife(c.Target).View?.Record.State ?? c.Loaded) : null);
        host = new AreaAiHost(session, scene, new AreaEventWorld(() => combat, () => null), round);
        Assert.AreEqual(0, host.ScheduleTicks(), "every living NPC of the cave gets its delayed tick");
        Assert.IsNotNull(session.QueryAiTick(rat));
        return new(session, host, combat, map, rat, ratRow, hero, round) { AttackArts = attackArts };
    }

    private static void Fight(int distance)
    {
        var cave = Open(distance);
        var (session, host, combat, map, rat, _, hero, _) = cave;

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
        var pump = new FrameStep(session, host).WithAi(() => host);
        var notices = new List<AreaAiNotice>();
        var started = false;
        // A frame of 50 real milliseconds: the runs' animation clock; the game clock runs 8 times faster.
        for (var frame = 0; frame < 1200 && !notices.Any(n => n.Kind == AreaAiNoticeKind.Hit); frame++)
        {
            pump.AdvanceGame(8 * 50);
            // The attack goal replaces itself with the attempt when the target is in reach.
            started |= host.RunOf(rat) is { Bottom.Goal: GoalType.Attack or GoalType.AttemptAttack };
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
        // ARC-RENDER-038: the bite asks for the attack (20) with bare hands (WA1, WA4: the standing hero is a critter of height 1), plays with
        // the fixed timing when no ART is read, and the animal shows bare hands in combat mode.
        Assert.IsTrue(cave.AttackArts.Contains((rat, new AreaAiAttackArtRequest(20, 1, 0))), string.Join(", ", cave.AttackArts));
        var swing = notices.First(n => n.Kind == AreaAiNoticeKind.Swing);
        Assert.IsNotNull(swing.Play);
        Assert.IsNull(swing.Play.Art);
        Assert.AreEqual(9, swing.Play.Moments.Count(m => m.Kind != AttackUpdateKind.Ended) + 1, "the fixed human attack ART's 9 frames");
        Assert.AreEqual((1, 0), host.ShownWeapon(rat));
        // ARC-WORLD-099: its attack run ends done, and an NPC in combat mode outside a round then starts its fidget (0x4B4320).
        var fidget = false;
        for (var frame = 1200; frame < 1600 && !fidget; frame++)
        {
            pump.AdvanceGame(8 * 50);
            fidget = host.DrainNotices().Any(n => n.Npc == rat && n.Kind == AreaAiNoticeKind.Fidget);
        }
        Assert.IsTrue(fidget, "a fidget follows its done attack run");
        Trace($"distance {distance} steps {notices.Count(n => n.Kind == AreaAiNoticeKind.Step)} bite {bite.Resolution!.Outcome} damage {bite.Life?.HitPointDamage} hero damage {session.QueryHeroLife()!.Record.State.HitPointDamage}");
    }

    [TestMethod, TestCategory("RealData")]
    public void In_a_turn_based_round_the_animal_walks_up_and_bites_in_its_own_subturns()
    {
        var (session, host, _, _, rat, _, hero, world) = Open(4);
        var critter = SessionCombatant.Critter(rat);
        host.HeroAttacked(rat);
        var danger = session.QueryDanger(rat);
        if (danger.State != DangerState.CombatFocus) Assert.Inconclusive($"this animal does not fight the hero: {danger.State}");
        // The hero enters combat mode with the turn-based setting: the round begins, the animal first (the party goes last).
        var began = session.SetCombatMode(session.Version, SessionCombatant.Hero, true, new SessionCombatModeContext { TurnBased = true, World = world });
        Assert.AreEqual(GameSessionFailure.None, began.Failure);
        Assert.IsNotNull(session.QueryCombatRound(), "the round began");
        Assert.IsTrue(session.QueryCombatRound()!.Participants.Contains(critter), "the animal takes part");

        var pump = new FrameStep(session, host).WithAi(() => host);
        var notices = new List<AreaAiNotice>();
        var events = new List<SessionCombatEvent>();
        var held = 0; var heroTurns = 0; var paid = false; var offTurn = false; var pending = false; var pendingAtHero = new List<int>();
        int? lastPoints = null;
        AreaAiNotice? bite = null;
        for (var frame = 0; frame < 4000 && bite is null; frame++)
        {
            pump.AdvanceGame(8 * 50);
            var actorBefore = session.QueryCombatRound()?.Actor;
            var drained = host.DrainNotices().Where(n => n.Npc == rat).ToArray();
            // The animal moves and swings only in its own subturn (a swing that began in it may land after it, as an animation does).
            offTurn |= drained.Any(n => n.Kind is AreaAiNoticeKind.Step or AreaAiNoticeKind.Swing) && actorBefore != critter;
            notices.AddRange(drained);
            bite ??= drained.FirstOrDefault(n => n.Kind == AreaAiNoticeKind.Hit);
            events.AddRange(host.DrainRoundEvents());
            var step = AreaTurnBased.Step(session, world, heroIdle: true, host);
            Assert.AreEqual(GameSessionFailure.None, step.Failure, $"frame {frame}");
            events.AddRange(step.Events);
            var now = session.QueryCombatRound() ?? throw new AssertFailedException($"the round ended at frame {frame}");
            if (now.Actor == critter)
            {
                if (host.RunOf(rat) is not null) held++;
                pending |= host.PendingCost > 0;
                paid |= lastPoints is { } before && now.ActionPoints < before;
                lastPoints = now.ActionPoints;
            }
            else lastPoints = null;
            if (now.Actor.IsHero)
            {
                // The hero passes its subturn at once; the pending cost the animal's checks wrote reads 0 in it.
                heroTurns++;
                pendingAtHero.Add(host.PendingCost);
                Assert.AreEqual(GameSessionFailure.None, session.EndCombatTurn(session.Version, SessionCombatant.Hero, world).Failure);
            }
        }
        Console.WriteLine($"ARC-WORLD-095 turn-based: frames held {held} hero turns {heroTurns} steps {notices.Count(n => n.Kind == AreaAiNoticeKind.Step)} bite {bite?.Result?.Resolution?.Outcome} round {session.QueryCombatRound()?.Turn}");
        Assert.IsNotNull(bite, "the animal bites in a later subturn of the round");
        Assert.AreNotEqual(GameSessionDisposition.Rejected, bite!.Result!.Disposition, $"the session resolves the bite: {bite.Result.Failure}");
        Assert.AreEqual(1, AiExecutorRules.Distance(host.Tile(rat)!.Value, hero), "it bites from the tile next to the hero");
        Assert.IsTrue(notices.Count(n => n.Kind == AreaAiNoticeKind.Step) >= 3, "it walked up tile by tile");
        Assert.IsFalse(offTurn, "it never walked or swung on another participant's subturn");
        Assert.IsTrue(held >= 2, "its goal run held its subturn over frames");
        Assert.IsTrue(paid, "its steps and its attack paid the round's action points");
        Assert.IsTrue(heroTurns >= 1, "its subturns passed on to the hero's");
        Assert.IsTrue(pending, "its checks wrote the pending cost");
        Assert.IsTrue(pendingAtHero.All(p => p == 0), "the pending cost reads 0 after a subturn start");
        Assert.IsTrue(events.Any(e => e.Kind == SessionCombatEventKind.SubturnEnded && e.Combatant == critter), "its subturn ended");
        Assert.IsTrue(host.TraceOf(rat)!.EndCombatScripts > 0, "its subturn ends asked its end-combat script (point 16, ARC-WORLD-099)");
        Assert.IsFalse(notices.Any(n => n.Kind == AreaAiNoticeKind.Fidget), "no fidget starts in a round");
    }

    [TestMethod, TestCategory("RealData")]
    public void A_turn_set_that_interrupts_the_last_run_runs_the_completion_check()
    {
        var (session, host, _, _, rat, _, _, world) = Open(4);
        var critter = SessionCombatant.Critter(rat);
        host.HeroAttacked(rat);
        if (session.QueryDanger(rat).State != DangerState.CombatFocus) Assert.Inconclusive($"this animal does not fight the hero: {session.QueryDanger(rat).State}");
        Assert.AreEqual(GameSessionFailure.None, session.SetCombatMode(session.Version, SessionCombatant.Hero, true,
            new SessionCombatModeContext { TurnBased = true, World = world }).Failure);
        // The round goes on (the runs stepping, the hero passing its subturns) until the animal's subturn hook starts its walk up, which
        // holds the subturn.
        var pump = new FrameStep(session, host).WithAi(() => host);
        for (var frame = 0; frame < 4000 && !(session.QueryCombatRound()?.Actor == critter && host.RunOf(rat) is not null); frame++)
        {
            pump.AdvanceGame(8 * 50);
            Assert.AreEqual(GameSessionFailure.None, AreaTurnBased.Step(session, world, heroIdle: true, host).Failure);
            if (session.QueryCombatRound() is { Actor.IsHero: true })
                Assert.AreEqual(GameSessionFailure.None, session.EndCombatTurn(session.Version, SessionCombatant.Hero, world).Failure);
        }
        Assert.AreEqual(critter, session.QueryCombatRound()?.Actor, "the animal's subturn");
        Assert.IsNotNull(host.RunOf(rat), "its hook started a run");
        // Its subturn ends from outside while the run goes on: the next turn set interrupts that run, the last one, and the completion
        // check follows for the new participant (TH10); the round goes on.
        Assert.AreEqual(GameSessionFailure.None, session.EndCombatTurn(session.Version, critter, world).Failure);
        var checks = host.CompletionChecks;
        host.ObserveRound();
        Assert.IsNull(host.RunOf(rat), "the turn set interrupted its run");
        Assert.AreEqual(checks + 1, host.CompletionChecks, "the free of the last run ran the completion check");
        Assert.AreEqual(GameSessionFailure.None, host.CheckFailure);
        Assert.IsNotNull(session.QueryCombatRound());
        host.ObserveRound();
        Assert.AreEqual(checks + 1, host.CompletionChecks, "once");
    }

    private static void Trace(string line) => Console.WriteLine("ARC-WORLD-092 " + line);
}
