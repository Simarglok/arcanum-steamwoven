using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-COMBAT-008) with --hero, the last stage of map mode (the map keeps what the hits did): in combat mode the
    // hero Alt-clicks the drawn door, window, container (not a junk pile, which never breaks) or scenery it can damage that has the
    // fewest hit points (scenery, and portals
    // and containers whose material subtracts nothing, first), walks within reach and attacks until it breaks or a bounded number of
    // attacks ran; each hit runs through the session, and an object the hit destroyed or switched off is no longer drawn. NONE on a
    // map without such an object. ARC-COMBAT-014: only an object the player's click can pick is a candidate (a click-through scenery,
    // OF_CLICK_THROUGH, is never picked, as in the original, and a roof-covered body is not drawn); a candidate with no cell within the
    // hero's reach to stand on, with no start the route planner connects to the cell the attack walks to, or with no point under the
    // pointer is skipped for the next, up to eight; NONE with the reasons when all are skipped.
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
        // ARC-COMBAT-011: the trap flag of every attackable row's use script (OD6); unknown without script content.
        var useHost = UseScriptHost(area.Snapshot);
        var trapFlags = area.Snapshot.Objects.Where(row => targets.Target(session, row.Key) is not null).Select(row => useHost?.IsTrap(row)).ToArray();
        GD.Print($"ARC-COMBAT-011 native-smoke trap-flags {(useHost is null || trapFlags.Any(t => t is null) ? "NONE" : "PASS")} map={_readyMapName}"
            + $" objects={trapFlags.Length} traps={trapFlags.Count(t => t == true)} unknown={trapFlags.Count(t => t is null)}");
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).GroupBy(c => c.Identity!.Value).ToDictionary(g => g.Key, g => g.First());
        var pickable = HoverIndex()?.Bodies.Select(body => body.Identity).ToHashSet() ?? [];
        var candidates = area.Snapshot.Objects.Where(row => row.Scope == AreaScope.Core && commands.ContainsKey(row.Key) && pickable.Contains(row.Key)
                && _canvas?.AreaRoofCovers(row.Key) != true)
            .Select(row => targets.Target(session, row.Key, AreaTrapped))
            .Where(t => t is { Refusal: null, Object: { } o } && t.Kind is DamageableObjectKind.Portal or DamageableObjectKind.Container or DamageableObjectKind.Scenery
                && !o.Baseline.Invulnerable && !o.Baseline.Busted && !o.Baseline.JunkPile)
            .Select(t => (Target: t!, State: session.QueryObject(t!.Object!), Reduced: t!.Kind != DamageableObjectKind.Scenery
                && AttackRules.MaterialDamageReduction(t.Object!.Baseline.Material) > 0))
            .Where(c => c.State.HitPoints > 0)
            .OrderBy(c => c.Reduced).ThenBy(c => c.State.HitPoints).ThenBy(c => c.Target.Entity, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) { GD.Print($"ARC-COMBAT-008 native-smoke object-attack NONE map={_readyMapName} objects={targets.Count}"); return; }
        var reach = session.QueryHeroAttack()?.Range ?? 1;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            // The first candidate the pointer can hover; a skipped one is reported.
            (AreaObjectAttackTarget? Target, SessionObjectState State, bool Reduced) chosen = default;
            CellCoord targetCell = default; Vector2 point = default; var skipped = new List<string>();
            foreach (var candidate in candidates.Take(8))
            {
                var cell = map.Chart.ToChart(new(candidate.Target.Row.WorldX!.Value, candidate.Target.Row.WorldY!.Value));
                // The hero attacks within its weapon's reach from a cell it can step onto, the cell the attack's approach looks for
                // (ApproachCell); an object without one cannot be reached.
                try { AreaSmokeStandable(map, cell, reach); }
                catch (InvalidOperationException) { skipped.Add($"{candidate.Target.Entity}:no standable cell within reach"); continue; }
                // A start a few cells away from which the attack's approach finds a route (ARC-WORLD-073: the original's approach, one
                // step short of the object; a start across a fence or wall from the object would leave the walk without a route).
                CellCoord? start = null;
                foreach (var (dx, dy) in new[] { (3, 0), (-3, 0), (0, 3), (0, -3), (2, 0), (-2, 0), (0, 2), (0, -2), (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var near = new CellCoord(cell.X + dx, cell.Y + dy);
                    if (!map.Chart.Contains(near)) continue;
                    CellCoord standable;
                    try { standable = AreaSmokeStandable(map, near, 1); } catch (InvalidOperationException) { continue; }
                    AreaSmokePlace(map, standable);
                    if (_worldActor.Session is not AreaGridSession live
                        || AttackApproachCell(live, candidate.Target.Row.WorldX!.Value, candidate.Target.Row.WorldY!.Value) is not { } approach) continue;
                    var routes = new AreaRouteSource(live.Snapshot, live.Chart, live.Occupancy, () => live.Portals?.Invoke());
                    if (approach != standable && Arcanum.Simulation.Navigation.RoutePlanner.Plan(routes, standable, approach).Outcome
                        != Arcanum.Simulation.Navigation.RouteOutcome.Found) continue;
                    start = standable; break;
                }
                if (start is null) { skipped.Add($"{candidate.Target.Entity}:no start with a route to it"); continue; }
                if (!HeroInCombatMode) ToggleCombatMode();
                Require(HeroInCombatMode, "the hero is in combat mode");
                _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
                PublishWorldSelection();
                if (await HoverPointAsync(this, commands[candidate.Target.Row.Key]) is not { } found) { skipped.Add($"{candidate.Target.Entity}:not under the pointer"); continue; }
                (chosen, targetCell, point) = (candidate, cell, found);
                break;
            }
            if (chosen.Target is null)
            {
                GD.Print($"ARC-COMBAT-008 native-smoke object-attack NONE map={_readyMapName} (every candidate skipped: {string.Join("; ", skipped)})");
                return;
            }
            var (target, before, reduced) = (chosen.Target!, chosen.State, chosen.Reduced);
            var key = target.Row.Key;
            HoverAtForSelfTest(point);
            var blockedSteps = BlockedSteps(grid, key, targetCell);
            var revision = session.Version.Revision;
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point, AltPressed = true });
            Require(_attack is { Object: not null }, "the Alt-click started an object attack: " + _attackNotice);
            AttackSettleForSelfTest();
            var walked = AttackApproaching;
            var frames = 0; var attacks = 0;
            for (; frames < 20000 && _attack is not null && attacks < 40; frames++)
            {
                _areaSmokeTime++; SmokeTickHeroRuns();
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
            // The PC's overlay moves the removed record; the steps are checked against the removed objects alone (a companion's
            // live cell next to the target would block for its own reason).
            Require(!gone || AreaPcOccupancy()?.Moved.Contains(key) == true, "the PC's overlay moves the removed object's record");
            var overlay = AreaOccupancy.None.WithRemoved(AreaRemovedObjects());
            var freed = blockedSteps.Count(step => AreaOccupancyRules.Apply(step.Raw, step.From, step.Direction, overlay) is
                { Coverage: Arcanum.Data.Traversal.CardinalTraversalCoverage.Supported, Disposition: Arcanum.Data.Traversal.CardinalTraversalDisposition.Open });
            Require(!gone || freed == blockedSteps.Length, $"a removed object blocks no step onto its tile ({freed}/{blockedSteps.Length})");
            Require(gone || freed == 0, $"a standing object still blocks ({freed}/{blockedSteps.Length})");
            ToggleCombatMode();
            HoverReleaseForSelfTest();
            GD.Print("ARC-COMBAT-008 native-smoke object-attack PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, target.Entity, Kind = target.Kind.ToString(), Material = target.Object!.Baseline.Material.ToString(), Reduced = reduced,
                Before = before.HitPoints, After = after.HitPoints, Condition = after.Condition.ToString(), Walked = walked, Attacks = attacks, Frames = frames,
                Skipped = skipped.Count, Hidden = gone, BlockedSteps = blockedSteps.Length, FreedSteps = freed, Notice = _attackNotice, Trapped = target.Object.Baseline.Trapped,
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

    // ARC-WORLD-065: the steps onto an object's tile that its T0 record alone blocks (no terrain, wall, other object or gap).
    private static (NeighborDirection Direction, CellCoord From, Arcanum.Data.Traversal.AreaStepAssessment Raw)[] BlockedSteps(AreaGridSession grid,
        AreaObjectKey key, CellCoord cell) =>
        Enum.GetValues<NeighborDirection>()
            .Select(d => (Direction: d, From: Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(cell, (NeighborDirection)(((int)d + 4) % 8))))
            .Where(step => grid.Chart.Contains(step.From) && grid.Chart.Contains(cell))
            .Select(step => (step.Direction, step.From, Raw: grid.Assess(step.From, cell, step.Direction)))
            .Where(step => step.Raw.ReasonCode is "DESTINATION_OBJECT_BLOCKER" or "SHOULDER_OBJECT_BLOCKER"
                && step.Raw.Objects.Where(o => o.Disposition == Arcanum.Data.Traversal.CardinalTraversalDisposition.Blocked).All(o => o.Key == key)
                && step.Raw.Gaps.Count == 0).ToArray();

    // Native observation (ARC-WORLD-065) with --hero, after the object attack: the container or scenery of the shown map whose T0
    // record alone blocks steps onto its tile is marked destroyed in the session (as a break does), and those steps are open for
    // the PC's live overlay and its click routes; marked whole again, they block again. The object is restored, so the stages after
    // see the map as before. NONE on a map without such an object.
    private Task RunAreaFreedCellSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || ObjectTargets() is not { } targets || _readyArea is not { } area
            || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-WORLD-065 native-smoke freed-cell NONE map={_readyMapName} (no hero or no attack map)"); return Task.CompletedTask; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-065 freed-cell smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_FREED_CELL_SMOKE_FAILED");
        }
        var candidate = area.Snapshot.Objects.Where(row => row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null)
            .Select(row => targets.Target(session, row.Key))
            .Where(t => t is { Object: not null, Kind: DamageableObjectKind.Container or DamageableObjectKind.Scenery }
                && session.QueryObject(t.Object!).Condition == SessionObjectCondition.Whole)
            .Select(t => (Target: t!, Steps: BlockedSteps(grid, t!.Row.Key, grid.Chart.ToChart(new(t.Row.WorldX!.Value, t.Row.WorldY!.Value)))))
            .Where(c => c.Steps.Length > 0).OrderBy(c => c.Target.Entity, StringComparer.Ordinal).FirstOrDefault();
        if (candidate.Target is null) { GD.Print($"ARC-WORLD-065 native-smoke freed-cell NONE map={_readyMapName} (no object blocks a step by itself)"); return Task.CompletedTask; }
        var (target, allSteps) = candidate;
        // Steps a live actor (a companion) holds at their start, end or a shoulder are blocked for their own reason; they are left out.
        var held = grid.Occupancy?.Invoke()?.Occupied ?? new HashSet<CellCoord>();
        var targetCell = grid.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
        var actors = new AreaOccupancy([], held);
        var steps = allSteps.Where(step => AreaOccupancyRules.Apply(step.Raw, step.From, step.Direction, actors).ReasonCode
            is not (AreaOccupancyRules.DestinationActor or AreaOccupancyRules.ShoulderActor) && !held.Contains(step.From)).ToArray();
        if (steps.Length == 0) { GD.Print($"ARC-WORLD-065 native-smoke freed-cell NONE map={_readyMapName} (a companion holds the steps)"); return Task.CompletedTask; }
        var mapId = new SessionMapId(CurrentAreaMap);
        var routes = new AreaRouteSource(grid.Snapshot, grid.Chart, grid.Occupancy, () => grid.Portals?.Invoke());
        int Open() => steps.Count(step => AreaOccupancyRules.Apply(step.Raw, step.From, step.Direction, grid.Occupancy?.Invoke()) is
            { Coverage: Arcanum.Data.Traversal.CardinalTraversalCoverage.Supported, Disposition: Arcanum.Data.Traversal.CardinalTraversalDisposition.Open });
        int RouteOpen() => steps.Count(step => routes.Assess(step.From, step.Direction) == Arcanum.Simulation.Navigation.RouteStepVerdict.Open);
        var conditionKey = MapChangeKey.ForEntity(target.Entity, GameSessionOwner.ObjectConditionProperty);
        var prior = session.QueryMapChange(mapId, conditionKey);
        void Mark(long? condition)
        {
            // Null returns the key to its loaded baseline.
            var written = session.ApplyChanges(session.Version, null, [new MapChangeEdit(mapId, conditionKey, condition)]);
            Require(written.Disposition != GameSessionDisposition.Rejected, $"the condition {condition} was written ({written.Failure})");
            SyncAreaInventory();
        }
        var before = (Open(), RouteOpen());
        Require(before == (0, 0), $"the standing object blocks its steps ({before})");
        Mark((long)SessionObjectCondition.Destroyed);
        var freed = (Open(), RouteOpen());
        var moved = grid.Occupancy?.Invoke()?.Moved.Contains(target.Row.Key) == true;
        Mark(prior);
        var restored = (Open(), RouteOpen());
        Require(moved, "the PC's overlay moves the removed object's record");
        Require(freed == (steps.Length, steps.Length), $"every step onto the removed object's tile is open ({freed} of {steps.Length})");
        Require(restored == (0, 0), $"the object blocks again once it is whole ({restored})");
        Require(session.QueryMapChange(mapId, conditionKey) == prior, "the object's condition is as it was");
        GD.Print("ARC-WORLD-065 native-smoke freed-cell PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = _readyMapName, target.Entity, Kind = target.Kind.ToString(), Steps = steps.Length, Freed = freed.Item1, RouteFreed = freed.Item2,
            Parity = "NOT_VERIFIED (rule read in the original executable, FC1–FC3; no original-runtime run)" }));
        return Task.CompletedTask;
    }
}
