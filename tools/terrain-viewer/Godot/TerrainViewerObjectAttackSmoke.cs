using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-COMBAT-008) with --hero, the last stage of map mode (the map keeps what the hits did): in combat mode the
    // hero Alt-clicks the drawn door, window, container or scenery it can damage that has the fewest hit points (scenery, and portals
    // and containers whose material subtracts nothing, first), walks within reach and attacks until it breaks or a bounded number of
    // attacks ran; each hit runs through the session, and an object the hit destroyed or switched off is no longer drawn. NONE on a
    // map without such an object.
    private async Task RunAreaObjectAttackSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || ObjectTargets() is not { } targets || _readyArea is not { } area
            || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-COMBAT-008 native-smoke object-attack NONE map={_readyMapName} (no hero or no attack map)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-008 object-attack smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_OBJECT_ATTACK_SMOKE_FAILED");
        }
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).GroupBy(c => c.Identity!.Value).ToDictionary(g => g.Key, g => g.First());
        var candidates = area.Snapshot.Objects.Where(row => row.Scope == AreaScope.Core && commands.ContainsKey(row.Key))
            .Select(row => targets.Target(session, row.Key))
            .Where(t => t is { Refusal: null, Object: { } o } && t.Kind is DamageableObjectKind.Portal or DamageableObjectKind.Container or DamageableObjectKind.Scenery
                && !o.Baseline.Invulnerable && !o.Baseline.Busted)
            .Select(t => (Target: t!, State: session.QueryObject(t!.Object!), Reduced: t!.Kind != DamageableObjectKind.Scenery
                && AttackRules.MaterialDamageReduction(t.Object!.Baseline.Material) > 0))
            .Where(c => c.State.HitPoints > 0)
            .OrderBy(c => c.Reduced).ThenBy(c => c.State.HitPoints).ThenBy(c => c.Target.Entity, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) { GD.Print($"ARC-COMBAT-008 native-smoke object-attack NONE map={_readyMapName} objects={targets.Count}"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var (target, before, reduced) = candidates[0];
            var key = target.Row.Key;
            var targetCell = map.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
            CellCoord? start = null;
            foreach (var (dx, dy) in new[] { (3, 0), (-3, 0), (0, 3), (0, -3), (2, 0), (-2, 0), (0, 2), (0, -2), (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var near = new CellCoord(targetCell.X + dx, targetCell.Y + dy);
                if (!map.Chart.Contains(near)) continue;
                try { start = AreaSmokeStandable(map, near, 1); break; } catch (InvalidOperationException) { }
            }
            if (start is null) { GD.Print($"ARC-COMBAT-008 native-smoke object-attack NONE map={_readyMapName} (no standable cell near {target.Entity})"); return; }
            AreaSmokePlace(map, start.Value);
            if (!HeroInCombatMode) ToggleCombatMode();
            Require(HeroInCombatMode, "the hero is in combat mode");
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            var point = await HoverPointAsync(this, commands[key]) ?? throw new InvalidOperationException("ARC-COMBAT-008 smoke: target not visible");
            HoverAtForSelfTest(point);
            var revision = session.Version.Revision;
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point, AltPressed = true });
            Require(_attack is { Object: not null }, "the Alt-click started an object attack: " + _attackNotice);
            var walked = _attack!.Approaching;
            var frames = 0; var attacks = 0;
            for (; frames < 20000 && _attack is not null && attacks < 40; frames++)
            {
                _areaSmokeTime++;
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                attacks = Math.Max(attacks, _attack?.Attacks ?? attacks);
            }
            CancelAreaAttack(null);
            var after = session.QueryObject(target.Object!);
            Require(attacks > 0 && session.Version.Revision > revision, $"the attack hit through the session (attacks {attacks}, notice {_attackNotice})");
            Require(after.Damage > before.Damage || after.Condition != before.Condition, "the hits changed the object's damage or condition");
            SyncAreaInventory();
            var gone = after.Condition is SessionObjectCondition.Destroyed or SessionObjectCondition.Off;
            Require(!gone || _inventoryHidden?.Contains(key) == true, "a destroyed or switched-off object is no longer drawn");
            ToggleCombatMode();
            HoverReleaseForSelfTest();
            GD.Print("ARC-COMBAT-008 native-smoke object-attack PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, target.Entity, Kind = target.Kind.ToString(), Material = target.Object!.Baseline.Material.ToString(), Reduced = reduced,
                Before = before.HitPoints, After = after.HitPoints, Condition = after.Condition.ToString(), Walked = walked, Attacks = attacks, Frames = frames,
                Hidden = gone, Notice = _attackNotice,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
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
