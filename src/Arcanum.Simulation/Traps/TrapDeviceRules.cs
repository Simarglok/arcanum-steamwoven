using Arcanum.Simulation.Magic;

namespace Arcanum.Simulation.Traps;

/// <summary>What the use of a trap device does, by the device's slot-1 spell (ledger IA13, TP2).</summary>
public enum TrapDeviceKind
{
    /// <summary>Slot 1 (field 0x65) holds a spell: the device is destroyed with no check of the target and the spell runs at the tile after the fuse (a timed bomb).</summary>
    Timed,
    /// <summary>Slot 1 is empty: an ordinary script trap armed on a portal, a container or a tile (ARC-WORLD-125, not built here).</summary>
    Script,
    /// <summary>Slot 1 holds a number no spell record has (the executable would read past its record table): the device does nothing.</summary>
    Invalid,
}

/// <summary>What a timed device placement writes: the spell its fuse casts, the marker prototype made first on a tile (0: none), the alarm (record 176) and the fuse.</summary>
public readonly record struct TrapDevicePlan(TrapDeviceKind Kind, int Spell, int MarkerPrototype, bool Alarm, long FuseMs);

/// <summary>
/// ARC-WORLD-124 (E6-b, ledger TP2, TP3, IA13): the trap device's use decided from its slot-1 spell, as the executable's object form (0x4BC2E0) and tile form (0x4BC480) read it.
/// Pure: the session writes the marker, the fuse event and the destroyed device, the AI host the alarm.
/// </summary>
public static class TrapDeviceRules
{
    /// <summary>The item field of the device's spell: slot 1 (field 0x65), the only slot these branches read.</summary>
    public const int SpellSlot = 1;

    /// <summary>The empty spell slot.</summary>
    public const int NoSpell = 10000;

    /// <summary>The fuse: the timer's delay argument 88 of the device branch, times 1000 (0x4BC690, ledger TP3), on the game clock.</summary>
    public const long FuseMs = 88_000;

    /// <summary>The one record whose device alerts the critters around its user (0x4AA0D0, ledger TP2).</summary>
    public const int AlarmSpell = 176;

    /// <summary>The alarm's reach in tiles around the user (the area query of 0x4AA0D0).</summary>
    public const int AlarmRadius = 10;

    /// <summary>The marker prototype of record 176 (ledger TP8).</summary>
    public const int MarkerOf176 = 4047;

    /// <summary>The marker prototype of record 220 (ledger TP8).</summary>
    public const int MarkerOf220 = 4049;

    /// <summary>The marker the tile form makes for <paramref name="spell"/> (none for any other record).</summary>
    [OriginalFunction(0x4BC480, OriginalFunctionScope.Partial)]
    public static int MarkerPrototype(int spell) => spell switch { AlarmSpell => MarkerOf176, 220 => MarkerOf220, _ => 0 };

    /// <summary>
    /// The plan of the device whose slot 1 holds <paramref name="slotSpell"/>. <paramref name="onTile"/> is the tile form (a tile or a wall target): the marker is made only there.
    /// A spell number outside the records is <see cref="TrapDeviceKind.Invalid"/>: the original reads past its table and no installed device holds one.
    /// </summary>
    [OriginalFunction(0x4BC2E0, OriginalFunctionScope.Partial)]
    public static TrapDevicePlan Plan(int slotSpell, bool onTile)
    {
        if (slotSpell == NoSpell) return new(TrapDeviceKind.Script, NoSpell, 0, false, 0);
        if (slotSpell is < 0 or >= SpellPaymentRules.Spells) return new(TrapDeviceKind.Invalid, slotSpell, 0, false, 0);
        return new(TrapDeviceKind.Timed, slotSpell, onTile ? MarkerPrototype(slotSpell) : 0, slotSpell == AlarmSpell, FuseMs);
    }
}
