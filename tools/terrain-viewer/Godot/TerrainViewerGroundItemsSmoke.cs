using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-RENDER-033) with --hero, before the object attack: an NPC's item the session says lies on the ground
    // next to the hero (its drop's map change) is drawn there from its ground ART and picked as a ground item at that cell; a
    // scenery or trap the session busts takes its destroyed ART at its T0 anchor. Every change is returned to what it was, so the
    // stages after see the map as before. NONE for a part the map has no candidate for.
    private Task RunAreaGroundItemsSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || _worldActor.Session is not AreaGridSession grid
            || _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == area.Snapshot.RuntimeMap) is not { } map)
        { GD.Print($"ARC-RENDER-033 native-smoke ground-items NONE map={_readyMapName} (no hero or no inventory map)"); return Task.CompletedTask; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-RENDER-033 ground-items smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_GROUND_ITEMS_SMOKE_FAILED");
        }
        Require(ExtraArt() is not null, "the extra art opens: " + _extraArtFailure);
        var mapId = new SessionMapId(CurrentAreaMap);
        void Write(MapChangeKey key, long? value)
        {
            var written = session.ApplyChanges(session.Version, null, [new MapChangeEdit(mapId, key, value)]);
            Require(written.Disposition != GameSessionDisposition.Rejected, $"{key} written ({written.Failure})");
            SyncAreaInventory(); SyncAreaGroundBodies();
        }
        // An NPC's item with a ground ART that reads, dropped next to the hero.
        string dropped = "none";
        var hero = grid.Chart.ToWorld(grid.State.CommittedCell);
        var cell = new CellCoord(checked((int)hero.X + 1), checked((int)hero.Y));
        var holders = session.QueryMapInventory(mapId)?.Holders ?? [];
        var item = holders.Where(h => h.Kind == SessionHolderKind.Npc).SelectMany(h => h.Items)
            .Select(i => map.TryGetRow(i.Entity, out var row) ? (Item: i, Row: row) : default)
            .FirstOrDefault(c => c.Row is not null && Number(c.Row, CurrentArtSlot) is { } art && art >> 28 == 6 && ExtraArt()!.Load(art).Status == AreaExtraArtStatus.Ready);
        if (item.Row is { } itemRow)
        {
            var key = MapChangeKey.ForEntity(item.Item.Entity, GameSessionOwner.ItemDroppedProperty);
            var prior = session.QueryMapChange(mapId, key);
            Write(key, GameSessionOwner.ItemDroppedValue(cell));
            var drawn = _groundBodies.GetValueOrDefault(itemRow.Key);
            Require(drawn is not null && drawn.MapX == cell.X && drawn.MapY == cell.Y, "the dropped item is drawn at its cell");
            var body = drawn!;
            Require(body.Frame == ExtraArt()!.Load(Number(itemRow, CurrentArtSlot)!.Value).Frame!.Key, "from its ground ART");
            Require(AreaMovingCommands()?.Contains(body) == true, "among the moved bodies the canvas draws");
            Require(AreaInventoryTargetOf(itemRow.Key) is { Kind: AreaInventoryTargetKind.GroundItem } target && target.Row.WorldX == cell.X && target.Row.WorldY == cell.Y,
                "picked as a ground item at its cell");
            Write(key, prior);
            Require(!_groundBodies.ContainsKey(itemRow.Key), "no longer drawn once the NPC holds it again");
            dropped = $"{item.Item.Entity}@{cell} {body.Destination.Width}x{body.Destination.Height} phase {body.Phase}";
        }
        // A scenery or trap with a destroyed ART that reads, busted.
        string busted = "none";
        if (ObjectTargets() is { } targets)
        {
            var candidate = area.Snapshot.Objects.Where(row => row.Type is SceneryType or TrapType && row.Scope == AreaScope.Core)
                .Select(row => (Row: row, Target: targets.Target(session, row.Key)))
                .FirstOrDefault(c => c.Target is { Object: not null } && session.QueryObject(c.Target.Object!).Condition == SessionObjectCondition.Whole
                    && Number(c.Row, DestroyedArtSlot) is { } art && !AreaDataOwner.NamesNoArt(art) && ExtraArt()!.Load(art).Status == AreaExtraArtStatus.Ready);
            if (candidate.Target is { } struck)
            {
                var key = MapChangeKey.ForEntity(struck.Entity, GameSessionOwner.ObjectConditionProperty);
                var prior = session.QueryMapChange(mapId, key);
                Write(key, (long)SessionObjectCondition.Busted);
                var art = Number(candidate.Row, DestroyedArtSlot)!.Value;
                Require(_groundBodies.GetValueOrDefault(candidate.Row.Key)?.Frame == ExtraArt()!.Load(art).Frame!.Key,
                    "the busted object is drawn from its destroyed ART");
                Write(key, prior);
                Require(!_groundBodies.ContainsKey(candidate.Row.Key), "its T0 ART once it is whole again");
                busted = $"{struck.Entity} 0x{art:X8}";
            }
        }
        // The hero's kit items, which a drop places without a map row, draw from their prototypes' ground ART.
        var kit = (_areaKitArt ?? []).Select(art => art is { } id ? ExtraArt()!.Load(id).Status : (AreaExtraArtStatus?)null).ToArray();
        Require(kit.All(status => status is null or AreaExtraArtStatus.Ready), "every kit item's ground ART reads: " + string.Join(",", kit));
        _canvas?.SetAreaMovingBodies(AreaMovingCommands());
        var arts = ExtraArt()!.Loads;
        GD.Print($"ARC-RENDER-033 native-smoke ground-items {(dropped == "none" && busted == "none" ? "NONE" : "PASS")} map={_readyMapName}"
            + $" dropped={dropped} busted={busted} kit={kit.Count(status => status == AreaExtraArtStatus.Ready)}/{kit.Length} arts={arts.Count} ready={arts.Values.Count(l => l.Status == AreaExtraArtStatus.Ready)}");
        return Task.CompletedTask;
    }
}
