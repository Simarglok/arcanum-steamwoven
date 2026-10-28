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
    }

    // The exterior's highest-level NPC whose fields store a college level (the monster; Virgil is the other), the hero a few tiles away.
    private static Crash Open()
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
        var scene = new AreaAiScene
        {
            RuntimeMap = map, Chart = AreaChart.For(snapshot), Steps = steps, Npcs = npcs, Combat = combat, HeroCell = () => hero, Parameters = table,
            LineOfFire = lineOfFire, Spells = () => caster,
        };
        host = new AreaAiHost(session, scene, events, round);
        Assert.AreEqual(0, host.ScheduleTicks());
        // The hero's attack event: the caster reacts and fights the hero.
        host.HeroAttacked(npc);
        Trace($"reaction {host.TraceOf(npc)?.Reaction} setter {host.TraceOf(npc)?.Setter}");
        Assert.AreEqual(DangerState.CombatFocus, session.QueryDanger(npc).State, "the caster fights the hero");
        return new(session, host, caster, npc, known, round, events);
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

    private static void Trace(string line) => Console.WriteLine("ARC-WORLD-102 " + line);
}
