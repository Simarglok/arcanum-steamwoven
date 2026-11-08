namespace Arcanum.TerrainViewer.Core;

/// <summary>One waypoint of the town map: the picture pixel of the click (where its icon is drawn and hit) and the tile location it stands for.</summary>
public readonly record struct AreaTownWaypoint((long X, long Y) Pixel, (long X, long Y) Location);

/// <summary>Why a town waypoint was not added (ledger WM87, WM105): none; the shared cap of 30 (silent); the town's cap of 6 (message 611); no path from the
/// start (message 610).</summary>
public enum AreaTownWaypointRefusal { None, SharedCap, TownCap, Blocked }

/// <summary>
/// ARC-UI-092: the town map's waypoint list of the original (ledger WM85–WM88, WM105; list set 1 at 0x64E418): at most <see cref="TownCap"/> waypoints (the shared
/// cap of 30 is never reached first), each added after a path test from the PC's location (the first) or the previous waypoint's location, a selection that
/// a press on a waypoint or an add sets, and a deletion of the selected waypoint, or the last, that keeps the selection in range. There is no drag in the
/// town map (WM85) and no re-plan: the list holds no route, so the deletion has no flaw to switch (WM97 concerns the map view's step buffer). Godot-free.
/// </summary>
public sealed class AreaTownWaypoints
{
    public const int SharedCap = 30, TownCap = 6;

    private readonly List<AreaTownWaypoint> _items = [];

    public IReadOnlyList<AreaTownWaypoint> Items => _items;
    public int Count => _items.Count;
    /// <summary>The selected waypoint (0x5C9AD8), -1 for none.</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>
    /// Adds a waypoint at a clicked pixel whose tile is <paramref name="location"/>: refused silently at 30, with message 611 at 6, and with message 610 when
    /// <paramref name="pathExists"/> finds no path from the start (the PC's location for the first waypoint, else the previous waypoint's) to the target.
    /// The new waypoint is selected.
    /// </summary>
    public AreaTownWaypointRefusal Add((long X, long Y) pixel, (long X, long Y) location, (long X, long Y) pcLocation, Func<(long X, long Y), (long X, long Y), bool> pathExists)
    {
        ArgumentNullException.ThrowIfNull(pathExists);
        if (_items.Count >= SharedCap) return AreaTownWaypointRefusal.SharedCap;
        if (_items.Count >= TownCap) return AreaTownWaypointRefusal.TownCap;
        var start = _items.Count == 0 ? pcLocation : _items[^1].Location;
        if (!pathExists(start, location)) return AreaTownWaypointRefusal.Blocked;
        _items.Add(new(pixel, location));
        Selected = _items.Count - 1;
        return AreaTownWaypointRefusal.None;
    }

    /// <summary>Selects the waypoint under a press (WM85, WM102); false outside the list.</summary>
    public bool Select(int index)
    {
        if (index < 0 || index >= _items.Count) return false;
        Selected = index;
        return true;
    }

    /// <summary>Entering the note quill clears the selection (WM103, ARC-UI-106).</summary>
    public void Deselect() => Selected = -1;

    /// <summary>A right release or <c>Delete</c> (WM88): the selected waypoint goes, or the last when none is selected; the selection is then clamped to the
    /// new last (-1 when none is left). The list keeps its order and re-plans nothing. False for an empty list.</summary>
    public bool Remove()
    {
        if (_items.Count == 0) return false;
        var index = Selected < 0 ? _items.Count - 1 : Math.Min(Selected, _items.Count - 1);
        _items.RemoveAt(index);
        Selected = Math.Min(index, _items.Count - 1);
        return true;
    }

    /// <summary>Empties the list (a map load, WM89).</summary>
    public void Clear() { _items.Clear(); Selected = -1; }
}
