using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-COMBAT-012) with --hero, in map mode after the critical stage: the line of fire over the shown map. It
    // traces the line from the cells around each living NPC with combat values to it, and looks for a cell next to another such NPC,
    // or else next to a door, window, container or scenery the hero can attack, that stops the line; the hero stands there with a
    // lent reach (no kit holds a reach weapon), attacks the farther NPC in combat mode, and the hit goes through the session to the
    // nearer one instead (the original's ranged path, 0x4B3170 with flag 0x200); the farther NPC keeps its hit points. The animation
    // starts without the goal's reach test, which admits only a clear line (ARC-COMBAT-015, ledger RH1). A struck NPC
    // is healed back; a struck object keeps what the hit did (a late stage of map mode). NONE on a map without such a cell.
    private async Task RunAreaLineOfFireSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area
            || _worldActor.Session is not AreaGridSession grid || AreaLineOfFireNow() is not { } lineMap)
        { GD.Print($"ARC-COMBAT-012 native-smoke line-of-fire NONE map={_readyMapName} (no hero or no attack map)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-012 line-of-fire smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_LINE_OF_FIRE_SMOKE_FAILED");
        }
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var runtime = CurrentAreaMap;
        var npcs = area.Snapshot.Objects.Where(row => row.Type == 16 && row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null
                && !IsAreaCompanion(row.Key))
            .Select(row => _combatHost.Target(session, runtime, row.Key))
            .Where(t => t is { Dead: false, Follower: false, Build.Combat: not null } && session.QueryLife(t.Build.Combat!.Target).View?.Status.IsDead != true)
            .Select(t => t!).ToArray();
        var byKey = npcs.ToDictionary(t => t.Row.Key);
        static CellCoord World(AreaObjectRecord row) => new(checked((int)row.WorldX!.Value), checked((int)row.WorldY!.Value));
        var profile = session.Rules.Combat;
        int traces = 0, blocked = 0, failed = 0;
        var objects = ObjectTargets();
        var kinds = new Dictionary<string, int>();
        (AreaCombatTarget Target, AreaObjectRecord Blocker, string Entity, AreaCombatTarget? Npc, CellCoord Hero, LineOfFireTrace<AreaObjectRecord> Trace)? found = null;
        (AreaCombatTarget, AreaObjectRecord, string, AreaCombatTarget?, CellCoord, LineOfFireTrace<AreaObjectRecord>)? objectFound = null;
        foreach (var target in npcs.OrderBy(t => t.Entity, StringComparer.Ordinal))
        {
            if (found is not null) break;
            var at = World(target.Row);
            for (var dy = -6; dy <= 6 && found is null; dy++)
            for (var dx = -6; dx <= 6 && found is null; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < 2) continue;
                var hero = new CellCoord(at.X + dx, at.Y + dy);
                if (!lineMap.Contains(hero)) continue;
                var trace = LineOfFireRules.Trace(hero, 0, 0, at, lineMap, profile);
                traces++;
                if (trace.LineFailed) failed++;
                if (trace.Blocker is null) continue;
                blocked++;
                var row = trace.Blocker;
                var kind = $"type{row.Type}";
                kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                // A blocker the hero's real reach (1) strikes from a cell it can stand on.
                if (row.Key == target.Row.Key || row.WorldX is null || row.WorldY is null) continue;
                var near = World(row);
                if (Math.Max(Math.Abs(near.X - hero.X), Math.Abs(near.Y - hero.Y)) != 1) continue;
                var npc = byKey.GetValueOrDefault(row.Key);
                var thing = npc is null && !IsAreaCompanion(row.Key) ? objects?.Target(session, row.Key, AreaTrapped) : null;
                if (npc is null && thing is not { Refusal: null, Kind: DamageableObjectKind.Portal or DamageableObjectKind.Container or DamageableObjectKind.Scenery })
                    continue;
                if (npc is null && objectFound is not null) continue;
                var chart = map.Chart.ToChart(new(hero.X, hero.Y));
                if (!map.Chart.Contains(chart) || ApproachCell(grid, at.X, at.Y, 6, cell => cell == chart) != chart) continue;
                if (npc is not null) found = (target, row, npc.Entity, npc, hero, trace);
                else objectFound = (target, row, thing!.Entity, null, hero, trace);
            }
        }
        found ??= objectFound;
        if (found is not { } pick)
        {
            GD.Print($"ARC-COMBAT-012 native-smoke line-of-fire NONE map={_readyMapName} npcs={npcs.Length} traces={traces} blocked={blocked} failed={failed}"
                + $" blockers={System.Text.Json.JsonSerializer.Serialize(kinds)} (no NPC or attackable object stops the line to an NPC from a cell next to it)");
            return;
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var nearLife = pick.Npc is { } struckNpc ? session.QueryLife(struckNpc.Build.Combat!.Target).View!.Status : null;
        var farLife = session.QueryLife(pick.Target.Build.Combat!.Target).View!.Status;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            AreaSmokePlace(map, map.Chart.ToChart(new(pick.Hero.X, pick.Hero.Y)));
            if (!HeroInCombatMode) ToggleCombatMode();
            Require(HeroInCombatMode, "the hero is in combat mode");
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var reach = Math.Max(Math.Abs(World(pick.Target.Row).X - pick.Hero.X), Math.Abs(World(pick.Target.Row).Y - pick.Hero.Y));
            _attackReachForSelfTest = reach;
            _lineOfFireStruck = null;
            var draws = session.QueryRandom().Draws;
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            CancelAreaAttack(null);
            _attack = new(pick.Target, pick.Target.Build.Combat!, null);
            // ARC-COMBAT-015: the goal's reach test admits only a clear line (ledger RH1), so the stage starts the animation itself,
            // as when the line changed after that test, and the hit meets the blocker.
            BeginAttackAnimation(_attack, session.QueryHeroAttack()!);
            Require(_attack is { Approaching: false, Player: not null }, "the attack starts without a walk: " + _attackNotice);
            var frames = 0;
            for (; frames < 2000 && _attack is not null && _attack.Attacks == 0; frames++)
            {
                _areaSmokeTime++;
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            var attacks = _attack?.Attacks ?? 0;
            CancelAreaAttack(null);
            Require(attacks == 1, $"one hit ran (attacks {attacks}, notice {_attackNotice})");
            Require(_lineOfFireStruck == pick.Entity, $"the hit struck the nearer one ({_lineOfFireStruck} for {pick.Entity})");
            Require(session.QueryRandom().Draws > draws, "the hit rolled");
            var farAfter = session.QueryLife(pick.Target.Build.Combat!.Target).View!.Status;
            Require(farAfter.HitPoints == farLife.HitPoints, "the farther NPC keeps its hit points");
            var nearAfter = pick.Npc is { } hitNpc ? session.QueryLife(hitNpc.Build.Combat!.Target).View!.Status : null;
            ToggleCombatMode();
            GD.Print("ARC-COMBAT-012 native-smoke line-of-fire PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, Target = pick.Target.Entity, Struck = pick.Entity, StruckType = pick.Blocker.Type, Hero = new[] { pick.Hero.X, pick.Hero.Y }, Reach = reach,
                pick.Trace.Cost, Tiles = pick.Trace.Tiles.Count, Npcs = npcs.Length, Traces = traces, Blocked = blocked, Failed = failed, Frames = frames,
                NearBefore = nearLife?.HitPoints, NearAfter = nearAfter?.HitPoints, Blockers = kinds, Notice = _attackNotice,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            CancelAreaAttack(null); _attackReachForSelfTest = null;
            // The later stages see the map as it was: the nearer NPC is healed (and raised) back.
            if (pick.Npc is { } healNpc)
            {
                var healed = session.Heal(session.Version, healNpc.Build.Combat!.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                if (healed.Disposition == GameSessionDisposition.Rejected) GD.Print($"ARC-COMBAT-012 line-of-fire smoke heal refused failure={healed.Failure}");
            }
            SyncAreaCombatBodies();
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }
}
