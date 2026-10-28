using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-WORLD-092, ARC_AREA_MAP_SMOKE_ONLY=ai, map mode with --hero, the Crash Site cave): the AI turns on, the
    // hero stands a few cells from the living monstrous NPC with the fewest hit points, in combat mode, and that NPC is told of the
    // hero's attack (the attack event a hit runs). Its AI tick's pass focuses the hero, the run walks it up tile by tile, it swings
    // and the session resolves its bite on the hero; its body is drawn on its new cell and the status line shows the AI.
    private async Task RunAreaAiSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area) return;
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-092 ai smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_AI_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var candidates = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is { Monstrous: true } && commands.ContainsKey(c.Row.Key) && c.Row.Scope == AreaScope.Core)
            .OrderBy(c => LifeRules.MaxHitPoints(c.Build.Combat!.Facts, 0)).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) { GD.Print($"ARC-WORLD-092 native-smoke ai NO-TARGET map={map.RuntimeMap}"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var target = candidates[0];
            var entity = target.Holder.Entity;
            var targetCell = map.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
            // A standable cell 3..4 cells from the NPC, so it walks up before it bites.
            CellCoord? start = null;
            foreach (var (dx, dy) in new[] { (4, 0), (-4, 0), (0, 4), (0, -4), (3, 0), (-3, 0), (0, 3), (0, -3), (3, 3), (-3, -3) })
            {
                var near = new CellCoord(targetCell.X + dx, targetCell.Y + dy);
                if (!map.Chart.Contains(near)) continue;
                CellCoord cell;
                try { cell = AreaSmokeStandable(map, near, 1); } catch (InvalidOperationException) { continue; }
                if (Math.Max(Math.Abs(cell.X - targetCell.X), Math.Abs(cell.Y - targetCell.Y)) >= 3) { start = cell; break; }
            }
            Require(start is not null, "a standable cell a few cells from the NPC");
            AreaSmokePlace(map, start!.Value);
            if (!HeroInCombatMode) ToggleCombatMode();
            Require(HeroInCombatMode, "the hero is in combat mode");
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            var host = AreaAiHostNow();
            Require(host is not null, "the AI host is built");
            Require(session.QueryAiTick(entity) is not null, "the NPC has its AI tick");
            var heroLife = session.QueryHeroLife()!.Record.State.HitPointDamage;
            host!.HeroAttacked(entity);
            var danger = session.QueryDanger(entity);
            Require(danger.State == DangerState.CombatFocus && danger.Focus == SessionCombatant.Hero,
                $"the NPC fights the hero: {danger.State} {host.TraceOf(entity)?.Reaction} {host.TraceOf(entity)?.Setter}");

            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var frames = 0; var drawnMoved = false; var swing = false;
            // Automated runs drive the clock themselves: a frame is 50 host milliseconds, as the world frame (the game clock runs 8x).
            var hostMs = 30_000_000L;
            TickAreaClock(hostMs);
            for (; frames < 4000 && host.TraceOf(entity)?.Hit is null; frames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                drawnMoved |= host.Movers.Of(entity) is { } mover && AreaMovingCommands()?.Any(c => c.Identity == target.Row.Key && c.MapX == mover.Cell.X && c.MapY == mover.Cell.Y) == true;
                swing |= host.RunOf(entity)?.Top.Goal == AiGoalType.AttemptAttack;
            }
            var trace = host.TraceOf(entity);
            GD.Print($"ARC-WORLD-092 ai smoke trace danger={trace?.Danger} perform={trace?.Perform} step={trace?.Step} hit={trace?.Hit}");
            Require(trace?.Hit is { } hit && !hit.StartsWith(nameof(GameSessionDisposition.Rejected), StringComparison.Ordinal), $"the NPC's bite was resolved after {frames} frames: {trace?.Hit}");
            Require(swing, "the attempt-attack goal ran");
            var at = host.Tile(entity)!.Value;
            var hero = map.Chart.ToWorld(((AreaGridSession)_worldActor.Session!).State.CommittedCell);
            Require(AiExecutorRules.Distance(at, new(checked((int)hero.X), checked((int)hero.Y))) == 1, "it bit from the cell next to the hero");
            Require(drawnMoved, "its body was drawn on the cells it walked");
            Require(session.QueryCombatMode(SessionCombatant.Critter(entity)), "the attempt put it into combat mode (goal 19 node 5)");
            RefreshAreaStatus();
            Require(GetSelfTestSnapshot().Hud.Contains(" | AI: ", StringComparison.Ordinal), "the status line shows the AI");
            GD.Print("ARC-WORLD-092 native-smoke ai PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Target = target.Row.PrototypeNumber, MaxHitPoints = LifeRules.MaxHitPoints(target.Build.Combat!.Facts, 0),
                Frames = frames, Steps = host.Movers.Of(entity) is not null, Hit = trace!.Hit,
                HeroDamage = session.QueryHeroLife()!.Record.State.HitPointDamage - heroLife,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            _aiForSmoke = false; _aiHost = null; _aiHostStamp = null;
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
        await Task.CompletedTask;
    }
}
