namespace Arcanum.TerrainViewer.Core;

/// <summary>The five light slots of the original's interface (ledger GB13): the order of its two button-art tables at 0x5C6524 (the idle
/// art) and 0x5C6510 (the lit art), the slot number the game's light callback 0x556A90 takes. Slot 2 and slot 3 are the Map button's two
/// faces, the town map's and the world map's; the map slot a redraw draws is the one the current map mode names (0x556B70).</summary>
public enum AreaLightSlot { Character = 0, Logbook = 1, TownMap = 2, WorldMap = 3, Inventory = 4 }

/// <summary>
/// ARC-UI-1364 (ledger GB13, read in the 1.0.7.4 executable): the lit state of the top strip's Character, Logbook, Map and Inventory
/// buttons and the Map button's face, Godot-free. The original keeps five art words (0x64C484, initially the idle table) and a map mode
/// (0x64C534, initially 2). The light callback (0x556A90) with a non-zero flag stores the lit art in a slot unless that slot's window is
/// open (its own test: the character sheet 0x55A220, the logbook 0x53F0D0, the world map 0x560F90 for both map slots, the inventory
/// 0x5735F0); with a zero flag it stores the idle art back, whatever is open. Each store redraws the button, and the map slots draw only
/// while the map mode names them (0x556B90). The map callback (0x556B70) stores the mode and redraws it. All five words and the mode are
/// part of the original's save (0x54A1A0); this build keeps them for the session only.
/// </summary>
public sealed class AreaBarLights
{
    /// <summary>The idle art of each slot (0x5C6524): the Character, Logbook, town map, world map and Inventory buttons.</summary>
    public static readonly IReadOnlyList<int> IdleArt = [169, 187, 193, 194, 186];
    /// <summary>The lit art of each slot (0x5C6510): <c>Char_ON</c> and the lit Logbook, town map, world map and Inventory arts.</summary>
    public static readonly IReadOnlyList<int> LitArt = [561, 560, 558, 195, 559];

    /// <summary>Every interface art the lights can show (both tables), for the bars' art load.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [.. IdleArt.Concat(LitArt).Distinct()];

    private readonly bool[] _lit = new bool[5];

    /// <summary>The Map button's face: <see cref="AreaLightSlot.TownMap"/> (193, the start) or <see cref="AreaLightSlot.WorldMap"/> (194).</summary>
    public AreaLightSlot MapMode { get; private set; } = AreaLightSlot.TownMap;

    /// <summary>Whether the slot holds its lit art.</summary>
    public bool IsLit(AreaLightSlot slot) => _lit[Index(slot)];

    /// <summary>The slot a bar button shows: its own, the Map button's the one the mode names; null for a button the lights do not touch.</summary>
    public static AreaLightSlot? SlotOf(AreaBarButton button, AreaLightSlot mapMode) => button switch
    {
        AreaBarButton.Character => AreaLightSlot.Character,
        AreaBarButton.Logbook => AreaLightSlot.Logbook,
        AreaBarButton.Inventory => AreaLightSlot.Inventory,
        AreaBarButton.Map => mapMode,
        _ => null,
    };

    /// <summary>The art a bar button draws now (its lit art while its slot is lit, otherwise the idle art of its slot; the Map button's the
    /// current mode's); null for a button the lights do not touch (it keeps its layout art).</summary>
    public int? ArtOf(AreaBarButton button) => SlotOf(button, MapMode) is { } slot ? (IsLit(slot) ? LitArt : IdleArt)[(int)slot] : null;

    /// <summary>The light callback 0x556A90: <paramref name="lit"/> stores the slot's lit art unless <paramref name="windowOpen"/> (the slot's
    /// own test, see the class); not lit stores the idle art whatever is open. True when the stored state changed.</summary>
    public bool Light(AreaLightSlot slot, bool lit, bool windowOpen)
    {
        var index = Index(slot);
        if (lit && windowOpen) return false;
        if (_lit[index] == lit) return false;
        _lit[index] = lit;
        return true;
    }

    /// <summary>The map callback 0x556B70: the Map button's face (<see cref="AreaLightSlot.TownMap"/> or <see cref="AreaLightSlot.WorldMap"/>).
    /// True when the mode changed.</summary>
    public bool SetMapMode(AreaLightSlot mode)
    {
        if (mode is not (AreaLightSlot.TownMap or AreaLightSlot.WorldMap)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (MapMode == mode) return false;
        MapMode = mode;
        return true;
    }

    /// <summary>The initial state (0x549B70, 0x549F00): every slot idle, the town map's face. True when anything was lit or the mode differed.</summary>
    public bool Reset()
    {
        var changed = MapMode != AreaLightSlot.TownMap || _lit.Any(lit => lit);
        Array.Clear(_lit);
        MapMode = AreaLightSlot.TownMap;
        return changed;
    }

    private static int Index(AreaLightSlot slot)
    {
        if (!Enum.IsDefined(slot)) throw new ArgumentOutOfRangeException(nameof(slot));
        return (int)slot;
    }
}
