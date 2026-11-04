using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-102 (NPC AI slice I10, the casting host): on the installed original (opt-in, <c>ARC_AREA_CONVERSATION_GAME_ROOT</c> and
/// the Crash Site exterior's mobile folder in <c>ARC_AREA_MOBILE</c>) the exterior's NPC with a magick college level, told of the
/// hero's attack, chooses a spell at its danger update, casts it through the map's cast host and plays its casting goal: the run
/// starts, the release begins it and the goal's end leaves no run behind. Numbers only; no original text is asserted or printed.
/// </summary>
[TestClass]
public sealed class AreaAiCastTests
{
    private sealed record Crash(GameSessionOwner Session, AreaAiHost Host, SessionSpellCaster Caster, string Npc, int[] Known, AreaCombatWorld Round,
        AreaEventWorld Events) : IDisposable
    {
        public void Dispose() => Session.Dispose();
        /// <summary>ARC-WORLD-105: the cast host the scene answers (the viewer rebuilds its host on a map change).</summary>
        public System.Runtime.CompilerServices.StrongBox<SessionSpellCaster> Spells { get; init; } = new(Caster);
        /// <summary>A new cast host over the same session and map, as a rebuilt one.</summary>
        public Func<SessionSpellCaster>? Rebuild { get; init; }
    }

    // The exterior's highest-level NPC whose fields store a college level (the monster; Virgil is the other), the hero a few tiles away.
    private static Crash Open(Func<string, int, CastEyeCandyAnimation?>? eyeCandy = null, Func<int, bool>? missileArt = null)
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        var mobile = Environment.GetEnvironmentVariable("ARC_AREA_MOBILE");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(mobile))
            Assert.Inconclusive("ARC_AREA_CONVERSATION_GAME_ROOT and ARC_AREA_MOBILE are required.");
        string P(string relative) => Path.Combine(root, relative);
        var module = P("modules/Arcanum.dat");
        using var data = AreaOriginalValidator.OpenFiles(module, P("arcanum1.dat"), P("arcanum2.dat"), mobile, P("data/proto"));
        var snapshot = data.LoadStrict();
        var steps = AreaStepInspector.ValidateOriginalData(snapshot, module, P("arcanum2.dat"), P("arcanum3.dat"), P("modules/Arcanum.PATCH0"), mobile, 1);
        var inventory = AreaInventoryMap.Build(snapshot, steps).Map!;
        var combat = new AreaCombatHost(new AreaInventoryHost([inventory]));
        var table = AreaCompanionData.Open(module).Data?.Table ?? throw new AssertFailedException("AI parameter table");
        var spellData = AreaSpellsPage.LoadData(module).Data ?? throw new AssertFailedException("spell data");
        var records = Enumerable.Range(0, SpellRecord.Count).Select(n => SessionSpellRecords.Translate(spellData.Records[n])).ToArray();
        var map = snapshot.RuntimeMap;

        var occupied = snapshot.Objects.Where(o => o.WorldX is not null).Select(o => (o.WorldX!.Value, o.WorldY!.Value)).ToHashSet();
        var casters = snapshot.Objects.Where(o => o.Type == 16 && o.WorldX is not null && combat.Entity(map, o.Key) is { } e
                && combat.Build(map, e).Combat is { } c && !LifeRules.IsDead(c.Loaded, c.Facts)
                && combat.Fields(map, e)?.Critter?.SpellTech.Take(SpellBookRules.Colleges).Any(rank => rank > 0) == true)
            .ToArray();
        Trace($"college NPCs {string.Join(",", casters.Select(o => o.PrototypeNumber))}");
        var casterRow = casters.OrderByDescending(o => combat.Build(map, combat.Entity(map, o.Key)!).Combat!.Level).FirstOrDefault()
            ?? throw new AssertFailedException("no college NPC");
        var npc = combat.Entity(map, casterRow.Key)!;
        AreaStepCell? heroCell = null;
        foreach (var (dx, dy) in new[] { (4, 0), (-4, 0), (0, 4), (0, -4), (3, 0), (-3, 0), (0, 3), (0, -3), (5, 0), (-5, 0), (0, 5), (0, -5) })
        {
            var cell = new AreaStepCell(casterRow.WorldX!.Value + dx, casterRow.WorldY!.Value + dy);
            if (steps.IsAdmitted(cell) && !occupied.Contains((cell.X, cell.Y))) { heroCell = cell; break; }
        }
        Assert.IsNotNull(heroCell, "a free admitted cell a few tiles from the caster");
        var hero = new CellCoord(checked((int)heroCell.Value.X), checked((int)heroCell.Value.Y));
        var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(), heroCell.Value, map,
            AreaStartingHero.Load(module, AreaStartingHero.Premade[0])).Owner!;
        Assert.AreEqual(GameSessionFailure.None, session.AttachMapInventory(new SessionMapId(map), inventory.Seed).Failure);

        AreaAiHost? host = null;
        var lineOfFire = () => new AreaLineOfFire(steps, snapshot.Objects, host?.Movers.Moved.ToHashSet() ?? [], new Dictionary<CellCoord, AreaObjectRecord>(),
            _ => false, row => combat.Entity(map, row.Key) is { } e && session.QueryNpcKilled(e), _ => null);
        // The cast map sees the NPCs the AI walked on their cells, as the viewer's does.
        var spellMap = new AreaSpellCastMap(session, map, steps, snapshot.Objects, combat, () => hero, lineOfFire,
            placed: () => host?.Movers.All.ToDictionary(m => m.Key, m => ((long X, long Y)?)(m.Cell.X, m.Cell.Y)) ?? []);
        var npcs = snapshot.Objects.Where(o => o.Type == 16 && o.WorldX is not null && combat.Entity(map, o.Key) is not null)
            .Select(o => new AreaAiNpc(combat.Entity(map, o.Key)!, o, new(checked((int)o.WorldX!.Value), checked((int)o.WorldY!.Value)))).ToArray();
        var round = new AreaCombatWorld(map,
            () => AreaCombatScan.Critters(npcs.Select(n => (n.Entity, n.Row.Key)), hero, e => host!.Tile(e), new AreaSectorEntries()),
            e => combat.Build(map, e).Combat is { } c && combat.Fields(map, e) is { } fields
                ? AreaCombatWorld.Facts(c, fields, session.QueryLife(c.Target).View?.Record.State ?? c.Loaded) : null);
        var events = new AreaEventWorld(() => combat, () => null, ai: () => host, round: () => round);
        var caster = new SessionSpellCaster(session, n => n is >= 0 and < SpellRecord.Count ? records[n] : null, spellMap, events);
        var book = caster.Book(npc)!;
        var known = Enumerable.Range(0, SpellBookRules.Spells).Where(s => SpellBookRules.IsKnown(book, s)).ToArray();
        Trace($"caster {casterRow.PrototypeNumber} knows {string.Join(",", known)}; lists "
            + string.Join(" ", Enum.GetValues<AiActionList>().Select(l => $"{l}:{string.Join(",", caster.AiLists[l].Where(known.Contains))}")));
        var spells = new System.Runtime.CompilerServices.StrongBox<SessionSpellCaster>(caster);
        var scene = new AreaAiScene
        {
            RuntimeMap = map, Chart = AreaChart.For(snapshot), Steps = steps, Npcs = npcs, Combat = combat, HeroCell = () => hero, Parameters = table,
            LineOfFire = lineOfFire, Spells = () => spells.Value, CastEyeCandy = eyeCandy, MissileArt = missileArt,
        };
        host = new AreaAiHost(session, scene, events, round);
        Assert.AreEqual(0, host.ScheduleTicks());
        // The hero's attack event: the caster reacts and fights the hero.
        host.HeroAttacked(npc);
        Trace($"reaction {host.TraceOf(npc)?.Reaction} setter {host.TraceOf(npc)?.Setter}");
        Assert.AreEqual(DangerState.CombatFocus, session.QueryDanger(npc).State, "the caster fights the hero");
        return new(session, host, caster, npc, known, round, events)
        {
            Spells = spells,
            Rebuild = () => new SessionSpellCaster(session, n => n is >= 0 and < SpellRecord.Count ? records[n] : null, spellMap, events),
        };
    }

    [TestMethod, TestCategory("RealData")]
    public void On_the_crash_site_the_college_npc_told_of_an_attack_casts_its_spell_through_the_cast_host()
    {
        using var crash = Open();
        var (session, host, caster, npc, known, _, events) = crash;
        // The AI ticks fire and the runs step until a casting goal's end.
        var pump = new AreaEventPump(session, events, spells: () => caster);
        var notices = new List<AreaAiNotice>();
        var choices = new HashSet<string>();
        for (var frame = 0; frame < 4000 && !notices.Any(n => n.Kind == AreaAiNoticeKind.CastEnd); frame++)
        {
            pump.Advance(8 * 50);
            host.Pump(50L * frame);
            if (host.TraceOf(npc)?.Spell is { } spell) choices.Add(spell);
            notices.AddRange(host.DrainNotices().Where(n => n.Npc == npc));
        }
        var trace = host.TraceOf(npc);
        Trace($"choices {string.Join(" | ", choices)}");
        Trace($"danger {trace?.Danger} perform {trace?.Perform} step {trace?.Step} cast {trace?.Cast} hit {trace?.Hit}");
        var play = notices.FirstOrDefault(n => n.Kind == AreaAiNoticeKind.Cast) ?? throw new AssertFailedException("no casting goal played");
        Assert.IsTrue(known.Contains(play.Spell), "the cast spell is one the caster knows");
        Assert.IsFalse(play.CastSwing.Plays, "without the eye-candy table no play: the release comes at once");
        var release = notices.FirstOrDefault(n => n.Kind == AreaAiNoticeKind.Release && n.Run == play.Run) ?? throw new AssertFailedException("no release");
        Trace($"spell {play.Spell} at {play.Target} released {release.Released} {release.SpellRun?.Disposition}; hero hp {session.QueryHeroLife()!.Status.HitPoints};"
            + $" log {string.Join(",", caster.Log.Select(e => $"{e.Kind}:{e.Value}"))}");
        Assert.IsTrue(release.Released, "the release began the run");
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.FatiguePaid), "the caster paid the cast");
        Assert.AreNotEqual(SpellRunAction.Begin, session.QuerySpellRuns()[play.Run]?.Action ?? SpellRunAction.End, "its BEGIN ran");
        Assert.IsTrue(notices.Any(n => n.Kind == AreaAiNoticeKind.CastEnd && n.Run == play.Run && n.SpellRun is null),
            "the goal's end after the release frees nothing");
    }

    [TestMethod, TestCategory("RealData")]
    public void In_a_turn_based_round_the_college_npc_casts_in_its_own_subturn_and_pays_4_action_points()
    {
        using var crash = Open();
        var (session, host, caster, npc, _, world, events) = crash;
        var critter = SessionCombatant.Critter(npc);
        var began = session.SetCombatMode(session.Version, SessionCombatant.Hero, true, new SessionCombatModeContext { TurnBased = true, World = world });
        Assert.AreEqual(GameSessionFailure.None, began.Failure);
        Assert.IsTrue(session.QueryCombatRound()?.Participants.Contains(critter) == true, "the caster takes part in the round");
        var pump = new AreaEventPump(session, events, spells: () => caster);
        AreaAiNotice? play = null;
        var offTurn = false; var paid = -1;
        for (var frame = 0; frame < 4000 && play is null; frame++)
        {
            pump.Advance(8 * 50);
            var before = session.QueryCombatRound();
            host.Pump(50L * frame);
            var drained = host.DrainNotices().Where(n => n.Npc == npc).ToArray();
            if (drained.FirstOrDefault(n => n.Kind == AreaAiNoticeKind.Cast) is { } cast)
            {
                play = cast;
                offTurn = before?.Actor != critter;
                // The start's 4 action points (0x429B50), unless the run stepped further on this frame.
                if (before is { } b && session.QueryCombatRound() is { } after && after.Actor == critter) paid = b.ActionPoints - after.ActionPoints;
            }
            host.DrainRoundEvents();
            var step = AreaTurnBased.Step(session, world, heroIdle: true, host);
            Assert.AreEqual(GameSessionFailure.None, step.Failure, $"frame {frame}");
            if (session.QueryCombatRound() is { Actor.IsHero: true })
                Assert.AreEqual(GameSessionFailure.None, session.EndCombatTurn(session.Version, SessionCombatant.Hero, world).Failure);
        }
        Trace($"turn-based cast {play?.Spell} paid {paid} cast {host.TraceOf(npc)?.Cast}");
        Assert.IsNotNull(play, "the caster casts in the round");
        Assert.IsFalse(offTurn, "in its own subturn");
        Assert.AreEqual(TurnBasedRules.CastSpellCost, paid, "the casting goal's start paid the round's 4 action points");
    }

    [TestMethod, TestCategory("RealData")]
    public void A_cast_eye_candy_times_the_goal_and_a_host_retired_mid_play_frees_the_run()
    {
        // An invented cast eye candy: 8 frames at 10 fps (a 100 ms pace), action frame 4. Frame k shows at (k − 1)·100 ms up to the
        // release, so the release is at 300 ms; after it frames 5..7 come at once and one pace apart, the last frame ends the goal one
        // pace later: at 700 ms, a rest of 400 ms.
        using var crash = Open((_, _) => new CastEyeCandyAnimation(new EyeCandyClip(8, 4, 10), null, timesRelease: true, 0));
        var (session, host, caster, npc, _, _, events) = crash;
        var pump = new AreaEventPump(session, events, spells: () => caster);
        AreaAiNotice? play = null;
        for (var frame = 0; frame < 4000 && play is null; frame++)
        {
            pump.Advance(8 * 50);
            host.Pump(50L * frame);
            play = host.DrainNotices().FirstOrDefault(n => n.Npc == npc && n.Kind == AreaAiNoticeKind.Cast);
        }
        Assert.IsNotNull(play, "a casting goal played");
        Assert.AreEqual(new AiCastSwing(true, 300, 400), play.CastSwing, "the release frame and the rest of the play");
        Assert.AreEqual(AiGoalType.AttemptSpell, host.RunOf(npc)?.Top.Goal, "the play holds the attempt goal until its action frame");
        Assert.AreEqual(SpellRunAction.Begin, session.QuerySpellRuns()[play.Run]?.Action, "the run waits for its release");
        // The viewer drops the host mid-play (a follower joins, the AI is turned off): its runs end through their end nodes, so the
        // attempt goal's end (0x429CD0) frees the run with its BEGIN charged (MG39) and no slot is left behind (AC4).
        host.Retire();
        Assert.IsNull(host.RunOf(npc));
        Assert.IsNull(session.QuerySpellRuns()[play.Run], "the retired host's casting goal freed its run");
        Assert.IsFalse(caster.AiUnderInfluence(AiSchedulerRules.Pc, play.Spell), "nothing keeps the hero under the spell's influence");
        Trace($"retired mid-play: run {play.Run} freed");
    }

    [TestMethod, TestCategory("RealData")]
    public void A_play_that_never_reaches_its_action_frame_releases_nothing_and_its_end_frees_the_run()
    {
        // Action frame 0 is never reached (EC7): the play runs its 3 frames and the goal ends without a release.
        using var crash = Open((_, _) => new CastEyeCandyAnimation(new EyeCandyClip(3, 0, 10), null, timesRelease: true, 0));
        var (session, host, caster, npc, _, _, events) = crash;
        var pump = new AreaEventPump(session, events, spells: () => caster);
        var notices = new List<AreaAiNotice>();
        for (var frame = 0; frame < 4000 && !notices.Any(n => n.Kind == AreaAiNoticeKind.CastEnd); frame++)
        {
            pump.Advance(8 * 50);
            host.Pump(50L * frame);
            notices.AddRange(host.DrainNotices().Where(n => n.Npc == npc));
        }
        var play = notices.FirstOrDefault(n => n.Kind == AreaAiNoticeKind.Cast) ?? throw new AssertFailedException("no casting goal played");
        Assert.IsTrue(play.CastSwing.Plays);
        Assert.IsFalse(notices.Any(n => n.Kind == AreaAiNoticeKind.Release && n.Run == play.Run), "no release frame");
        var end = notices.First(n => n.Kind == AreaAiNoticeKind.CastEnd && n.Run == play.Run);
        Assert.IsNotNull(end.SpellRun, "the goal's end before a release frees the run (0x429CD0, its BEGIN charged)");
        Assert.IsNull(session.QuerySpellRuns()[play.Run]);
    }

    // ---- ARC-WORLD-104: the queued cast (SU13, AC5) ----

    // The first casting goal's play (the invented eye candy: release at 300 ms, end at 700 ms), the run holding its attempt goal.
    private static AreaAiNotice PlayFirstCast(Crash crash, AreaEventPump pump, ref int frame)
    {
        AreaAiNotice? play = null;
        for (; frame < 4000 && play is null; frame++)
        {
            pump.Advance(8 * 50);
            crash.Host.Pump(50L * frame);
            play = crash.Host.DrainNotices().FirstOrDefault(n => n.Npc == crash.Npc && n.Kind == AreaAiNoticeKind.Cast);
        }
        Assert.IsNotNull(play, "a casting goal played");
        Assert.AreEqual(AiGoalType.AttemptSpell, crash.Host.RunOf(crash.Npc)?.Top.Goal, "the play holds the attempt goal");
        return play;
    }

    private static int[] RunsOf(GameSessionOwner session, string npc) =>
        [.. session.QuerySpellRuns().Runs.Where(r => r.Parent?.Id == npc).Select(r => r.Id)];

    private static CastEyeCandyAnimation EightFrames() => new(new EyeCandyClip(8, 4, 10), null, timesRelease: true, 0);

    [TestMethod, TestCategory("RealData")]
    public void A_cast_while_the_casting_goal_plays_is_queued_beneath_it_and_the_queue_plays_in_the_order_cast()
    {
        using var crash = Open((_, _) => EightFrames());
        var (session, host, caster, npc, _, _, events) = crash;
        var pump = new AreaEventPump(session, events, spells: () => caster);
        var frame = 0;
        var play = PlayFirstCast(crash, pump, ref frame);
        var choice = new AiPassChoice(AiPassAction.Cast, play.Spell, 0, null, AiSchedulerRules.Pc);
        var queued = new List<int>();
        for (var i = 0; i < 2; i++)
        {
            var start = host.Cast(npc, choice);
            Trace104($"queue {i}: {host.TraceOf(npc)?.Cast}");
            Assert.IsTrue(start.Queued, "queued beneath the playing goal (SU13)");
            Assert.IsNull(start.Run, "no run starts: perform keeps the current one");
            queued.Add(host.RunOf(npc)!.Bottom.SpellRun!.Value);
        }
        var run = host.RunOf(npc)!;
        CollectionAssert.AreEqual(new[] { AiGoalType.CastSpell, AiGoalType.CastSpell, AiGoalType.AttemptSpell }, run.Frames.Select(f => f.Goal).ToArray(),
            "each queued goal goes to the bottom; the playing goal stays current");
        CollectionAssert.AreEqual(new int?[] { queued[1], queued[0], play.Run }, run.Frames.Select(f => f.SpellRun).ToArray());
        Assert.AreEqual(SpellRunAction.Begin, session.QuerySpellRuns()[queued[0]]?.Action, "a queued run waits for its own release");

        var notices = new List<AreaAiNotice>();
        for (; frame < 8000 && host.RunOf(npc) is not null; frame++)
        {
            pump.Advance(8 * 50);
            host.Pump(50L * frame);
            notices.AddRange(host.DrainNotices().Where(n => n.Npc == npc));
        }
        var plays = notices.Where(n => n.Kind == AreaAiNoticeKind.Cast).Select(n => n.Run).ToArray();
        Trace104($"plays after the first: {string.Join(",", plays)}; releases {string.Join(",", notices.Where(n => n.Kind == AreaAiNoticeKind.Release).Select(n => $"{n.Run}:{n.Released}"))}");
        CollectionAssert.AreEqual(queued.ToArray(), plays, "the queued goals play in the order they were cast");
        foreach (var id in queued)
            Assert.IsTrue(notices.Any(n => n.Kind == AreaAiNoticeKind.Release && n.Run == id && n.Released), $"queued run {id} was released");
        Assert.AreEqual(0, RunsOf(session, npc).Count(id => session.QuerySpellRuns()[id]?.Action == SpellRunAction.Begin), "no run waits for a BEGIN");
    }

    [TestMethod, TestCategory("RealData")]
    public void The_queue_refuses_at_depth_4_and_in_a_turn_based_round_freeing_the_run_and_a_retired_host_frees_the_queue()
    {
        using var crash = Open((_, _) => EightFrames());
        var (session, host, caster, npc, _, world, events) = crash;
        var pump = new AreaEventPump(session, events, spells: () => caster);
        var frame = 0;
        var play = PlayFirstCast(crash, pump, ref frame);
        var choice = new AiPassChoice(AiPassAction.Cast, play.Spell, 0, null, AiSchedulerRules.Pc);
        // Depths 0..3 queue; at 4 (the playing goal's current index, QC2) the run is freed.
        for (var depth = 0; depth < SpellUiRules.QueueRefusedFromDepth; depth++)
            Assert.IsTrue(host.Cast(npc, choice).Queued, $"depth {depth} queues: {host.TraceOf(npc)?.Cast}");
        var before = RunsOf(session, npc);
        var refused = host.Cast(npc, choice);
        Trace104($"depth 4: {host.TraceOf(npc)?.Cast}");
        Assert.IsFalse(refused.Queued);
        Assert.IsNull(refused.Run);
        Assert.AreEqual(SpellUiRules.QueueRefusedFromDepth + 1, host.RunOf(npc)!.Frames.Length, "nothing was inserted");
        CollectionAssert.AreEquivalent(before, RunsOf(session, npc), "the refused cast's run was freed");

        // The retired host ends the run: the playing goal's end frees its run, the never-played queued runs are freed unpaid (MG43).
        host.Retire();
        Assert.AreEqual(0, RunsOf(session, npc).Length, "no run of the NPC is left");

        // A turn-based round refuses the queue (0x4B6D70, any round).
        using var round = Open((_, _) => EightFrames());
        var roundPump = new AreaEventPump(round.Session, round.Events, spells: () => round.Caster);
        var roundFrame = 0;
        var first = PlayFirstCast(round, roundPump, ref roundFrame);
        Assert.AreEqual(GameSessionFailure.None, round.Session.SetCombatMode(round.Session.Version, SessionCombatant.Hero, true,
            new SessionCombatModeContext { TurnBased = true, World = round.Round }).Failure);
        var held = RunsOf(round.Session, round.Npc);
        var inRound = round.Host.Cast(round.Npc, new AiPassChoice(AiPassAction.Cast, first.Spell, 0, null, AiSchedulerRules.Pc));
        Trace104($"in a round: {round.Host.TraceOf(round.Npc)?.Cast}");
        Assert.IsFalse(inRound.Queued);
        CollectionAssert.AreEquivalent(held, RunsOf(round.Session, round.Npc), "the run was freed");
        Assert.AreEqual(1, round.Host.RunOf(round.Npc)!.Frames.Length);
    }

    [TestMethod, TestCategory("RealData")]
    public void A_queued_goal_that_ends_through_the_end_step_drops_the_casts_beneath_it_and_frees_their_runs()
    {
        // The first cast plays its eye candy; the later ones have none, so the first queued goal releases at once and takes the end
        // step (QC4), which leaves the second queued cast unplayed: its run is freed unpaid (MG43, both modes).
        var calls = 0;
        using var crash = Open((_, _) => calls++ == 0 ? EightFrames() : null);
        var (session, host, caster, npc, _, _, events) = crash;
        var pump = new AreaEventPump(session, events, spells: () => caster);
        var frame = 0;
        var play = PlayFirstCast(crash, pump, ref frame);
        var choice = new AiPassChoice(AiPassAction.Cast, play.Spell, 0, null, AiSchedulerRules.Pc);
        Assert.IsTrue(host.Cast(npc, choice).Queued);
        var firstQueued = host.RunOf(npc)!.Bottom.SpellRun!.Value;
        Assert.IsTrue(host.Cast(npc, choice).Queued);
        var secondQueued = host.RunOf(npc)!.Bottom.SpellRun!.Value;
        var notices = new List<AreaAiNotice>();
        for (; frame < 8000 && host.RunOf(npc) is not null; frame++)
        {
            pump.Advance(8 * 50);
            host.Pump(50L * frame);
            notices.AddRange(host.DrainNotices().Where(n => n.Npc == npc));
        }
        Trace104($"step {host.TraceOf(npc)?.Step}; notices {string.Join(" ", notices.Select(n => $"{n.Kind}:{n.Run}"))}");
        Assert.IsTrue(notices.Any(n => n.Kind == AreaAiNoticeKind.Release && n.Run == firstQueued), "the first queued goal released at once");
        Assert.IsFalse(notices.Any(n => n.Kind == AreaAiNoticeKind.Cast && n.Run == secondQueued), "the second queued goal never played");
        Assert.IsTrue(notices.Any(n => n.Kind == AreaAiNoticeKind.CastEnd && n.Run == secondQueued && n.SpellRun is not null), "its run was freed");
        CollectionAssert.DoesNotContain(RunsOf(session, npc), secondQueued, "no slot is left behind");
    }

    // ---- ARC-WORLD-105: the NPC's spell missile (AC8) ----

    // The casting goal up to its missile's launch: the play (the invented eye candy) and the launch notice. Every spell has a missile
    // here: the caster's installed spells have none.
    private static (AreaAiNotice Play, AreaAiNotice Missile, List<AreaAiNotice> Notices) LaunchFirstMissile(Crash crash, AreaEventPump pump, ref int frame)
    {
        var notices = new List<AreaAiNotice>();
        for (; frame < 4000 && !notices.Any(n => n.Kind == AreaAiNoticeKind.Missile); frame++)
        {
            pump.Advance(8 * 50);
            crash.Host.Pump(50L * frame);
            notices.AddRange(crash.Host.DrainNotices().Where(n => n.Npc == crash.Npc));
        }
        var play = notices.FirstOrDefault(n => n.Kind == AreaAiNoticeKind.Cast) ?? throw new AssertFailedException("no casting goal played");
        var missile = notices.FirstOrDefault(n => n.Kind == AreaAiNoticeKind.Missile) ?? throw new AssertFailedException("no missile launched");
        Trace105($"spell {play.Spell} run {play.Run} swing {play.CastSwing}; missile at {missile.Target}; cast {crash.Host.TraceOf(crash.Npc)?.Cast}");
        return (play, missile, notices);
    }

    [TestMethod, TestCategory("RealData")]
    public void A_missile_spell_launches_at_the_action_frame_and_its_arrival_after_the_goals_end_begins_the_run()
    {
        using var crash = Open((_, _) => EightFrames(), _ => true);
        var (session, host, caster, npc, _, _, events) = crash;
        var pump = new AreaEventPump(session, events, spells: () => caster);
        var frame = 0;
        var (play, missile, notices) = LaunchFirstMissile(crash, pump, ref frame);
        // The release frame at 300 ms; the launch restarts the goal one pace (100 ms) later (SM1), so the rest is 500 ms, not 400.
        Assert.AreEqual(new AiCastSwing(true, 300, 500), play.CastSwing);
        Assert.AreEqual(play.Run, missile.Run);
        Assert.AreEqual(play.Spell, missile.Spell);
        Assert.AreSame(caster, missile.Caster, "the map's cast host launched it");
        Assert.AreEqual(play.Target, missile.Target, "the missile flies at the run's target");
        Assert.IsFalse(notices.Any(n => n.Kind == AreaAiNoticeKind.Release), "the launch is no release: no re-check, no BEGIN");
        Assert.IsTrue(caster.MissileFlies(play.Run));
        Assert.AreEqual(SpellRunAction.Begin, session.QuerySpellRuns()[play.Run]?.Action, "the run waits for the missile");

        for (; frame < 8000 && host.RunOf(npc) is not null; frame++)
        {
            pump.Advance(8 * 50);
            host.Pump(50L * frame);
            notices.AddRange(host.DrainNotices().Where(n => n.Npc == npc));
        }
        Assert.IsTrue(notices.Any(n => n.Kind == AreaAiNoticeKind.CastEnd && n.Run == play.Run && n.SpellRun is null),
            "the goal's end after the launch frees nothing (0x429CD0: the frame was handled)");
        Assert.IsTrue(caster.MissileFlies(play.Run), "the missile still flies after the caster's goal ended");

        // The arrival (goal 28's release, 0x429C80): the re-check and the BEGIN.
        var begun = caster.ArriveMissile(play.Run);
        Trace105($"arrived: {begun?.Disposition} {begun?.Failure}; hero hp {session.QueryHeroLife()!.Status.HitPoints}");
        Assert.IsNotNull(begun, "the re-check passed");
        Assert.AreNotEqual(GameSessionDisposition.Rejected, begun.Disposition);
        Assert.AreNotEqual(SpellRunAction.Begin, session.QuerySpellRuns()[play.Run]?.Action ?? SpellRunAction.End, "its BEGIN ran");
        Assert.IsFalse(caster.MissileFlies(play.Run));
    }

    [TestMethod, TestCategory("RealData")]
    public void A_missile_outlives_its_casters_run_dropped_mid_play_and_its_loss_frees_the_run()
    {
        using var crash = Open((_, _) => EightFrames(), _ => true);
        var (session, host, caster, npc, _, _, events) = crash;
        var pump = new AreaEventPump(session, events, spells: () => caster);
        var frame = 0;
        var (play, _, _) = LaunchFirstMissile(crash, pump, ref frame);
        Assert.AreEqual(AiGoalType.AttemptSpell, host.RunOf(npc)?.Top.Goal, "the play's rest holds the attempt goal");
        // The NPC's run is dropped mid-rest (a retired host; an interrupt or a new perform alike): the attempt goal's end node frees
        // nothing after the launch, so the run is the missile's (a goal of its own on its own object), whatever becomes of the caster's.
        host.Retire();
        Assert.IsNull(host.RunOf(npc));
        Assert.IsTrue(caster.MissileFlies(play.Run));
        Assert.AreEqual(SpellRunAction.Begin, session.QuerySpellRuns()[play.Run]?.Action, "the run waits for the missile");
        // A lost missile (a refused line, the map left) frees its run unpaid (MG44, both modes).
        var freed = caster.LoseMissile(play.Run);
        Assert.IsNotNull(freed);
        Assert.IsNull(session.QuerySpellRuns()[play.Run], "no slot is left behind");
        Trace105($"dropped mid-rest, then lost: run {play.Run} freed {freed.Disposition}");
    }

    [TestMethod, TestCategory("RealData")]
    public void A_cast_host_rebuilt_before_the_launched_goals_end_frees_nothing_of_the_missiles_run()
    {
        using var crash = Open((_, _) => EightFrames(), _ => true);
        var (session, host, caster, npc, _, _, events) = crash;
        var pump = new AreaEventPump(session, events, spells: () => caster);
        var frame = 0;
        var (play, _, _) = LaunchFirstMissile(crash, pump, ref frame);
        // The viewer rebuilds its cast host mid-rest (a map change), then drops the AI host: the goal's end goes to the host that
        // launched the run, which knows the handled frame, so the run is neither freed nor charged (the new host would free it charged).
        crash.Spells.Value = crash.Rebuild!();
        var fatigue = session.QueryLife(host.AiLifeTarget(npc)!).View?.Record.State.FatigueDamage;
        host.Retire();
        Assert.IsTrue(caster.MissileFlies(play.Run));
        Assert.AreEqual(SpellRunAction.Begin, session.QuerySpellRuns()[play.Run]?.Action, "the run waits for its missile");
        Assert.AreEqual(fatigue, session.QueryLife(host.AiLifeTarget(npc)!).View?.Record.State.FatigueDamage, "nothing was charged");
        Assert.IsNotNull(caster.LoseMissile(play.Run), "the launching host loses it, freeing the run unpaid");
        Assert.IsNull(session.QuerySpellRuns()[play.Run]);
    }

    private static void Trace(string line) => Console.WriteLine("ARC-WORLD-102 " + line);
    private static void Trace105(string line) => Console.WriteLine("ARC-WORLD-105 " + line);
    private static void Trace104(string line) => Console.WriteLine("ARC-WORLD-104 " + line);
}
