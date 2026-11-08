using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-308 (B07, smoke plan SM07) native observation (stage "placement-census", only when named, map mode with --hero, AI on, the start map; read-only): the map's NPC rows around the hero (the session's residency
    // holds every mobile row of the open map, as the original's map load does) against the AI host's NPCs, before any tick has moved one: an NPC the host holds stands on its row's cell, is held once, and has a
    // row; every row in the hero's sector and the eight around it (the loaded core) is held by the host. The table of the difference is printed (counts by distance in sectors, then each difference). F7 then F8 on the same map:
    // the rows (the session's places after the load) and the host's NPCs agree the same way. Nothing is changed in the session.
    private async Task RunAreaPlacementCensusSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-308 placement-census smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_PLACEMENT_CENSUS_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is null)
        { GD.Print($"ARC-DEV-308 native-smoke placement-census SKIP map={_readyMapName} (needs --hero, an area session and its combat host)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-dev-308-").FullName;
        _saveLibraryForSelfTest = new(saves);
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps } ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
            var mapId = new SessionMapId(map.RuntimeMap);
            AreaSmokePlace(map, ((AreaGridSession)_worldActor.Session!).State.CommittedCell);
            var host = AreaAiHostNow();
            if (host is null) { GD.Print($"ARC-DEV-308 native-smoke placement-census NONE map={map.RuntimeMap} (no AI host)"); return; }
            if (session.QueryResidents(mapId).Status != ResidencyStatus.Open) { GD.Print($"ARC-DEV-308 native-smoke placement-census NONE map={map.RuntimeMap} (the map holds no residency)"); return; }

            (int Rows, int InHost, int OnCell, int Moved, int Missing, int HostOnly, int HeldOff, int Twice, int NearMissing, int NearRows, string[] Lines) Census(AreaAiHost ai, GameSessionOwner now, string label)
            {
                var hero = map.Chart.ToWorld(((AreaGridSession)_worldActor.Session!).State.CommittedCell);
                var heroSector = (X: (int)(hero.X >> 6), Y: (int)(hero.Y >> 6));
                int Distance(CellCoord cell) => Math.Max(Math.Abs((cell.X >> 6) - heroSector.X), Math.Abs((cell.Y >> 6) - heroSector.Y));
                var rows = now.QueryResidents(mapId).Residents
                    .Where(r => r.Kind == SessionResidentKind.Npc && r.Origin == SessionResidentOrigin.MapData && !r.Excluded && r.Cell is { } c && Distance(c) <= 10)
                    .ToDictionary(r => r.Entity, r => r.Cell!.Value, StringComparer.Ordinal);
                var held = ai.Scene.Npcs.GroupBy(n => n.Entity, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
                var lines = new List<string>();
                int onCell = 0, moved = 0, missing = 0, hostOnly = 0, heldOff = 0, nearMissing = 0, nearRows = 0;
                var byDistance = new SortedDictionary<int, (int Rows, int Held)>();
                foreach (var (entity, cell) in rows.OrderBy(r => r.Key, StringComparer.Ordinal))
                {
                    var d = Distance(cell);
                    byDistance.TryGetValue(d, out var tally);
                    if (d <= 1) nearRows++;
                    if (!held.ContainsKey(entity))
                    {
                        missing++; byDistance[d] = (tally.Rows + 1, tally.Held);
                        if (d <= 1) { nearMissing++; lines.Add($"{label} MISSING near row {entity[..12]} cell {cell.X},{cell.Y} sectors {d}"); }
                        continue;
                    }
                    byDistance[d] = (tally.Rows + 1, tally.Held + 1);
                    var at = ai.Tile(entity);
                    if (at == cell) onCell++;
                    else { moved++; lines.Add($"{label} MOVED row {entity[..12]} row cell {cell.X},{cell.Y} host {at?.X},{at?.Y} sectors {d}"); }
                }
                foreach (var entity in held.Keys.Where(e => !rows.ContainsKey(e)))
                {
                    var at = ai.Tile(entity);
                    var resident = now.QueryResidents(mapId).Residents.FirstOrDefault(r => r.Entity == entity);
                    // An off or destroyed row is no row of the census (the original's queries leave it out, RL9): the host holding it is told apart, not counted as a missing row.
                    if (resident is { Excluded: true }) { heldOff++; lines.Add($"{label} HELD-OFF {entity[..12]} host {at?.X},{at?.Y} (off={resident.Off} destroyed={resident.Destroyed})"); continue; }
                    hostOnly++; lines.Add($"{label} HOST-ONLY {entity[..12]} host {at?.X},{at?.Y} ({(resident is null ? "no resident" : resident.Kind + "/" + resident.Origin)})");
                }
                var twice = held.Count(h => h.Value > 1);
                GD.Print($"ARC-DEV-308 placement-census {label} rowsWithin10={rows.Count} heldRows={rows.Count - missing} onRowCell={onCell} moved={moved} notHeld={missing} hostOnly={hostOnly} heldOff={heldOff} heldTwice={twice} hostNpcs={held.Count} "
                    + "byDistance=" + string.Join(" ", byDistance.Select(kv => $"{kv.Key}:{kv.Value.Held}/{kv.Value.Rows}")));
                foreach (var line in lines.Take(60)) GD.Print("ARC-DEV-308 placement-census " + line);
                return (rows.Count, rows.Count - missing, onCell, moved, missing, hostOnly, heldOff, twice, nearMissing, nearRows, lines.ToArray());
            }

            // Before any frame ticks: the host as the map opened.
            var opened = Census(host, session, "opened");
            Require(opened.Rows > 0, "the map has NPC rows within ten sectors of the hero");
            Require(opened.Twice == 0, $"no NPC is held twice by the host ({opened.Twice})");
            Require(opened.Moved == 0, $"every NPC the host holds stands on its row's cell before a tick ({opened.Moved} off): " + string.Join(" | ", opened.Lines.Where(l => l.Contains("MOVED")).Take(5)));
            Require(opened.HostOnly == 0, $"every NPC the host holds has a row ({opened.HostOnly} without): " + string.Join(" | ", opened.Lines.Where(l => l.Contains("HOST-ONLY")).Take(5)));
            Require(opened.NearMissing == 0, $"every row in the hero's sector and the eight around it is held by the host ({opened.NearMissing} of {opened.NearRows} not): " + string.Join(" | ", opened.Lines.Where(l => l.Contains("MISSING")).Take(5)));

            // F7 then F8 on the same map: nothing was ticked, so the places the load gives back are the rows' places.
            QuickKey(Key.F7);
            Require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved: " + _quickSaveStatus);
            var running = _areaSession;
            QuickKey(Key.F8);
            Require(_mapHandoff is not null || _quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 loaded: " + _quickSaveStatus);
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            Require(!ReferenceEquals(running, _areaSession) && _areaSession is not null, "the load published a new session: " + _quickSaveStatus);
            for (var i = 0; i < 5; i++) { AreaSmokeFrame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            var reloaded = AreaAiHostNow() ?? throw new InvalidOperationException("ARC-DEV-308 smoke: no host after the load");
            var loadedSession = _areaSession!;
            Require(loadedSession.QueryResidents(mapId).Status == ResidencyStatus.Open, "the map's residency is open after the load");
            var after = Census(reloaded, loadedSession, "loaded");
            Require(after.Twice == 0, $"after the load no NPC is held twice ({after.Twice})");
            Require(after.Moved == 0, $"after the load every NPC the host holds stands on its row's cell ({after.Moved} off): " + string.Join(" | ", after.Lines.Where(l => l.Contains("MOVED")).Take(5)));
            Require(after.HostOnly == 0, $"after the load every NPC the host holds has a row ({after.HostOnly} without): " + string.Join(" | ", after.Lines.Where(l => l.Contains("HOST-ONLY")).Take(5)));
            Require(after.NearMissing == 0, $"after the load every row near the hero is held by the host ({after.NearMissing} of {after.NearRows} not): " + string.Join(" | ", after.Lines.Where(l => l.Contains("MISSING")).Take(5)));
            Require(after.Rows == opened.Rows, $"the rows within ten sectors are the same after the load ({opened.Rows} then {after.Rows})");
            GD.Print("ARC-DEV-308 native-smoke placement-census PASS " + System.Text.Json.JsonSerializer.Serialize(new { map.RuntimeMap, Rows = opened.Rows, Held = opened.InHost, HeldAfterLoad = after.InHost, NearRows = opened.NearRows }));
        }
        finally
        {
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            try { System.IO.Directory.Delete(saves, recursive: true); } catch (System.IO.IOException) { }
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
        }
    }
}
