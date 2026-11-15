using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>
    /// ARC-SCRIPT-1735 (E5-a tail, ledger NS5, NS9) native observation (stage "object-art", only when named, with --hero, on a map window with the inventory active): a scenery of the shown map takes another art id the way a script's
    /// change art number writes it (the number of another scenery's art in its own id, frame 0, only when an ART file names it): the session keeps it as the object's art field, the view draws the new art where the T0 body stood, the hover
    /// index still holds the object, the save holds the field, and the id of the map data returns the object to its T0 body. The map's own state is left as it was (the field is cleared at the end). The script path itself (use, placed, heartbeat
    /// and dying runs, the plan and its refusals) is the host's unit tests'.
    /// </summary>
    private async Task RunAreaObjectArtStageAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || !InventoryActive || ExtraArt() is not { } art || ObjectTargets() is not { } targets)
        { GD.Print("ARC-SCRIPT-1735 native-smoke object-art SKIP (needs --hero, an area session, the inventory and the module's content)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-SCRIPT-1735 object-art smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_OBJECT_ART_SMOKE_FAILED");
        }
        async Task Settle()
        {
            // ARC-DEV-2259: in a native smoke ProcessWorld returns before the scenery step while a test clock is set, and an earlier stage leaves it set (the
            // chain: life ... movement), so the loaded session's host and the art fields were never read and the stage failed only after others. The stage
            // drives that per-frame step itself, as the scenery smokes and barter do (ARC-DEV-2042).
            for (var i = 0; i < 3; i++) { ScanSceneryArtNow(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); ProcessAreaSceneryAnimation(); SyncAreaInventory(); }
        }
        await Settle();
        Require(ShownSceneryHost is not null, "the scenery host belongs to the shown session (the stage drives the scenery step itself)");
        var runtimeMap = area.Snapshot.RuntimeMap;
        var map = new SessionMapId(runtimeMap);
        var overlay = (_areaOverlay ?? throw new InvalidOperationException("AREA_OVERLAY_REQUIRED"));
        // The scenery rows of the map that are drawn (class 4 art, a T0 body) and the art numbers they carry.
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_OBJECT_ART_CAPTURE");
        var capturing = !string.IsNullOrWhiteSpace(capture);
        var hero = HeroWorldCell();
        var sceneries = area.Snapshot.Objects.Where(row => row.Type == SceneryType && Number(row, CurrentArtSlot) is { } aid && aid >> 28 == 4 && overlay.Bodies.ContainsKey(row.Key)
            && targets.EntityOfKey(row.Key) is not null).ToArray();
        var numbers = sceneries.Select(row => (Number(row, CurrentArtSlot)!.Value >> 19) & 511).Distinct().ToArray();
        // A graphics report (ARC_AREA_OBJECT_ART_CAPTURE=PREFIX: PREFIX-before.png and PREFIX-after.png save the window; never committed) takes the sceneries within 9 tiles of the hero, so the camera, which follows him, shows the change.
        if (capturing && hero is { } at)
            sceneries = [.. sceneries.Where(row => row.WorldX is { } x && row.WorldY is { } y && Math.Abs(x - at.X) <= 9 && Math.Abs(y - at.Y) <= 9)];
        AreaObjectRecord? chosen = null; uint next = 0; AreaExtraFrame? frame = null;
        // The pair a graphics report shows best: the largest bodies (old drawn area plus new frame area), a bounded scan (the first 400 sceneries against the first 40 numbers) that stops at two bodies of 40 x 40 or more.
        long best = -1;
        foreach (var row in sceneries.Take(400))
        {
            var current = Number(row, CurrentArtSlot)!.Value;
            var oldArea = (long)overlay.Bodies[row.Key].Destination.Width * overlay.Bodies[row.Key].Destination.Height;
            foreach (var number in numbers.Take(40))
            {
                var candidate = ObjectArtNumber.Changed(current, checked((int)number), out var outOfRange);
                if (outOfRange || candidate == current || !art.Exists(candidate) || art.Load(candidate).Frame is not { } loaded) continue;
                var score = oldArea + (long)loaded.Frame.Width * loaded.Frame.Height;
                if (score > best) { best = score; chosen = row; next = candidate; frame = loaded; }
            }
            if (best >= 3200 && oldArea >= 1600 && frame is { } found && (long)found.Frame.Width * found.Frame.Height >= 1600) break;
        }
        if (chosen is null || frame is null)
        { GD.Print($"ARC-SCRIPT-1735 native-smoke object-art NONE (the map shows {sceneries.Length} sceneries with art and none takes the number of another one's art into an ART file)"); return; }
        var newFrame = frame;
        var key = chosen.Key;
        var entity = targets.EntityOfKey(key)!;
        var baseline = Number(chosen, CurrentArtSlot)!.Value;
        var t0 = overlay.Bodies[key];
        // The frames as the view draws them (the T0 body's frame and the new art's), saved as PNG for a graphics report; the window itself is covered by the diagnostic panel on most maps.
        void SaveFrame(string name, DecodedAreaFrame decoded)
        {
            if (!capturing) return;
            using var image = Image.CreateFromData(decoded.Width, decoded.Height, false, Image.Format.Rgba8, decoded.Rgba);
            if (image.SavePng($"{capture}-{name}.png") != Error.Ok) throw new InvalidOperationException("AREA_OBJECT_ART_CAPTURE_FAILED");
        }
        try
        {
            Require(!_sceneryArt.ContainsKey(entity), "the scenery is drawn from its map data before the change");
            Require(session.QueryObjectArt(map, entity) is null, "no art field before the change");
            if (capturing && art.Load(unchecked((uint)baseline)).Frame is { } oldFrame) SaveFrame("before", oldFrame.Frame);
            var set = session.SetObjectArt(session.Version, map, entity, next, unchecked((uint)baseline));
            Require(set.Disposition == GameSessionDisposition.Changed, $"the session keeps the art: {set.Disposition} {set.Failure}");
            Require(session.QueryObjectArt(map, entity) == next && session.QueryObjectArts(map).Any(pair => pair.Entity == entity && pair.Art == next), "the session answers the new art id");
            await Settle();
            // ARC-RENDER-1781: the new art's frame the loop shows now (frame 0 when the art has one frame), among the scenery bodies.
            var found = _sceneryBodies.TryGetValue(key, out var drawn);
            var haveArt = _sceneryArt.TryGetValue(entity, out var shownArt);
            Require(found && haveArt && shownArt!.Frames.Any(f => AreaFrameKey.From(f) == drawn!.Frame) && drawn!.Frame != t0.Frame, "the view draws the new art where the T0 body stood");
            var newFrames = shownArt!.Frames;
            Require(drawn!.MapX == t0.MapX && drawn.MapY == t0.MapY, "at the object's cell");
            Require(HoverIndex() is { } index && index.Contains(key), "the hover index still holds the object");
            SaveFrame("after", newFrame.Frame);
            var saved = GameSessionSnapshotCodec.Encode(session.Capture());
            Require(saved.Contains($"\"{SessionObjectArt.Property}\"", StringComparison.Ordinal) && saved.Contains($"\"{entity}\"", StringComparison.Ordinal), "the save holds the art field");
            GD.Print($"ARC-SCRIPT-1735 object-art entity={entity} art=0x{baseline:X8}->0x{next:X8} cell={t0.MapX},{t0.MapY}");
            // The id the map data gives returns the object to its T0 body.
            Require(session.SetObjectArt(session.Version, map, entity, unchecked((uint)baseline), unchecked((uint)baseline)).Disposition == GameSessionDisposition.Changed, "the data's id clears the field");
            await Settle();
            Require(!_sceneryArt.ContainsKey(entity) && session.QueryObjectArt(map, entity) is null && (!_sceneryBodies.TryGetValue(key, out var back) || newFrames.All(f => AreaFrameKey.From(f) != back.Frame)), "the object has its own art again");
            var loopReport = await ObserveSceneryArtLoopAsync(session, map, art);
            GD.Print("ARC-SCRIPT-1735 native-smoke object-art PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = runtimeMap, Sceneries = sceneries.Length, From = $"0x{baseline:X8}", To = $"0x{next:X8}", Loop = loopReport }));
        }
        finally
        {
            if (session.QueryObjectArt(map, entity) is not null) session.ClearMapChange(session.Version, map, SessionObjectArt.Key(entity));
        }
    }
}
