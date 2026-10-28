using Arcanum.Data.Area;
using Arcanum.Data.Maps;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-WORLD-081: the PC's place the script world reports while the plants smoke runs (null: the walking actor's cell).
    private CellCoord? _plantSmokePcCell;

    // Native observation (ARC_AREA_MAP_SMOKE_ONLY=plants, a main-map window around a generated sector): the core sector scripts of the
    // window's template sectors are the plant generator's; with the PC reported on a core marker (scenery named 1673) the marker's
    // sector script runs through map mode's own runner 40 times, as 40 entries would; its herbs lie next to the marker as created placed
    // items, at most the Steamwoven limit, drawn from their prototype's art and picked as ground items. NONE on a map without one.
    private Task RunAreaTemplatePlantsSmokeAsync()
    {
        const int MarkerName = 1673, NameSlot = 21, Entries = 40;
        if (_areaSession is not { } session || _readyArea is not { } area || _mapTransfers is not { PlacedRunner: { } runner } transfers)
        { GD.Print($"ARC-WORLD-081 native-smoke plants NONE map={_readyMapName} (no session or no transfers)"); return Task.CompletedTask; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-081 plants smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_TEMPLATE_PLANTS_SMOKE_FAILED");
        }
        var marker = area.Snapshot.Objects.Where(row => row.Scope == AreaScope.Core && row.Type == 3 && row.WorldX is not null && row.WorldY is not null
                && row.EffectiveFields.FirstOrDefault(f => f.Slot == NameSlot)?.Number == MarkerName
                && transfers.SectorScript(WorldSectorCoordinate.Containing(new WorldLocation(row.WorldX.Value, row.WorldY.Value))) is not null)
            .OrderBy(row => row.Key.ObjectIdSha256, StringComparer.Ordinal).ThenBy(row => row.Key.SourceIndex).FirstOrDefault();
        if (marker is null) { GD.Print($"ARC-WORLD-081 native-smoke plants NONE map={_readyMapName} (no core marker in a scripted sector)"); return Task.CompletedTask; }
        var at = new CellCoord(checked((int)marker.WorldX!.Value), checked((int)marker.WorldY!.Value));
        var placed = transfers.SectorScript(WorldSectorCoordinate.Containing(new WorldLocation(at.X, at.Y)))!;
        var before = session.QueryPlacedItems().Count;
        _plantSmokePcCell = at;
        try
        {
            for (var entry = 0; entry < Entries; entry++)
            {
                var ran = runner(placed, _ => null);
                Require(ran.Outcome == AreaScriptedUseOutcome.NoTeleport, $"entry {entry} ran ({ran.Outcome} {ran.Detail})");
            }
        }
        finally { _plantSmokePcCell = null; }
        var created = session.QueryPlacedItems().Where(p => p.Source == SessionPlacedSource.Created && CreatedItemRules.Near(at, p.Cell)).ToArray();
        var limit = session.Rules.Scripts.CreatedItemsUnbounded ? int.MaxValue : CreatedItemRules.Limit;
        Require(created.Length > 0 && created.Length <= limit, $"{created.Length} herbs lie next to the marker (limit {limit})");
        Require(session.QueryPlacedItems().Count - before == created.Length, "every created item lies next to the marker");
        var products = new[] { 10020 + (int)(placed.Counters >> 16 & 0xFF), 10020 + (int)(placed.Counters & 0xFF) };
        Require(created.All(p => products.Contains(p.Kind.Prototype) && p.Kind.Type == SessionItemType.Food), "the herbs are the template's products");
        SyncAreaInventory(); SyncAreaGroundBodies();
        var drawn = created.Count(p => _groundBodies.TryGetValue(PlacedKey(area.Snapshot.RuntimeMap, p.Entity), out var body) && body.MapX == p.Cell.X && body.MapY == p.Cell.Y);
        var picked = created.Count(p => AreaInventoryTargetOf(PlacedKey(area.Snapshot.RuntimeMap, p.Entity)) is { Kind: AreaInventoryTargetKind.GroundItem });
        Require(!InventoryActive || drawn == created.Length && picked == created.Length, $"drawn {drawn} and picked {picked} of {created.Length} with the inventory bound");
        GD.Print("ARC-WORLD-081 native-smoke plants PASS " + System.Text.Json.JsonSerializer.Serialize(new
        {
            Map = _readyMapName, Sector = $"{placed.Sector.X},{placed.Sector.Y}", placed.Script, Marker = AreaUseScriptHost.EntityId(marker), Entries,
            Created = created.Length, Products = created.Select(p => p.Kind.Prototype).Distinct().Order().ToArray(), Drawn = drawn, Picked = picked,
            Inventory = InventoryActive, Rules = session.Rules.Name,
            Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)",
        }));
        return Task.CompletedTask;
    }
}
