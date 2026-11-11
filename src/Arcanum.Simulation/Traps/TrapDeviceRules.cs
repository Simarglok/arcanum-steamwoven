using Arcanum.Simulation.Magic;

namespace Arcanum.Simulation.Traps;

/// <summary>What the use of a trap device does, by the device's slot-1 spell (ledger IA13, TP2).</summary>
public enum TrapDeviceKind
{
    /// <summary>Slot 1 (field 0x65) holds a spell: the device is destroyed with no check of the target and the spell runs at the tile after the fuse (a timed bomb).</summary>
    Timed,
    /// <summary>Slot 1 is empty: an ordinary script trap armed on a portal, a container or a tile (<see cref="TrapDeviceRules.ScriptOnObject"/>, <see cref="TrapDeviceRules.ScriptOnTile"/>; ARC-WORLD-125).</summary>
    Script,
    /// <summary>Slot 1 holds a number no spell record has (the executable would read past its record table): the device does nothing.</summary>
    Invalid,
}

/// <summary>What a timed device placement writes: the spell its fuse casts, the marker prototype made first on a tile (0: none), the alarm (record 176) and the fuse.</summary>
public readonly record struct TrapDevicePlan(TrapDeviceKind Kind, int Spell, int MarkerPrototype, bool Alarm, long FuseMs);

/// <summary>
/// ARC-WORLD-125 (ledger IA13, TP2): what a script trap device's use does. <see cref="Armed"/>: the item's script is written onto the target object (<see cref="OnTile"/> false) or onto a marker made
/// at the tile (<see cref="MarkerPrototype"/>), the target gets the PC-placed and spotted marks and the device is destroyed. Otherwise the device is kept and the player gets line
/// <see cref="RefusalLine"/> of <c>mes\trap.mes</c> (2: the target takes no script trap, 3: the tile holds a trap object already).
/// </summary>
public readonly record struct ScriptTrapPlan(bool Armed, bool OnTile, int MarkerPrototype, int RefusalLine);

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

    /// <summary>The object types a script trap can be armed on, and the trap object's own type (ledger IA13; the object field 0x14F).</summary>
    public const int ObjectTypeWall = 0, ObjectTypePortal = 1, ObjectTypeContainer = 2, ObjectTypeTrap = 17;

    /// <summary>The lines of <c>mes\trap.mes</c> a refused script trap shows the player (ledger IA13): the target takes none, the tile holds a trap object.</summary>
    public const int TargetRefusedLine = 2, TileRefusedLine = 3;

    /// <summary>The first prototype of the native scripts' markers (the dwords 26000..26007 of the table at 0x5B5F60, one for each of 30000..30007) and the two the other scripts take.</summary>
    public const int MarkerOfFirstNative = 26000, MarkerOfMagicalScript = 26000, MarkerOfOtherScript = 26001;

    /// <summary>
    /// The marker prototype a script trap on a tile makes (0x4BC480): the table 0x5B5F60 for a native script 30000..30007, otherwise prototype 26000 when the script's kind (0x4BBE00) is magical and 26001
    /// for every other kind, also for a script with no trap flag.
    /// </summary>
    public static int ScriptMarkerPrototype(int script, TrapClass kind) =>
        script is >= TrapRules.FirstNativeScript and <= TrapRules.LastNativeScript ? MarkerOfFirstNative + (script - TrapRules.FirstNativeScript)
            : kind == TrapClass.Magical ? MarkerOfMagicalScript : MarkerOfOtherScript;

    /// <summary>
    /// A script trap on an object (0x4BC2E0): only a portal (type 1) or a container (type 2) whose use slot holds no script takes it; any other object, or one that holds a script, is refused with line 2 and the
    /// device stays. A wall (type 0) takes the tile form instead (<see cref="ScriptOnTile"/>); <paramref name="targetUseScript"/> is the number now in the target's slot 1 (0 for none).
    /// </summary>
    public static ScriptTrapPlan ScriptOnObject(int objectType, int targetUseScript) =>
        objectType is ObjectTypePortal or ObjectTypeContainer && targetUseScript == 0 ? new(true, false, 0, 0) : new(false, false, 0, TargetRefusedLine);

    /// <summary>A script trap on a tile (0x4BC480): refused with line 3 when a trap object (type 17) stands there already (0x4BCAB0), else a marker of <see cref="ScriptMarkerPrototype"/> carries the script.</summary>
    [OriginalFunction(0x4BCAB0, OriginalFunctionScope.Partial)]
    public static ScriptTrapPlan ScriptOnTile(bool trapObjectOnTile, int script, TrapClass kind) =>
        trapObjectOnTile ? new(false, true, 0, TileRefusedLine) : new(true, true, ScriptMarkerPrototype(script, kind), 0);
}
