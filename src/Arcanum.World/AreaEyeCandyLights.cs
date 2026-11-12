using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Simulation.Goals;

namespace Arcanum.World;

/// <summary>
/// ARC-MAGIC-1229 (ledger IW2–IW4): the live light one eye-candy run keeps on its owner while it runs: the owner, the run that holds it, the overlay light slot (0..3) it
/// sits in, the light ART number of the entry and its colour.
/// </summary>
public readonly record struct EyeCandyLight(string Owner, GoalHandle Handle, int Slot, int ArtNumber, (byte R, byte G, byte B) Color);

/// <summary>
/// ARC-MAGIC-1229 (FR-LIGHT-011, ledger IW2–IW5, LT1, LT14): the light an eye candy puts on its owner. The add 0x4CCF70 takes the first free of the owner's four overlay light slots
/// (arrays 16–18 of the object, 0x43EE10) for an entry whose light is not −1; the run's begin writes the entry's light ART (class 9, the entry's number, frame 0), the flags 0x20 (no
/// light animation) and the colour there, which builds a live light owned by the object that follows it and is drawn and sampled like any light; the end node and the finish write the slot
/// back as no light. Steamwoven keeps no stored light for it: the light exists exactly while its run is shown (<see cref="AreaEyeCandyHost.Lights"/>), so it is never a stray light of the
/// map and a save writes none (the overlay runs are not saved yet, saved-runs §6).
/// </summary>
public static class AreaEyeCandyLights
{
    /// <summary>The overlay light slots of an object (the arrays 16–18 hold four words).</summary>
    public const int Slots = ObjectLightReader.OverlaySlots;

    /// <summary>The overlay slots of an object that hold a light: bit i for slot i of <paramref name="used"/> (the saved overlay lights and the live ones).</summary>
    public static int Mask(IEnumerable<int> used)
    {
        ArgumentNullException.ThrowIfNull(used);
        var mask = 0;
        foreach (var slot in used)
            if (slot is >= 0 and < Slots) mask |= 1 << slot;
        return mask;
    }

    /// <summary>The first free overlay slot of a mask (the add's loop takes the first slot whose art is −1, IW2), or −1 when all four are in use.</summary>
    public static int FirstFree(int mask)
    {
        for (var slot = 0; slot < Slots; slot++)
            if ((mask & (1 << slot)) == 0) return slot;
        return -1;
    }

    /// <summary>The sector light the live light stands for at a world cell and screen offsets (ART class 9 of the entry's number, frame 0, flags 0x20, the entry's colour); <paramref name="owner"/> is
    /// the object's key (null for the hero, which is no map object).</summary>
    public static StationaryLight ToLight(EyeCandyLight light, long cellX, long cellY, int offsetX, int offsetY, AreaObjectKey? owner)
    {
        var sector = new AreaSectorCoordinate(cellX >> 6, cellY >> 6).Packed;
        return new StationaryLight(sector, -1, cellX, cellY, offsetX, offsetY, StationaryLightFlags.NoAnimation, (9u << 28) | ((uint)light.ArtNumber << 19),
            new AmbientColor(light.Color.R, light.Color.G, light.Color.B), "eye-candy-light") { Owner = owner, Slot = light.Slot };
    }
}
