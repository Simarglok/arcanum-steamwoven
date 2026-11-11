using Arcanum.Session;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Traps;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-124 (E6-b; ledger IA13, TP2, TP3): the hero's use of a trap device whose slot 1 holds a spell (the timed bombs 15154 and 15193). The cast host places it (the device destroyed, the marker made on a
// tile, the 88 s fuse set) and its event pass runs the spell at the tile when the fuse ends; the alarm of record 176 is the AI host's. A device whose slot 1 is empty (a script trap) stays NOT READY until ARC-WORLD-125.
public partial class TerrainViewerApp
{
    /// <summary>The last placement a trap device use made (the smoke and the self-test read it).</summary>
    internal SessionTrapDeviceUse? LastTrapDeviceUse { get; private set; }

    /// <summary>The line the hero's message window shows when a placed marker is spotted (<c>mes\trap.mes</c> line 0, 0x4BC090).</summary>
    private const string TrapDevicePlacedLine = "mes\\trap.mes 0";

    /// <summary>
    /// A trap device use on the object <paramref name="target"/> (the object form, 0x4BC2E0; a wall target takes the tile form) or on <paramref name="tile"/> (0x4BC480). Returns the message, or null when the
    /// use is not this slice's (a script trap), so the caller keeps its own NOT READY line.
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
                    (used.Marker is not null ? $", marker spotted ({TrapDevicePlacedLine})" : "") + (alerted.Count > 0 ? $", {alerted.Count} critters alerted" : "");
            case SessionTrapDeviceOutcome.Script: return null;
            case SessionTrapDeviceOutcome.Refused: return $"not used: the trap device stays ({used.Failure})";
            default: return $"not used: the trap device's slot 1 does not read ({used.Outcome})";
        }
    }
}
