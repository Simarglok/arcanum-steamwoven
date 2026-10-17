using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-COMBAT-005) with --hero, after the combat-mode stage, on the exterior or a whole map: in combat mode
    // the hero clicks the living NPC with the fewest hit points from a few cells away, walks within reach, plays its attack
    // ART, and the auto-attack repeats until the NPC dies; the session then holds its death, the experience it paid and makes it
    // lootable, its body is drawn fallen, and a click on the corpse in combat mode without Alt does nothing. A map without a
    // living NPC that has combat values reports that and passes nothing.
    private async Task RunAreaAttackSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area) return;
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-005 attack smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_ATTACK_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var candidates = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is not null && commands.ContainsKey(c.Row.Key) && c.Row.Scope == AreaScope.Core)
            .OrderBy(c => LifeRules.MaxHitPoints(c.Build.Combat!.Facts, 0)).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        var refused = session.QueryMapInventory(new(map.RuntimeMap))!.Holders.Where(h => h.Kind == SessionHolderKind.Npc)
            .Select(h => _combatHost.Build(map.RuntimeMap, h.Entity).Failure).Where(f => f != SessionCritterCombatFailure.None)
            .GroupBy(f => f).ToDictionary(g => g.Key.ToString(), g => g.Count());
        if (candidates.Length == 0)
        {
            GD.Print("ARC-COMBAT-005 native-smoke attack NO-TARGET " + System.Text.Json.JsonSerializer.Serialize(new { map.RuntimeMap, Refused = refused }));
            return;
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var target = candidates[0];
            var key = target.Row.Key;
            var targetCell = map.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
            // A standable cell 2..4 cells from the target, so the click walks within reach first.
            CellCoord? start = null;
            foreach (var range in new[] { 3, 2, 4 })
            {
                var cell = AreaSmokeStandable(map, targetCell, range);
                var d = Math.Max(Math.Abs(cell.X - targetCell.X), Math.Abs(cell.Y - targetCell.Y));
                if (d >= 2) { start = cell; break; }
            }
            AreaSmokePlace(map, start ?? AreaSmokeStandable(map, targetCell, 1));
            if (!HeroInCombatMode) ToggleCombatMode();
            Require(HeroInCombatMode, "the hero is in combat mode");
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            var point = await HoverPointAsync(this, commands[key]) ?? throw new InvalidOperationException("ARC-COMBAT-005 smoke: target not visible");
            HoverAtForSelfTest(point);
            var hover = HoverActionForSelfTest;
            Require(hover is AreaHoverAction.Attack or AreaHoverAction.WalkToAttack, "hover " + hover);
            Require(Godot.Input.GetCurrentCursorShape() == Godot.Input.CursorShape.Drag || _hoverCursor == Godot.Input.CursorShape.Drag, "attack cursor");

            var experience = session.QueryHeroExperience()?.Experience;
            var draws = session.QueryRandom().Draws;
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            AreaSmokeClick(point);
            Require(_attack is not null, "the click started an attack: " + _attackNotice);
            var walked = _attack!.Approaching;
            var attackFrames = 0; var frames = 0; var maxAttacks = 0;
            for (; frames < 20000 && !session.QueryNpcKilled(target.Holder.Entity); frames++)
            {
                _areaSmokeTime++;
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                if (_worldActor.State.Action == ActorDiagnosticAction.Attack) attackFrames++;
                maxAttacks = Math.Max(maxAttacks, _attack?.Attacks ?? 0);
                if (_attack is null && !session.QueryNpcKilled(target.Holder.Entity)) break;
            }
            Require(session.QueryNpcKilled(target.Holder.Entity), $"killed after {frames} frames, attack {_attack is not null}: {_attackNotice}");
            Require(attackFrames > 0, "the attack ART was drawn");
            Require(session.QueryRandom().Draws > draws, "the attacks rolled");
            for (var i = 0; i < 4; i++) { _areaSmokeTime++; WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20)); }
            Require(_attack is null && _worldActor.State.Action != ActorDiagnosticAction.Attack, "the attack stops with the death");
            Require(session.QueryMapInventory(new(map.RuntimeMap))!.Holders.Single(h => h.Entity == target.Holder.Entity).Dead, "the corpse can be looted");
            Require(experience is null || session.QueryHeroExperience()!.Experience > experience, "the kill paid experience");
            RefreshAreaStatus();
            Require(GetSelfTestSnapshot().Hud.Contains("killed", StringComparison.Ordinal), "the status line names the kill");
            Require(_fallenBodies.ContainsKey(key), "the body is drawn fallen");

            // A corpse clicked in combat mode without Alt does nothing.
            HoverAtForSelfTest(point);
            var corpseHover = HoverActionForSelfTest;
            AreaSmokeClick(point);
            Require(_attack is null && !InventoryOpen, "a corpse click without Alt does nothing");
            ToggleCombatMode();
            HoverReleaseForSelfTest();
            GD.Print("ARC-COMBAT-005 native-smoke attack PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Target = target.Row.PrototypeNumber, MaxHitPoints = LifeRules.MaxHitPoints(target.Build.Combat!.Facts, 0),
                Walked = walked, Frames = frames, AttackFrames = attackFrames, Attacks = maxAttacks, Hover = hover.ToString(), CorpseHover = corpseHover.ToString(),
                Experience = session.QueryHeroExperience()?.Experience - experience, Refused = refused,
                Parity = "NOT_VERIFIED (rules from the CE reading and the original executable; no original-runtime run)" }));
        }
        finally
        {
            CancelAreaAttack(null); _attackAimForSelfTest = null;
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }
}
