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
    /// ARC-SCRIPT-1779 (ledger AN10) native observation (part of the stage "object-art", only when named, with --hero, on a map window with the inventory active): an item takes another ground art id the way a script's change art number
    /// writes it (the number in the item's own range, frame 0, only when an ART file names it): the session keeps it by the item's identity, the view draws the ground item from the new art (an NPC's item dropped next to the hero, and an item
    /// a T0 body of the map draws where it lies), the save holds the field, and the data's id returns the item to its own art. The inventory art is left as it was (it is another object field, 0x5D). The map's own state is left as it
    /// was (every write is returned). NONE for a part the map has no candidate for.
    /// </summary>
    private async Task RunAreaObjectArtItemsStageAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || !InventoryActive || ExtraArt() is not { } art || _worldActor.Session is not AreaGridSession grid
            || _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == area.Snapshot.RuntimeMap) is not { } inventoryMap || _areaOverlay is not { } overlay)
        { GD.Print("ARC-SCRIPT-1779 native-smoke object-art-items SKIP (needs --hero, an area session, the inventory and the module's content)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-SCRIPT-1779 object-art-items smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_OBJECT_ART_ITEMS_SMOKE_FAILED");
        }
        async Task Settle()
        {
            for (var i = 0; i < 3; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); SyncAreaInventory(); }
        }
        await Settle();
        var map = new SessionMapId(area.Snapshot.RuntimeMap);
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_OBJECT_ART_CAPTURE");
        // The frames as the view draws them (the old art and the new), saved as PNG for a graphics report (never committed).
        void SaveFrame(string name, DecodedAreaFrame decoded)
        {
            if (string.IsNullOrWhiteSpace(capture)) return;
            using var image = Image.CreateFromData(decoded.Width, decoded.Height, false, Image.Format.Rgba8, decoded.Rgba);
            if (image.SavePng($"{capture}-{name}.png") != Error.Ok) throw new InvalidOperationException("AREA_OBJECT_ART_CAPTURE_FAILED");
        }
        // The art id an item takes: its own number replaced by another one whose ground ART reads and differs from the old one (the pair with the largest frames, so a report shows the change).
        (uint Next, AreaExtraFrame Frame)? Pick(uint current)
        {
            (uint, AreaExtraFrame)? best = null; long bestArea = -1;
            var range = Math.Min(ObjectArtNumber.ItemRange(current), 60u);
            var currentKey = art.Load(current).Frame?.Key;
            for (var number = 0; number < range; number++)
            {
                var candidate = ObjectArtNumber.Changed(current, number, out var outOfRange);
                if (outOfRange || candidate == current || !art.Exists(candidate) || art.Load(candidate).Frame is not { } loaded || loaded.Key == currentKey) continue; // another number may name the same ART file
                var size = (long)loaded.Frame.Width * loaded.Frame.Height;
                if (size > bestArea) { bestArea = size; best = (candidate, loaded); }
            }
            return best;
        }
        var parts = new List<string>();
        // Part 1: an NPC's item with a ground ART that reads, dropped next to the hero and drawn from the new art.
        var hero = grid.Chart.ToWorld(grid.State.CommittedCell);
        var cell = new CellCoord(checked((int)hero.X + 1), checked((int)hero.Y));
        var holders = session.QueryMapInventory(map)?.Holders ?? [];
        var carried = holders.Where(h => h.Kind == SessionHolderKind.Npc).SelectMany(h => h.Items)
            .Select(i => inventoryMap.TryGetRow(i.Entity, out var row) ? (Item: i, Row: row) : default)
            .Select(c => (c.Item, c.Row, Art: c.Row is not null && Number(c.Row, CurrentArtSlot) is { } own && own >> 28 == 6 && art.Load(own).Status == AreaExtraArtStatus.Ready ? own : (uint?)null))
            .Where(c => c.Art is not null && !session.IsItemDestroyed(c.Item.Entity))
            .Select(c => (c.Item, c.Row, Art: c.Art!.Value, Pick: Pick(c.Art!.Value)))
            .FirstOrDefault(c => c.Pick is not null);
        if (carried.Row is { } itemRow && carried.Pick is { } picked)
        {
            var entity = carried.Item.Entity;
            var dropKey = MapChangeKey.ForEntity(entity, GameSessionOwner.ItemDroppedProperty);
            var prior = session.QueryMapChange(map, dropKey);
            try
            {
                Require(session.QueryObjectArt(map, entity) is null, "no art field before the change");
                Require(session.ApplyChanges(session.Version, null, [new MapChangeEdit(map, dropKey, GameSessionOwner.ItemDroppedValue(cell))]).Disposition != GameSessionDisposition.Rejected, "the item is dropped");
                await Settle();
                var before = _groundBodies.GetValueOrDefault(itemRow.Key);
                Require(before is not null && before.MapX == cell.X && before.MapY == cell.Y, "the dropped item is drawn at its cell");
                Require(before!.Frame == art.Load(carried.Art).Frame!.Key, "from its own ground ART before the change");
                if (art.Load(carried.Art).Frame is { } oldFrame) SaveFrame("item-before", oldFrame.Frame);
                var set = session.SetObjectArt(session.Version, map, entity, picked.Next, carried.Art);
                Require(set.Disposition == GameSessionDisposition.Changed, $"the session keeps the art: {set.Disposition} {set.Failure}");
                Require(session.QueryObjectArt(map, entity) == picked.Next, "the session answers the new art id");
                await Settle();
                var after = _groundBodies.GetValueOrDefault(itemRow.Key);
                Require(after is not null && after.Frame == picked.Frame.Key && after.Frame != before.Frame, $"the dropped item is drawn from the new ground ART (after={(after is null ? "none" : "drawn")} field=0x{session.QueryObjectArt(map, entity) ?? 0:X8} map={map.RuntimeMap}/{CurrentAreaMap})");
                Require(AreaInventoryTargetOf(itemRow.Key) is { Kind: AreaInventoryTargetKind.GroundItem }, "still picked as a ground item");
                SaveFrame("item-after", picked.Frame.Frame);
                var saved = GameSessionSnapshotCodec.Encode(session.Capture());
                Require(saved.Contains($"\"{SessionObjectArt.Property}\"", StringComparison.Ordinal) && saved.Contains($"\"{entity}\"", StringComparison.Ordinal), "the save holds the art field");
                GD.Print($"ARC-SCRIPT-1779 object-art-items dropped entity={entity} art=0x{carried.Art:X8}->0x{picked.Next:X8} cell={cell}");
                // The data's id returns the item to its own art.
                Require(session.SetObjectArt(session.Version, map, entity, carried.Art, carried.Art).Disposition == GameSessionDisposition.Changed, "the data's id clears the field");
                await Settle();
                Require(_groundBodies.GetValueOrDefault(itemRow.Key)?.Frame == before.Frame && session.QueryObjectArt(map, entity) is null, "the item is drawn from its own art again");
                parts.Add($"dropped 0x{carried.Art:X8}->0x{picked.Next:X8}");
            }
            finally
            {
                if (session.QueryObjectArt(map, entity) is not null) session.ClearMapChange(session.Version, session.QueryObjectRecordMap(SessionObjectArt.Key(entity)) ?? map, SessionObjectArt.Key(entity));
                session.ApplyChanges(session.Version, null, [new MapChangeEdit(map, dropKey, prior)]);
                await Settle();
            }
        }
        else parts.Add("dropped none");
        // Part 2: an item a T0 body of the map draws where it lies takes the new art in that body's place.
        var lying = area.Snapshot.Objects.Where(row => row.Type is >= 5 and <= 14 && Number(row, CurrentArtSlot) is { } own && own >> 28 == 6 && overlay.Bodies.ContainsKey(row.Key) && !_groundBodies.ContainsKey(row.Key)
                && session.QueryMapInventory(map)?.Ground.Any(g => g.Item.Entity == (ObjectTargets()?.EntityOfKey(row.Key) ?? "") && g.Cell.X == row.WorldX && g.Cell.Y == row.WorldY) == true)
            .Select(row => (Row: row, Entity: ObjectTargets()?.EntityOfKey(row.Key), Art: Number(row, CurrentArtSlot)!.Value))
            .Where(c => c.Entity is not null && !session.IsItemDestroyed(c.Entity))
            .Select(c => (c.Row, Entity: c.Entity!, c.Art, Pick: Pick(c.Art)))
            .FirstOrDefault(c => c.Pick is not null);
        if (lying.Row is { } lyingRow && lying.Pick is { } lyingPick)
        {
            var t0 = overlay.Bodies[lyingRow.Key];
            try
            {
                var set = session.SetObjectArt(session.Version, map, lying.Entity, lyingPick.Next, lying.Art);
                Require(set.Disposition == GameSessionDisposition.Changed, $"the session keeps the lying item's art: {set.Disposition} {set.Failure}");
                await Settle();
                var drawn = _groundBodies.GetValueOrDefault(lyingRow.Key);
                Require(drawn is not null && drawn.Frame == lyingPick.Frame.Key && drawn.Frame != t0.Frame, "the view draws the new art where the item's T0 body stood");
                Require(drawn!.MapX == t0.MapX && drawn.MapY == t0.MapY, "at the item's cell");
                Require(HoverIndex() is { } index && index.Contains(lyingRow.Key), "the hover index still holds the item");
                Require(session.SetObjectArt(session.Version, map, lying.Entity, lying.Art, lying.Art).Disposition == GameSessionDisposition.Changed, "the data's id clears the field");
                await Settle();
                Require(!_groundBodies.ContainsKey(lyingRow.Key), "the T0 body is drawn again");
                parts.Add($"lying 0x{lying.Art:X8}->0x{lyingPick.Next:X8}");
            }
            finally
            {
                if (session.QueryObjectArt(map, lying.Entity) is not null) session.ClearMapChange(session.Version, session.QueryObjectRecordMap(SessionObjectArt.Key(lying.Entity)) ?? map, SessionObjectArt.Key(lying.Entity));
                await Settle();
            }
        }
        else parts.Add("lying none");
        if (parts.All(part => part.EndsWith("none", StringComparison.Ordinal)))
        { GD.Print($"ARC-SCRIPT-1779 native-smoke object-art-items NONE map={_readyMapName} (no item with a ground ART and another number into an ART file)"); return; }
        GD.Print("ARC-SCRIPT-1779 native-smoke object-art-items PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = area.Snapshot.RuntimeMap, Parts = parts }));
    }
}
