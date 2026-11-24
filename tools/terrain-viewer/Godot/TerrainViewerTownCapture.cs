using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-3063: the town-map render's capture (docs/rendering/hd-world-town-map.md). ARC_TOWNCAP=JOB.json, run through the door-close stage of the map smoke, takes
// the viewport at camera centres given as picture offsets from a town map's origin location, with only the world drawn: no hero, no critter (NPCs and monsters), every roof
// closed, no overlay, the pointer's highlight and rings cleared. It dumps every town's .tmi record and the town's tile list (tiles.txt) beside the shots. Job: {"out":DIR,"w":W,"h":H,"zoom":Z,"town":NAME,
// "hour":H (optional, the area clock is moved forward to that hour),"shots":[{"name":S,"dx":PX,"dy":PX}]} (dx, dy: full-scale picture pixels from the picture centre, which
// is the origin location). The output is written beside the job and is never committed.
public partial class TerrainViewerApp
{
    /// <summary>The job file of the town-map render when the run selects the door-close stage and ARC_TOWNCAP names it; otherwise null.</summary>
    private static string? TownCaptureJob(TerrainViewerSmokeStageSet stages) =>
        System.Environment.GetEnvironmentVariable("ARC_TOWNCAP") is { Length: > 0 } job && stages.Stage("door-close") ? job : null;

    // The capture takes the place of the door-close stage's own smoke in the run that selects it (it prints the stage's PASS marker so the smoke runner accepts the run).
    private bool _townCaptured;

    private async Task RunTownCaptureAsync(string jobPath)
    {
        _townCaptured = true;                       // also when an early return follows: the run then has no PASS marker and fails
        using var job = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(jobPath));
        var root = job.RootElement;
        var outDir = root.GetProperty("out").GetString()!;
        System.IO.Directory.CreateDirectory(outDir);
        using var source = AreaTownMapSource.Open(_arguments!.ModulePath!, out var detail);
        GD.Print("TOWNCAP source " + detail);
        if (source is null) return;
        var tsv = new System.Text.StringBuilder("number\tname\twidth\theight\tcolumns\trows\toriginX\toriginY\tscale\n");
        for (var number = 0; number < 400; number++)
            if (source.Table.Find(number) is { } entry && source.Prepare(number) is { } plan)
                tsv.Append($"{number}\t{entry.Name}\t{plan.Info.Width}\t{plan.Info.Height}\t{plan.Info.Columns}\t{plan.Info.Rows}\t{plan.Info.OriginX}\t{plan.Info.OriginY}\t{plan.Info.Scale}\n");
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "tmi-all.tsv"), tsv.ToString());
        if (root.TryGetProperty("scan", out var scanProperty) && scanProperty.GetBoolean())
        {
            // ARC-RENDER-3107: which map each town map belongs to (the sectors whose metadata word names it), for every map of the run's module.
            var scan = AreaTownMapScan.Scan(_arguments!.ModulePath!, out var scanDetail);
            GD.Print("TOWNCAP scan " + scanDetail);
            if (scan is not { } scanned) return;
            var (mapsTsv, sectorsTsv) = AreaTownMapScan.ToTsv(scanned.Maps, scanned.Sectors);
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "maps-scan.tsv"), mapsTsv);
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "townmap-sectors.tsv"), sectorsTsv);
            // The tile list of every town map (the tiles the original has), so the plan of all towns needs no run of its own.
            var listDir = System.IO.Path.Combine(outDir, "tiles-all");
            System.IO.Directory.CreateDirectory(listDir);
            for (var number = 0; number < 400; number++)
                if (source.Table.Find(number) is { } listed && source.Prepare(number) is { } listedPlan)
                {
                    var list = new System.Text.StringBuilder();
                    for (var index = 0; index < listedPlan.Info.Columns * listedPlan.Info.Rows; index++)
                        if (source.TileExists(listed, index)) list.Append(index).Append('\n');
                    System.IO.File.WriteAllText(System.IO.Path.Combine(listDir, number + ".txt"), list.ToString());
                }
            if (!root.TryGetProperty("town", out _)) { GD.Print("ARC-DEV-138 native-smoke door-close PASS town-scan"); return; }
        }
        var townName = root.GetProperty("town").GetString()!;
        var townNumber = Enumerable.Range(0, 400).FirstOrDefault(n => source.Table.Find(n) is { } e && string.Equals(e.Name, townName, StringComparison.OrdinalIgnoreCase), -1);
        var town = townNumber < 0 ? null : source.Prepare(townNumber);
        if (town is null) { GD.Print("TOWNCAP town not found"); return; }
        // The tile list of the original (a tile without a file is drawn with tile 000000 there): the render writes exactly these tiles.
        var tiles = new System.Text.StringBuilder();
        for (var index = 0; index < town.Info.Columns * town.Info.Rows; index++)
            if (source.TileExists(town.Entry, index)) tiles.Append(index).Append('\n');
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "tiles.txt"), tiles.ToString());
        if (_worldActor.Session is not AreaGridSession session || _camera is null || _readyArea is null || _canvas is null) { GD.Print("TOWNCAP no session"); return; }
        var w = root.GetProperty("w").GetInt32(); var h = root.GetProperty("h").GetInt32(); var zoom = root.GetProperty("zoom").GetDouble();
        var hidden = new List<CanvasItem>(); var hiddenLayers = new List<CanvasLayer>();
        try
        {
            // Only the world: every overlay layer of the viewer is hidden, the hero and the critters are not drawn, the roofs stay closed.
            foreach (var child in GetChildren())
            {
                if (child is CanvasLayer layer && layer.Visible && !layer.IsAncestorOf(_canvas)) { layer.Visible = false; hiddenLayers.Add(layer); }
                else if (child is CanvasItem item && item.Visible && item != _canvas && !item.IsAncestorOf(_canvas)) { item.Visible = false; hidden.Add(item); }
            }
            _canvas.SetActorVisibilityForReadback(false); _canvas.HideCritters = true; _canvas.AreaHighlight = null; _canvas.AreaRing = null;
            _areaRoofsClosed = true; ResetAreaRoofs(); _canvas.SetAreaWallFades(null);
            if (root.TryGetProperty("hour", out var hourProperty) && _areaAmbient is { } ambient)
            {
                var advance = (hourProperty.GetInt32() - ambient.Clock.Hour + 24) % 24;
                if (advance > 0) ambient.AdvanceHours(advance, AmbientSector());
                AreaAmbientChanged("town capture hour");
            }
            await SetSmokeWindowSizeAsync(new Vector2I(w, h));
            var heroCell = session.State.CommittedCell; var heroLoc = session.Chart.ToWorld(heroCell);
            var foot = _worldActor.FootAnchor;
            var info = town.Info;
            // World px of the origin location: the hero's foot anchor plus the isometric difference of the two cells.
            double ox = foot.X + 40.0 * ((info.OriginY - heroLoc.Y) - (info.OriginX - heroLoc.X));
            double oy = foot.Y + 20.0 * ((info.OriginX - heroLoc.X) + (info.OriginY - heroLoc.Y));
            GD.Print($"TOWNCAP hero cell={heroCell.X},{heroCell.Y} loc={heroLoc.X},{heroLoc.Y} origin_loc={info.OriginX},{info.OriginY} origin_world={ox:F2},{oy:F2} viewport={WorldViewport.X}x{WorldViewport.Y} hidden={hiddenLayers.Count + hidden.Count} hour={_areaAmbient?.Clock.Hour}");
            var lines = new System.Text.StringBuilder();
            foreach (var shot in root.GetProperty("shots").EnumerateArray())
            {
                var name = shot.GetProperty("name").GetString()!;
                var wx = ox + shot.GetProperty("dx").GetDouble(); var wy = oy + shot.GetProperty("dy").GetDouble();
                _camera.ResetWorld(new Vec2(wx, wy), WorldViewport, zoom);
                _camera.HoldWorld();
                await RoofFrames(); await RoofFrames();
                using var image = GetViewport().GetTexture().GetImage();
                var path = System.IO.Path.Combine(outDir, name + ".png");
                if (image.SavePng(path) != Error.Ok) throw new InvalidOperationException("TOWNCAP save failed " + path);
                var c = _camera.Camera.Position; var z = _camera.Camera.Zoom;
                lines.Append($"{name}\t{wx:F3}\t{wy:F3}\t{c.X:F3}\t{c.Y:F3}\t{z.X:F4}\t{image.GetWidth()}\t{image.GetHeight()}\n");
                GD.Print($"TOWNCAP shot {name} want=({wx:F1},{wy:F1}) camera=({c.X:F1},{c.Y:F1}) zoom={z.X:F3} size={image.GetWidth()}x{image.GetHeight()}");
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "shots.tsv"), "name\twantX\twantY\tcamX\tcamY\tzoom\tw\th\n" + lines);
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "origin.txt"), $"{ox:F4}\t{oy:F4}\t{info.Width}\t{info.Height}\t{info.OriginX}\t{info.OriginY}\n");
            GD.Print("ARC-RENDER-3063 town-capture done " + townName);
            GD.Print("ARC-DEV-138 native-smoke door-close PASS town-capture " + townName);
        }
        finally
        {
            foreach (var layer in hiddenLayers) layer.Visible = true;
            foreach (var item in hidden) item.Visible = true;
            _canvas.SetActorVisibilityForReadback(true); _canvas.HideCritters = false; _areaRoofsClosed = false; ResetAreaRoofs(); _areaWallCell = null;
        }
    }
}
