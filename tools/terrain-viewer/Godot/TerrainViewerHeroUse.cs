using Arcanum.Data.Area;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-174 (MIG13 S11b): the hero's use click on a trigger object (an exit object, a scripted object, the cave entrance) is the use goal 38 of the goal table, not a walk-to-use intent. The click is an ObjectClickRequest; the
// goal's nodes (AiExecutorRules.HeroUseNode) test the range, walk the hero within use range through goal 8 and ask for the use in the last node, which the viewer applies at the frame's next safe point.
public partial class TerrainViewerApp
{
    // The target strings the use goals hold, by object (the run holds plain strings; a key resolves while the scene it was made for is shown).
    private readonly Dictionary<string, AreaObjectKey> _useTargets = new(StringComparer.Ordinal);

    // ARC-DEV-319: the string names the object by its map, source index, offset and the head of its object id, so two objects never share one (the placed items and piles all have index 0 and offset 0)
    // and a restored run's string finds its object again.
    private static string ObjectIdHead(AreaObjectKey key) => key.ObjectIdSha256.Length > 16 ? key.ObjectIdSha256[..16] : key.ObjectIdSha256;

    private string UseTargetOf(AreaObjectKey key)
    {
        var id = $"obj:{key.RuntimeMap}:{key.SourceIndex}:{key.Offset}:{ObjectIdHead(key)}";
        _useTargets[id] = key;
        return id;
    }

    /// <summary>ARC-DEV-175: the target string of a thing to pick up or loot (an item on the ground, a body, a container, a junk pile). It is kept apart from a trigger object's, so a trigger that is also an inventory object is never taken for one.</summary>
    private string InventoryTargetOf(AreaObjectKey key)
    {
        var id = $"inv:{key.RuntimeMap}:{key.SourceIndex}:{key.Offset}:{ObjectIdHead(key)}";
        _useTargets[id] = key;
        return id;
    }

    private static bool IsInventoryTarget(string target) => target.StartsWith("inv:", StringComparison.Ordinal);

    private AreaObjectKey? UseKeyOf(string target)
    {
        if (_useTargets.TryGetValue(target, out var key)) return key;
        // ARC-DEV-319 (MIG14 S9d): a restored use or pickup run names its object by the string alone: the one object of the shown scene with that map, source index, offset and object id head (none, or two, is a
        // target that is gone): a trigger object (obj:) among the scene's objects, a thing to pick up or loot (inv:) among them and the items on the ground.
        if (RestoredUseKey(target) is not { } found) return null;
        _useTargets[target] = found;
        return found;
    }

    private AreaObjectKey? RestoredUseKey(string target)
    {
        var parts = target.Split(':');
        if (parts.Length != 5 || parts[4].Length == 0 || parts[0] is not ("obj" or "inv") || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var map)
            || !int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
            || !long.TryParse(parts[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var offset)) return null;
        IEnumerable<AreaObjectKey> keys = [];
        if (parts[0] == "inv")
        {
            // A loaded session's ground items are keyed once the bodies are recomputed for it; as the inventory's own sync does, a recompute is drawn at once.
            if (SyncAreaGroundBodies() && _canvas is not null) _canvas.SetAreaMovingBodies(AreaMovingCommands());
            keys = _groundOnly.Keys;
        }
        if (_readyArea is { } area) keys = keys.Concat(area.Snapshot.Objects.Select(o => o.Key));
        if (_areaTransitions?.Current.Snapshot.Objects is { } scene) keys = keys.Concat(scene.Select(o => o.Key));
        var head = parts[4];
        var found = keys.Where(k => k.RuntimeMap == map && k.SourceIndex == index && k.Offset == offset && k.ObjectIdSha256.StartsWith(head, StringComparison.Ordinal)).Distinct().Take(2).ToArray();
        return found.Length == 1 ? found[0] : null;
    }

    /// <summary>The map row of a trigger object of the shown scene (map mode's, else the cave transitions').</summary>
    private AreaObjectRecord? UseObjectRow(AreaObjectKey key)
    {
        // A key the shown scene no longer holds (a goal step meets it after a rebuild) is a target that is gone, not a fault.
        try { return _mapTransfers is not null ? _mapTransfers.Object(key) : _areaTransitions?.Current.Snapshot.Objects.FirstOrDefault(o => o.Key == key); }
        catch (KeyNotFoundException) { return null; }
    }

    private int UseRange => _mapTransfers is not null ? AreaUseRule.UseRange : AreaTransitionCoordinator.UseRange;

    /// <summary>The cell next to a trigger object the use goal's approach walks to: a cell from which the use rule accepts the use.</summary>
    private CellCoord? UseApproachCell(AreaGridSession session, AreaObjectKey key)
    {
        if (UseObjectRow(key) is not { WorldX: { } x, WorldY: { } y }) return null;
        return _mapTransfers is not null ? ApproachCell(session, x, y, AreaUseRule.UseRange, UsableFrom(key)) : UseApproach(session, key);
    }

    /// <summary>The click on a trigger object that guards and refusals let through: the use goal 38 starts (the hero walks within use range first, as the goal's nodes decide). Returns true: the click was a use.</summary>
    private bool StartUseClick(AreaObjectKey key, bool shift = false)
    {
        _useOpenPending = null; _useOpenInventory = false;
        if (HeroRunsHost() is null)
        {
            // No session, so no table (the synthetic fixtures): nothing runs the goal, so the use is asked for at once (and reports "too far" when it is).
            ApplyUse(key);
            return true;
        }
        var request = HeroRequests.Apply(new ObjectClickRequest(UseTargetOf(key), Shift: shift));
        _areaUseStatus = request.Click != HeroClickGoal.UseObject ? $"use refused: the click is {request.Click}"
            : request.Outcome == HeroRequestOutcome.Done ? "using: goal 38 started" : "the use was refused";
        GD.Print($"ARC-DEV-174 use-click outcome={request.Outcome} goal={request.Click}");
        RefreshAreaStatus();
        return true;
    }

    /// <summary>The use that goal 38's last node asked for: applied under the click guards (focus, modal windows, reset/reload) at the frame's next safe point.</summary>
    private void ProcessUsePending()
    {
        if (_useOpenPending is not { } key) return;
        _useOpenPending = null;
        if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending) _areaUseStatus = "walk to use cancelled: reset/reload";
        else if (!_worldFocused || WorldModal || WorldUiFocus) _areaUseStatus = "walk to use dropped: input quarantined/modal";
        // ARC-DEV-175: the act of a pickup (goal 16) or of a loot (goal 81 on a body, a container, a junk pile) is the inventory's: the action points, the window or the transfer.
        else if (_useOpenInventory) { ApplyInventoryAct(key); return; }
        else { ApplyUse(key); return; }
        RefreshAreaStatus();
    }

    // Whether the act asked for belongs to the inventory (set with _useOpenPending by the use and pickup ports).
    private bool _useOpenInventory;

    private void ApplyInventoryAct(AreaObjectKey key)
    {
        if (InventoryActive && AreaInventoryTargetOf(key) is { } inventory) { ActOnInventoryTarget(inventory); return; }
        _inventoryStatus = "walk to pick up/loot ended: the target is gone";
        RefreshAreaStatus();
    }

    /// <summary>The use of a trigger object: the use rule and the script, in map mode or the cave transitions' diagnostic.</summary>
    private void ApplyUse(AreaObjectKey key)
    {
        // A key the shown scene no longer holds is a target that is gone.
        if (UseObjectRow(key) is null) { _areaUseStatus = "the object is gone"; _inventoryStatus = "walk to pick up/loot ended: the target is gone"; RefreshAreaStatus(); return; }
        if (_worldActor.Session is not AreaGridSession session) return;
        var actor = session.Chart.ToWorld(session.State.CommittedCell);
        // ARC-WORLD-1162 (SWB1): a bed is always used by the rest hook, never by a transition: 0x428A10 sends any scenery of art sub-type 0x16 there, and its script runs inside the hook.
        if (_mapTransfers is { } bedTransfers && bedTransfers.IsBed(key)) { ApplyBedUse(key, actor); return; }
        if (_mapTransfers is not null)
        {
            var result = _mapTransfers.RequestUse(key, actor);
            // An accepted use replaces any click route: the started step ends at its cell centre, then the transfer applies.
            if (result.Outcome == AreaUseOutcome.Accepted) StopWorldRoute("ROUTE_REPLACED_BY_USE");
            _areaUseStatus = DescribeMapUse(result);
            PostScriptedLines();
            GD.Print("ARC-WORLD-031 use " + System.Text.Json.JsonSerializer.Serialize(new { Outcome = result.Outcome.ToString(), result.Distance, result.Reason, Actor = actor,
                To = result.Site?.DestinationCatalogue, Arrival = result.Site?.Arrival }));
        }
        else if (_areaTransitions is not null)
        {
            var result = _areaTransitions.RequestUse(key, actor);
            if (result.Outcome == AreaUseOutcome.Accepted) StopWorldRoute("ROUTE_REPLACED_BY_USE");
            _areaUseStatus = result.Outcome switch
            {
                AreaUseOutcome.Accepted => $"use accepted: to map {result.Endpoint!.RuntimeDestination} at ({result.Endpoint.ArrivalX},{result.Endpoint.ArrivalY})",
                AreaUseOutcome.TooFar => $"too far: {result.Distance} cells (use range {AreaTransitionCoordinator.UseRange})",
                _ => $"use refused: {result.Outcome} {result.Reason}",
            };
            GD.Print("ARC-WORLD-009 use " + System.Text.Json.JsonSerializer.Serialize(new { Outcome = result.Outcome.ToString(), result.Distance, result.Reason, Actor = actor }));
        }
        RefreshAreaStatus();
    }
}
