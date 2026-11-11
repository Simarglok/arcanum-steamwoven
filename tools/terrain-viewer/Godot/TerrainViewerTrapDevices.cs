using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Traps;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-124 (E6-b; ledger IA13, TP2, TP3): the hero's use of a trap device whose slot 1 holds a spell (the timed bombs 15154 and 15193). The cast host places it (the device destroyed, the marker made on a
// tile, the 88 s fuse set) and its event pass runs the spell at the tile when the fuse ends; the alarm of record 176 is the AI host's.
// ARC-WORLD-125 (E6-c): a device whose slot 1 is empty (the script traps, items 15100 and 15120) copies its own use script onto a portal or a container (the host reads the target's slot) or onto a marker made at the
// tile; a target that takes none, or a tile that holds a trap object, refuses it with line 2 or 3 of mes\trap.mes and the device stays.
public partial class TerrainViewerApp
{
    /// <summary>The last placement a trap device use made (the smoke and the self-test read it).</summary>
    internal SessionTrapDeviceUse? LastTrapDeviceUse { get; private set; }

    /// <summary>The last script trap use (ARC-WORLD-125; the smoke and the self-test read it).</summary>
    internal SessionScriptTrapUse? LastScriptTrapUse { get; private set; }

    /// <summary>
    /// A trap device use on the object <paramref name="target"/> (the object form, 0x4BC2E0; a wall target takes the tile form) or on <paramref name="tile"/> (0x4BC480). Returns the message, or null when the
    /// use is not this slice's, so the caller keeps its own NOT READY line.
    /// </summary>
    internal string? PlaceTrapDeviceUse(string item, string? target, SpellLocation? tile)
    {
        if (SpellCasterNow() is not { } caster) return "not used: the trap device needs the map's cast host";
        SpellLocation at;
        bool onTile;
        if (tile is { } clicked) (at, onTile) = (clicked, true);
        else if (target is not null && caster.Facts(target) is { } facts && facts.Location != SpellLocation.None) (at, onTile) = (facts.Location, facts.Type == 0);
        else return "not used: the trap device has no target here";
        var used = caster.UseTrapDevice(item, at, onTile);
        LastTrapDeviceUse = used;
        GD.Print($"ARC-WORLD-124 trap-device item={item} at={at.X},{at.Y} on-tile={onTile} outcome={used.Outcome} spell={used.Plan.Spell} marker={used.Marker?.Entity ?? "-"} failure={used.Failure}");
        switch (used.Outcome)
        {
            case SessionTrapDeviceOutcome.Placed:
                var alerted = used.Plan.Alarm ? _aiHost?.TrapDeviceAlarm() ?? [] : [];
                SyncAreaInventory();
                return $"trap device placed: record {used.Plan.Spell} goes off in {TrapDeviceRules.FuseMs / 1000} s" +
                    (used.Marker is not null ? ", marker placed" : "") + (alerted.Count > 0 ? $", {alerted.Count} critters alerted" : "");
            case SessionTrapDeviceOutcome.Script: return PlaceScriptTrapUse(caster, item, target, at);
            case SessionTrapDeviceOutcome.Refused: return $"not used: the trap device stays ({used.Failure})";
            default: return $"not used: the trap device's slot 1 does not read ({used.Outcome})";
        }
    }

    // The device's own use slot (attachment point 1): the session's kept slot of an item it made, else the slot the host reads from the carried map item's data.
    private ScrSlot? DeviceUseSlot(GameSessionOwner owner, AreaUseScriptHost host, string item)
    {
        if (owner.QueryItemScriptSlot(item, ScrPoint.Use) is { } kept) return new(kept.Script, kept.LocalFlags, kept.Counters);
        return owner.QueryPcInventory().FirstOrDefault(held => held.Entity == item) is { Entity: not null } held ? host.ItemUseSlot(held.Origin.RuntimeMap, item) : null;
    }

    /// <summary>
    /// ARC-WORLD-125 (E6-c; ledger IA13, TP2): the script trap device <paramref name="item"/> used on the object <paramref name="target"/> (the object form, 0x4BC2E0) or on the tile <paramref name="at"/> (0x4BC480).
    /// The host reads what the rules need: the device's script and its trap class, the target's type and the use script now in its slot, and whether the map data holds a trap object on the tile.
    /// </summary>
    private string PlaceScriptTrapUse(SessionSpellCaster caster, string item, string? target, SpellLocation at)
    {
        if (_areaSession is not { } owner || _readyArea is not { } area || UseScriptHost(area.Snapshot) is not { } host || AreaTrapsNow() is not { } traps || _spellCastMap is not { } castMap)
            return "not used: the script trap device needs the map's use-script host";
        if (DeviceUseSlot(owner, host, item) is not { Script: not 0 } slot) return "not used: the script trap device carries no use script";
        SessionScriptTrapTarget? armed = null;
        if (target is not null && caster.Facts(target) is { } facts)
        {
            var row = area.Snapshot.Objects.FirstOrDefault(candidate => castMap.Id(candidate) == target);
            // Only a portal or a container is armed, and both are rows of the map's data; the use script is the row's slot as the trap state reads it.
            if (row is null && facts.Type is TrapDeviceRules.ObjectTypePortal or TrapDeviceRules.ObjectTypeContainer) return "not used: the target is no object of the map's data";
            armed = new(row is null ? target : AreaUseScriptHost.EntityId(row), facts.Type, row is null ? 0 : host.UseScriptOf(row));
        }
        var cell = new CellCoord(checked((int)at.X), checked((int)at.Y));
        var kind = TrapRules.Classify(slot.Script, host.TrapHeaderFlags(slot.Script));
        var used = caster.UseScriptTrapDevice(item, new(slot.Script, slot.LocalFlags, slot.Counters), kind, at, armed, traps.TrapObjectsOn(cell).Count > 0);
        LastScriptTrapUse = used;
        GD.Print($"ARC-WORLD-125 script-trap item={item} script={slot.Script} class={kind} at={at.X},{at.Y} target={armed?.Entity ?? "-"} type={armed?.Type.ToString() ?? "-"} outcome={used.Outcome} " +
            $"marker={used.Marker?.Entity ?? "-"} prototype={used.Marker?.Prototype.ToString() ?? "-"} refusal-line={used.Plan.RefusalLine} failure={used.Failure}");
        switch (used.Outcome)
        {
            case SessionScriptTrapOutcome.Armed:
                SyncAreaInventory();
                return $"script trap armed: script {slot.Script} on " + (used.Target is not null ? $"{used.Target}" : $"a marker (prototype {used.Marker!.Prototype})");
            case SessionScriptTrapOutcome.TargetRefused: return $"not used: the target takes no script trap (mes\\trap.mes {used.Plan.RefusalLine}): the device stays";
            case SessionScriptTrapOutcome.TileRefused: return $"not used: a trap object stands on the tile (mes\\trap.mes {used.Plan.RefusalLine}): the device stays";
            case SessionScriptTrapOutcome.Refused: return $"not used: the trap device stays ({used.Failure})";
            default: return $"not used: the trap device's slot 1 does not read ({used.Outcome})";
        }
    }
}
