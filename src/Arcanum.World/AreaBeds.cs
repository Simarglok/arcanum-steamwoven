using Arcanum.Data.Area;
using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-1162 (E3-d, ledger SWB1, SWB5, SWB14–SWB16): what a bed is and who lies in it. The executable has no bed flag, prototype list or type (SWB14): a bed is a scenery object (type 3) whose art id
/// is of art type 4 with scenery sub-type 0x16 (the art id's bits 6 to 10, 0x504300); the use-object act node (0x428A10) sends such a target to the rest hook and anything else to its use script. A bed holds at most
/// one occupant (scenery field 0x46). Godot-free and pure.
/// </summary>
public static class AreaBeds
{
    /// <summary>The art type of scenery (the art id's bits 28 to 31) and the sub-type of a bed (bits 6 to 10).</summary>
    public const uint SceneryArtType = 4;
    public const int BedSubType = 0x16;

    /// <summary>The object type of scenery.</summary>
    private const int SceneryType = 3;

    /// <summary>The field slot of the current art id in an object's fields (the executable's field 1).</summary>
    private const int CurrentArtSlot = 0;

    /// <summary>Whether an art id is a bed's (0x428A10 tests the sub-type through 0x504300 on the target's art id; the occupied frame of a bed is the same sub-type).</summary>
    public static bool IsBedArt(uint art) => art >> 28 == SceneryArtType && ((art >> 6) & 0x1f) == BedSubType;

    /// <summary>Whether a map object is a bed: scenery whose effective art id is a bed art.</summary>
    public static bool IsBed(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Type == SceneryType && row.EffectiveFields.FirstOrDefault(field => field.Slot == CurrentArtSlot)?.Number is { } art && IsBedArt(unchecked((uint)art));
    }

    /// <summary>The beds of a set of map objects, in order.</summary>
    public static IEnumerable<AreaObjectRecord> Beds(IEnumerable<AreaObjectRecord> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        return objects.Where(IsBed);
    }
}

/// <summary>
/// ARC-WORLD-1162 (ledger SWB15, SWB16): who occupies each bed of the shown map. <see cref="Seat"/> is the seat (0x45F2D0: an occupied bed answers 0 and changes nothing, else the sleeper is the occupant) and
/// <see cref="Unseat"/> the unseat (0x45F3A0: a bed whose occupant is not the sleeper only logs an error and changes nothing). Only the occupant field is modelled here (the sleeper's sleeping flag, its drawing and the bed's
/// occupied frame are not, ARC-WORLD-1162's NOT READY list); the hero's rest is the one use, and it is not saved (a load closes the window). Caller-serialized.
/// </summary>
public sealed class AreaBedOccupancy
{
    private readonly Dictionary<AreaObjectKey, string> _occupants = [];

    /// <summary>The beds that hold an occupant now.</summary>
    public int Count => _occupants.Count;

    /// <summary>The occupant of <paramref name="bed"/> (its critter's entity), or null when it is empty.</summary>
    public string? Occupant(AreaObjectKey bed) => _occupants.GetValueOrDefault(bed);

    /// <summary>The seat: false (nothing changed) when the bed is occupied, by anyone, the sleeper included; true once <paramref name="sleeper"/> lies in it.</summary>
    [OriginalFunction(0x45F2D0, OriginalFunctionScope.Partial)]
    public bool Seat(AreaObjectKey bed, string sleeper)
    {
        ArgumentException.ThrowIfNullOrEmpty(sleeper);
        if (_occupants.ContainsKey(bed)) return false;
        _occupants[bed] = sleeper;
        return true;
    }

    /// <summary>The unseat: false (nothing changed) when the bed's occupant is not <paramref name="sleeper"/> (the original logs an error); true once the bed is empty.</summary>
    [OriginalFunction(0x45F3A0, OriginalFunctionScope.Partial)]
    public bool Unseat(AreaObjectKey bed, string sleeper)
    {
        ArgumentException.ThrowIfNullOrEmpty(sleeper);
        if (!_occupants.TryGetValue(bed, out var occupant) || occupant != sleeper) return false;
        _occupants.Remove(bed);
        return true;
    }

    /// <summary>Empties every bed (for a host that replaces its map or session; the viewer empties the hero's bed when his window closes, which every such change does).</summary>
    public void Clear() => _occupants.Clear();
}
