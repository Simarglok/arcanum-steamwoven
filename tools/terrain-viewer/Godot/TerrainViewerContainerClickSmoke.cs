using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-299 (B23, smoke plan SM16) native observation (stage "container-click", only when named, map mode with --hero, AI off or on, best on the town window `--sector 1409,1314` of the main map): every
    // standing container of the shown window is clicked through the production click path, as the player does (the report: "searching the trash cans in the town gives no reaction"). For each: the
    // hero is stood beside it (a cell within reach 1), the camera is framed on it, a pixel where it is the frontmost pick is found (HoverPointAsync; none is a finding: the object picked
    // at its pixels is printed instead), the hover is read (the action and the key the cursor shows), the press is given at the frames' clock (AreaSmokeClick) and the frames run until the loot window
    // opens or 60 of them have passed. A container the session's open attempt admits (AttemptOpen: Opens) must open its loot window when its pixel is the pick; a locked one must refuse it
    // (no window); a container under a drawn roof (RF10), or with no standable cell within reach, is recorded and passes; a container that cannot be picked for another reason, or whose click does something else, fails the stage with the line naming it. Every rubbish bin must be among those that open.
    // The window's rubbish bins and metal cans (prototype 3075), the "trash cans" of the report.
    private const int RubbishPrototype = 3075;
    // ARC-DEV-395: unless ARC_SMOKE_ALL=1 (`./arcanum smoke ... --all`), the stage clicks a sample of at most this many containers, spread over the prototypes and the open/lock outcomes the session admits
    // (rubbish bins and a locked one first), instead of every container of the window (675 s). What the sample no longer proves: the click path of the containers left out (a pick lost behind another object or a roof there).
    private const int ContainerSampleSize = 8;

    private static bool SmokeAll => System.Environment.GetEnvironmentVariable("ARC_SMOKE_ALL") == "1";

    /// <summary>A deterministic sample of at most <paramref name="size"/> containers: the groups of (prototype, admitted outcome): admitted rubbish bins first, then locked ones, then the other opening ones, by prototype; each round takes one container of each group
    /// (a low-discrepancy index inside the group, so the picks lie apart), until the size is reached.</summary>
    private static AreaInventoryTarget[] SampleContainers(IReadOnlyList<AreaInventoryTarget> containers, Func<AreaInventoryTarget, string> admits, int size)
    {
        var groups = containers.GroupBy(c => (Proto: c.Row.PrototypeNumber, Admits: admits(c)))
            .OrderBy(g => g.Key.Proto == RubbishPrototype && g.Key.Admits == nameof(OpenAttempt.Opens) ? 0 : g.Key.Admits == nameof(OpenAttempt.Locked) ? 1 : g.Key.Admits == nameof(OpenAttempt.Opens) ? 2 : 3)
            .ThenBy(g => g.Key.Admits, StringComparer.Ordinal).ThenBy(g => g.Key.Proto)
            .Select(g => g.OrderBy(c => c.Entity, StringComparer.Ordinal).ToArray()).ToArray();
        var picked = new List<AreaInventoryTarget>();
        var used = groups.Select(g => new bool[g.Length]).ToArray();
        for (var round = 0; picked.Count < size && round < containers.Count; round++)
            for (var g = 0; g < groups.Length && picked.Count < size; g++)
            {
                if (round >= groups[g].Length) continue;
                var at = (int)(((round + 1) * 0.6180339887498949 % 1.0) * groups[g].Length);
                while (used[g][at]) at = (at + 1) % groups[g].Length;
                used[g][at] = true; picked.Add(groups[g][at]);
            }
        return picked.ToArray();
    }

    private async Task RunAreaContainerClickSmokeAsync()
    {
        void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-299 container-click smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_CONTAINER_CLICK_SMOKE_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || _worldActor.Session is not AreaGridSession grid || _inventoryHost?.Map(CurrentAreaMap) is not { } inventoryMap)
        { GD.Print($"ARC-DEV-299 native-smoke container-click NONE map={_readyMapName} (needs a hero, an area and the inventory host)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            for (var i = 0; i < 1200 && RestockHostNow() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(RestockHostNow() is not null, "the restock host is ready (barter data loaded)");
            for (var i = 0; i < 600 && _hoverNames is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptHoverNames(); }
            Require(_hoverNames?.Status == AreaNamesStatus.Ready, "names " + _hoverNames?.Status);
            if (InventoryOpen) EndInventory();
            var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
            var mapId = new SessionMapId(CurrentAreaMap);
            var commands = area.Plan.Commands.Where(c => c.Identity is not null).GroupBy(c => c.Identity!.Value).ToDictionary(g => g.Key, g => g.First());
            var containers = area.Snapshot.Objects.Where(row => row.Scope == AreaScope.Core && row.Type == ContainerType && row.WorldX is not null && inventoryMap.TryGetEntity(row.Key, out _))
                .Select(row => _inventoryHost.Target(session, CurrentAreaMap, row.Key)).OfType<AreaInventoryTarget>()
                .Where(target => target.Kind == AreaInventoryTargetKind.Container).OrderBy(target => target.Entity, StringComparer.Ordinal).ToArray();
            if (containers.Length == 0)
            { GD.Print($"ARC-DEV-299 native-smoke container-click NONE map={_readyMapName} (no standing container in the window)"); return; }

            // The report's clicks left `portal use ... Locked` of sector 88181048705 entity 649: where that door stands and which containers are near it.
            foreach (var portal in area.Snapshot.Objects.Where(o => o.Type == 1 && o.Key.SourceIndex == 649 && o.WorldX is not null))
                GD.Print($"ARC-DEV-299 container-click door key={portal.Key} proto={portal.PrototypeNumber} at={portal.WorldX},{portal.WorldY} nearest-containers=" + string.Join(",", containers
                    .Select(c => (c, d: Math.Max(Math.Abs(c.Row.WorldX!.Value - portal.WorldX!.Value), Math.Abs(c.Row.WorldY!.Value - portal.WorldY!.Value)))).OrderBy(x => x.d).Take(4)
                    .Select(x => $"{_inventoryHost.Name(x.c.Entity, _hoverNames?.Catalog)}@{x.c.Row.WorldX},{x.c.Row.WorldY}:d{x.d}")));
            string Admits(AreaInventoryTarget target) => Arcanum.Data.Combat.CombatFieldReader.ReadBreakable(target.Row).Fields is { } f ? session.AttemptOpen(new(mapId, target.Entity), AreaObjectTargets.ContainerBaseline(target.Row, f), false).ToString() : "no-fields";
            var windowContainers = containers.Length;
            var sampled = !SmokeAll && containers.Length > ContainerSampleSize;
            if (sampled)
            {
                containers = SampleContainers(containers, Admits, ContainerSampleSize);
                GD.Print($"ARC-DEV-395 container-click SAMPLE {containers.Length} of {windowContainers} containers (--all clicks every one): " + string.Join(",", containers.Select(c => c.Entity[^6..])));
            }
            var lines = new List<string>();
            var failures = new List<string>();
            int opened = 0, locked = 0, refusedByRules = 0, noStand = 0, notVisible = 0, wrongPick = 0, noWindow = 0, roofCovered = 0, unreachable = 0, trashSeen = 0, trashAdmitted = 0, trashOpened = 0;
            foreach (var target in containers)
            {
                var row = target.Row;
                var label = $"{target.Entity[^6..]}@{row.WorldX},{row.WorldY}:proto={row.PrototypeNumber}";
                SessionOpenableBaseline? baseline = Arcanum.Data.Combat.CombatFieldReader.ReadBreakable(row).Fields is { } fields ? AreaObjectTargets.ContainerBaseline(row, fields) : null;
                var admits = baseline is not { } b ? "no-fields" : session.AttemptOpen(new(mapId, target.Entity), b, false).ToString();
                var name = _inventoryHost.Name(target.Entity, _hoverNames?.Catalog);
                var trash = row.PrototypeNumber == RubbishPrototype;
                if (trash) { trashSeen++; if (admits == nameof(OpenAttempt.Opens)) trashAdmitted++; }
                // A container with no standable cell within reach 1 cannot be walked to (its goal 81 walk never arrives, as in the original): recorded, not a failure.
                try { AreaSmokeStandable(map, SmokeChartCellOf(row), 1); }
                catch (InvalidOperationException) { unreachable++; lines.Add($"{label} {name} UNREACHABLE attempt={admits} (no standable cell within 1)"); if (trash && admits == nameof(OpenAttempt.Opens)) failures.Add($"{label} {name} (a rubbish bin with no standable cell within reach)"); continue; }
                if (!commands.TryGetValue(row.Key, out var command)) { notVisible++; lines.Add($"{label} {name} NOT-DRAWN attempt={admits}"); if (admits == nameof(OpenAttempt.Opens)) failures.Add($"{label} {name} (admitted but not drawn in the window)"); continue; }
                // The hero's cells, nearest to the container first: the nearest standable cell may lie outside the wall of the building the container stands in, where the building's roof is drawn
                // over it (the original picks nothing under a drawn roof pixel and hides the roof only for a hero inside), so the cells within four go in turn until one shows a pick.
                var home = SmokeChartCellOf(row);
                var candidates = new List<CellCoord>();
                for (var dy = -4; dy <= 4; dy += 2)
                for (var dx = -4; dx <= 4; dx += 2)
                {
                    try { var near = AreaSmokeStandable(map, new(home.X + dx, home.Y + dy), 1); if (!candidates.Contains(near)) candidates.Add(near); }
                    catch (InvalidOperationException) { }
                }
                candidates = candidates.OrderBy(c => Math.Max(Math.Abs(c.X - home.X), Math.Abs(c.Y - home.Y))).ThenBy(c => c.Y).ThenBy(c => c.X).Take(16).ToList();
                if (candidates.Count == 0) { noStand++; lines.Add($"{label} {name} NO-STAND attempt={admits}"); continue; }
                Vector2? point = null; CellCoord stand = candidates[0]; var tried = 0;
                foreach (var candidate in candidates)
                {
                    tried++;
                    AreaSmokePlace(map, candidate);
                    if (InventoryOpen) EndInventory();
                    for (var settle = 0; settle < 10; settle++) AreaSmokeFrame(); // the roof fade follows the hero's cell
                    point = await HoverPointAsync(this, command);
                    stand = candidate;
                    if (point is not null) break;
                }
                if (point is not { } at)
                {
                    // What the player meets instead: the frontmost pick over the container's own screen rectangle, and whether a roof is drawn over it (the last cell tried).
                    AreaSmokePlace(map, candidates[0]);
                    for (var settle = 0; settle < 10; settle++) AreaSmokeFrame();
                    _ = await HoverPointAsync(this, command);
                    var picks = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    int bodyUnderRoof = 0, roofPixels = 0, bodyOpaque = 0;
                    var transform = AreaDrawItem.RasterTransform(GetViewport());
                    var left = (int)Math.Floor(transform.Origin.X + transform.X.X * command.Destination.X); var top = (int)Math.Floor(transform.Origin.Y + transform.Y.Y * command.Destination.Y);
                    var right = (int)Math.Ceiling(transform.Origin.X + transform.X.X * (command.Destination.X + command.Destination.Width));
                    var bottom = (int)Math.Ceiling(transform.Origin.Y + transform.Y.Y * (command.Destination.Y + command.Destination.Height));
                    for (var y = Math.Max(0, top); y < bottom; y += 4)
                    for (var x = Math.Max(0, left); x < right; x += 4)
                    {
                        var probe = new Vector2(x + .5f, y + .5f);
                        if (AreaAlphaAt(command, AreaFrames(), transform, x, y) != 0) { bodyOpaque++; if (AreaRoofHit(probe)) roofPixels++; if (AreaObjectAtScreen(probe, roofs: false) == row.Key) bodyUnderRoof++; }
                        if (AreaObjectAtScreen(probe) is { } hit)
                        {
                            var hitRow = area.Snapshot.Objects.FirstOrDefault(o => o.Key == hit);
                            var hitName = $"type{hitRow?.Type}:proto{hitRow?.PrototypeNumber}";
                            picks[hitName] = picks.GetValueOrDefault(hitName) + 1;
                        }
                    }
                    // Every opaque pixel under a drawn roof pixel: the original picks nothing there (RF10, 0x439890); whether the hero beside the container should have filled that roof is open (not read here).
                    var roofed = bodyOpaque > 0 && roofPixels == bodyOpaque;
                    if (roofed) roofCovered++; else notVisible++;
                    lines.Add($"{label} {name} {(roofed ? "ROOF" : "NO-PICK")} attempt={admits} cells-tried={tried} picked-over-its-rectangle=[{string.Join(",", picks.Select(p => $"{p.Key}x{p.Value}"))}] opaque-pixels={bodyOpaque} under-a-roof-pixel={roofPixels} its-own-pick-without-roofs={bodyUnderRoof} roof-art-at-hero={RoofArtText(map, candidates[0])} roof-art-at-container={RoofArtText(map, home)}");
                    if (admits == nameof(OpenAttempt.Opens) && (!roofed || trash)) failures.Add($"{label} {name} (admitted but no pixel of it is the pick from {tried} cells, and no roof covers it)");
                    continue;
                }
                // What else is drawn over the container's pixels (an object in front of it takes the click): the frontmost pick over the container's opaque pixels that is not the container itself.
                var covered = new SortedDictionary<string, int>(StringComparer.Ordinal); var ownPixels = 0;
                {
                    var tf = AreaDrawItem.RasterTransform(GetViewport());
                    var l = (int)Math.Floor(tf.Origin.X + tf.X.X * command.Destination.X); var t = (int)Math.Floor(tf.Origin.Y + tf.Y.Y * command.Destination.Y);
                    var r = (int)Math.Ceiling(tf.Origin.X + tf.X.X * (command.Destination.X + command.Destination.Width)); var bt = (int)Math.Ceiling(tf.Origin.Y + tf.Y.Y * (command.Destination.Y + command.Destination.Height));
                    for (var y = Math.Max(0, t); y < bt; y += 3)
                    for (var x = Math.Max(0, l); x < r; x += 3)
                    {
                        if (AreaAlphaAt(command, AreaFrames(), tf, x, y) == 0) continue;
                        var hit = AreaObjectAtScreen(new(x + .5f, y + .5f));
                        if (hit == row.Key) { ownPixels++; continue; }
                        var other = hit is { } k ? area.Snapshot.Objects.FirstOrDefault(o => o.Key == k) : null;
                        var key = other is null ? "nothing" : $"type{other.Type}:proto{other.PrototypeNumber}@{other.WorldX},{other.WorldY}";
                        covered[key] = covered.GetValueOrDefault(key) + 1;
                    }
                }
                HoverAtForSelfTest(at);
                var hoverAction = HoverActionForSelfTest; var hoverKey = HoverKeyForSelfTest;
                if (hoverKey != row.Key) { wrongPick++; lines.Add($"{label} {name} WRONG-HOVER hover-key={hoverKey?.ToString() ?? "-"} action={hoverAction} attempt={admits}"); failures.Add($"{label} {name} (hover shows another object)"); continue; }
                _lastContainerOpen = null; _inventoryStatus = string.Empty;
                AreaSmokeClick(at);
                var runKind = ShownHeroRunsHost is { } runs ? (runs.UseRun is not null ? "use-run" : runs.PickupRun is not null ? "pickup-run" : "no-run") : "no-host";
                for (var i = 0; i < 120 && !InventoryOpen && _lastContainerOpen is null; i++) AreaSmokeFrame();
                var outcome = _lastContainerOpen?.Outcome.ToString() ?? "no-open-call";
                lines.Add($"{label} {name} from={stand.X},{stand.Y} cells-tried={tried} own-pixels={ownPixels} covered-by=[{string.Join(",", covered.Select(c => $"{c.Key}x{c.Value}"))}] hover={hoverAction} click-run={runKind} attempt={admits} opened={InventoryOpen} outcome={outcome} status=\"{_inventoryStatus}\"");
                if (InventoryOpen) { opened++; if (trash) trashOpened++; EndInventory(); }
                else if (_lastContainerOpen is { Outcome: AreaContainerOpenOutcome.Locked }) locked++;
                else if (_lastContainerOpen is not null) refusedByRules++;
                else noWindow++;
                if (admits == nameof(OpenAttempt.Opens) && _lastContainerOpen is not { Outcome: AreaContainerOpenOutcome.Opened })
                    failures.Add($"{label} {name} (admitted, click gave {outcome}: {_inventoryStatus})");
                if (admits == nameof(OpenAttempt.Locked) && (InventoryOpen || _lastContainerOpen is not { Outcome: AreaContainerOpenOutcome.Locked })) failures.Add($"{label} {name} (locked: the click gave {outcome}, opened={InventoryOpen})");
            }
            foreach (var line in lines) GD.Print("ARC-DEV-299 container-click " + line);
            GD.Print($"ARC-DEV-299 container-click totals containers={containers.Length}/{windowContainers} sampled={sampled} opened={opened} locked={locked} refused={refusedByRules} no-window={noWindow} no-stand={noStand} roof={roofCovered} unreachable={unreachable} no-pick={notVisible} wrong-hover={wrongPick} rubbish={trashOpened}/{trashSeen}");
            Require(failures.Count == 0, $"{failures.Count} of {containers.Length} containers (window has {windowContainers}): {string.Join("; ", failures)}");
            Require(opened > 0, "no container opened by a click");
            Require(trashOpened == trashAdmitted && (trashSeen == 0 || trashOpened > 0), $"{trashOpened} of the {trashAdmitted} admitted rubbish bins and cans ({trashSeen} in the window) opened by a click");
            GD.Print("ARC-DEV-299 native-smoke container-click PASS " + System.Text.Json.JsonSerializer.Serialize(new
            {
                Map = _readyMapName, Containers = containers.Length, WindowContainers = windowContainers, Sampled = sampled, Opened = opened, Locked = locked, Refused = refusedByRules, NoStand = noStand, Roof = roofCovered, Unreachable = unreachable, NoPick = notVisible, Rubbish = $"{trashOpened}/{trashSeen}",
                Parity = "NOT_VERIFIED (click rules read in the original executable; no original-runtime run)",
            }));
        }
        finally
        {
            if (InventoryOpen) EndInventory();
            _lastContainerOpen = null;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    private string RoofArtText(AreaMapRuntime map, CellCoord cell)
    {
        var world = map.Chart.ToWorld(cell);
        return _areaRoofGrid?.Art(world.X, world.Y) is { } art ? "0x" + art.ToString("X") : "none";
    }
}
