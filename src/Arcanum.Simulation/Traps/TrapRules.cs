using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Traps;

/// <summary>What carries a trap (ledger TT1): a trap object (type 17) on a tile, or another object (a container, a door,
/// a scenery) whose use script is a trap.</summary>
public enum TrapCarrier { TrapObject = 1, TrappedObject = 2 }

/// <summary>The trap test's answer (ledger TT1): no trap, or the trap file flag of the object's use script, magical first.</summary>
public enum TrapClass { None = 0, Nonmagical = 1, Magical = 2 }

/// <summary>Who meets a trap (ledger TT2): the player, an NPC with a PC leader, an NPC without one, or no critter.</summary>
public enum TrapObserver { Player, LedNpc, FreeNpc, NotCritter }

/// <summary>What the spot attempt of one critter on one trap does (ledger TT3), before any roll.</summary>
public enum TrapSpotGate
{
    /// <summary>No spot: the observer is no critter, the target is a trap device item or carries no trap.</summary>
    NoTrap,
    /// <summary>The observer knows the trap already: the attempt answers spotted without a roll and marks nothing.</summary>
    Known,
    /// <summary>The player's spell flags hold bit 0x100: spotted without a roll, and marked.</summary>
    Detected,
    /// <summary>The spot trap roll decides.</summary>
    Roll,
}

/// <summary>What a native trap script does to its victims (ledger TT8); the effects themselves are a later slice.</summary>
public enum NativeTrapEffect
{
    /// <summary>Script 30007: the trap code takes the run and does nothing.</summary>
    None,
    /// <summary>Script 30000: a spell whose number is the top byte of the script's counters.</summary>
    Spell,
    /// <summary>Scripts 30001..30006: an attack whose damage dice come from the counters' low two bytes.</summary>
    Attack,
}

/// <summary>
/// One run of a native trap script (ledger TT8): the script, its effect, the half side of the square of victims (0: only
/// the triggerer), whether it clears its own number (header flag 0x4), and the lines of the trap eye candy table
/// (<c>Rules\TrapEyeCandy.mes</c>) the original uses: played on the triggerer, played on each victim, and the spotted mark
/// stopped on the trapped object (-1 for 30007, which uses none).
/// </summary>
public readonly record struct NativeTrapRun(int Script, NativeTrapEffect Effect, int Radius, bool AutoRemoves, int TriggererEyeCandy,
    int VictimEyeCandy, int SpottedEyeCandy);

/// <summary>The outcome of one disarm attempt (ledger TT5), in the original's order.</summary>
public enum TrapDisarm
{
    /// <summary>The target carries no trap: nothing happens.</summary>
    NotTrapped,
    /// <summary>The disarmer does not know the trap: nothing happens and the attempt counts as failed.</summary>
    NotKnown,
    /// <summary>Success: the trap is removed.</summary>
    Removed,
    /// <summary>An expert's critical success: the trap is removed and its device prototype goes to the disarmer.</summary>
    RemovedWithDevice,
    /// <summary>A critical failure: the trap fires on the disarmer through its use script.</summary>
    Sprung,
    /// <summary>An ordinary failure: nothing happens.</summary>
    Failed,
}

/// <summary>A spot mark (ledger TT4): the trap's object flags after it, whether it was written, and whether the player
/// gets the spotted line (<c>mes\trap.mes</c> line 0).</summary>
public readonly record struct TrapSpotMark(int ObjectFlags, bool Marked, bool PlayerMessage);

/// <summary>
/// ARC-WORLD-076: the trap rules of the retail 1.0.7.4 executable (ledger TT1–TT10): the trap test, who knows a trap, the
/// spot gate and mark, removal, disarm, the disarm device, a native trap script's run and the step trigger. Pure functions
/// over the trap's facts (its use script's number, file flags and counters, its object flags) and the caller's facts.
/// The single-player branch only: the multiplayer per-player lists (0x4BD430, 0x4BD1E0, 0x4BD340) are not modelled.
/// </summary>
public static class TrapRules
{
    /// <summary>The native trap scripts, which the trap code runs instead of the interpreter (ledger TT8).</summary>
    public const int FirstNativeScript = 30000, LastNativeScript = 30007;

    /// <summary>Script file flags (the SCR header): the two trap classes, auto-removal and the three victim squares.</summary>
    public const uint NonmagicalFlag = 0x1, MagicalFlag = 0x2, AutoRemoveFlag = 0x4, SquareOneFlag = 0x20, SquareTwoFlag = 0x40,
        SquareFourFlag = 0x80;

    /// <summary>Object flags (field 0x13): a trap the PC placed (OF_TRAP_PC), a spotted trap (OF_TRAP_SPOTTED), and
    /// don't-draw, which a trap object loses when it is spotted.</summary>
    public const int PlacedByPcFlag = 0x1000000, SpottedFlag = 0x2000000, DontDrawFlag = 0x100;

    /// <summary>The player's spell flag that spots every trap without a roll (environment check 0x458A80, ledger PT7).</summary>
    public const int DetectTrapsSpellFlag = 0x100;

    /// <summary>A mover with one of these spell flags (field 0x14) sets off no trap object it steps on (ledger TT9).</summary>
    public const int StepImmuneSpellFlags = 0x2 | 0x4;

    /// <summary>The disarm training the device needs (expert, ledger TT5).</summary>
    public const int ExpertTraining = 2;

    /// <summary>The device prototype of each native trap script's disarm (table 0x5B5F80, ledger TT6); 0 is none.</summary>
    private static readonly int[] DeviceByScript = [0, 6047, 7038, 7039, 7041, 7040, 10065, 0];

    /// <summary>A PC-placed trap's device: the explosives' prototype, or the second one when the counters' top byte is set.</summary>
    public const int PlacedDevice = 6047, PlacedDeviceSecond = 15101;

    public static bool IsNativeScript(int script) => script is >= FirstNativeScript and <= LastNativeScript;

    /// <summary>The trap test of an object's use script (slot 1): no script, or a header without either trap flag, is no
    /// trap; magical wins over nonmagical (ledger TT1).</summary>
    [OriginalFunction(0x4BBDD0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4BBE00, OriginalFunctionScope.Full)]
    public static TrapClass Classify(int script, uint headerFlags) =>
        script == 0 ? TrapClass.None : (headerFlags & MagicalFlag) != 0 ? TrapClass.Magical : (headerFlags & NonmagicalFlag) != 0 ? TrapClass.Nonmagical : TrapClass.None;

    /// <summary>Whether the observer knows the trap (ledger TT2): an NPC without a PC leader knows every trap the PC did
    /// not place; the player and an NPC led by a PC know a spotted or PC-placed one; anything else knows none.</summary>
    [OriginalFunction(0x4BBFE0, OriginalFunctionScope.Partial)]
    public static bool Known(TrapObserver observer, int objectFlags) => observer switch
    {
        TrapObserver.FreeNpc => (objectFlags & PlacedByPcFlag) == 0,
        TrapObserver.Player or TrapObserver.LedNpc => (objectFlags & (PlacedByPcFlag | SpottedFlag)) != 0,
        _ => false,
    };

    /// <summary>The gate of a spot attempt (ledger TT3): a PC or NPC only, never on a trap device item, only on a trap; a
    /// known trap answers at once; the player's Detect Traps flag spots without a roll; otherwise the spot trap roll
    /// (skill 7, the S10 slice) decides.</summary>
    [OriginalFunction(0x4BBE40, OriginalFunctionScope.Partial)]
    public static TrapSpotGate SpotGate(TrapObserver observer, bool trapDevice, TrapClass trap, int objectFlags, int playerSpellFlags)
    {
        if (observer == TrapObserver.NotCritter || trapDevice || trap == TrapClass.None) return TrapSpotGate.NoTrap;
        if (Known(observer, objectFlags)) return TrapSpotGate.Known;
        return (playerSpellFlags & DetectTrapsSpellFlag) != 0 ? TrapSpotGate.Detected : TrapSpotGate.Roll;
    }

    /// <summary>
    /// The spot trap skill's own case after the shared check's two rolls (ledger SP1, case 7 of 0x4C7160). A failed check that is not a
    /// critical failure, for a master (training 3), is rolled once more (<paramref name="secondRoll"/>, 1..100) and passes when
    /// <paramref name="effectiveness"/> is not below that roll plus the check's total difficulty (<paramref name="total"/>); a second roll
    /// that passes gives no critical. Then a source below expert (training under 2) never spots a magical trap, whichever roll passed, and
    /// its critical flag is cleared. The first roll's critical flag is kept on every other path. The second roll is drawn only when needed.
    /// </summary>
    public static (bool Success, bool Critical) SpotCase(bool success, bool critical, SkillTraining training, TrapClass trap, int effectiveness, int total,
        Func<int> secondRoll)
    {
        ArgumentNullException.ThrowIfNull(secondRoll);
        if (!success)
        {
            if (critical || training != SkillTraining.Master) return (false, critical);
            if (effectiveness < (long)secondRoll() + total) return (false, false);
        }
        return training < SkillTraining.Expert && trap == TrapClass.Magical ? (false, false) : (true, critical);
    }

    /// <summary>The spot mark (ledger TT4): only the player or an NPC led by a PC sets the spotted flag; a trap object also
    /// loses don't-draw. The player alone gets the spotted line, unless the caller is quiet (the device paths).</summary>
    [OriginalFunction(0x4BC090, OriginalFunctionScope.Partial)]
    public static TrapSpotMark MarkSpotted(TrapObserver observer, TrapCarrier carrier, int objectFlags, bool quiet = false)
    {
        if (observer is not (TrapObserver.Player or TrapObserver.LedNpc)) return new(objectFlags, false, false);
        var flags = objectFlags | SpottedFlag;
        if (carrier == TrapCarrier.TrapObject) flags &= ~DontDrawFlag;
        return new(flags, true, !quiet && observer == TrapObserver.Player);
    }

    /// <summary>A disarm attempt (ledger TT5) from the skill's result: nothing without a trap or when the disarmer does not
    /// know it; success removes it, and a critical success of an expert disarmer (training ≥ 2) also yields its device when
    /// it has one; a critical failure sets it off; any other failure does nothing.</summary>
    [OriginalFunction(0x4BC7B0, OriginalFunctionScope.Partial)]
    public static TrapDisarm Disarm(TrapClass trap, bool known, bool success, bool critical, int disarmTraining, int devicePrototype)
    {
        if (trap == TrapClass.None) return TrapDisarm.NotTrapped;
        if (!known) return TrapDisarm.NotKnown;
        if (!success) return critical ? TrapDisarm.Sprung : TrapDisarm.Failed;
        return critical && disarmTraining >= ExpertTraining && devicePrototype != 0 ? TrapDisarm.RemovedWithDevice : TrapDisarm.Removed;
    }

    /// <summary>The device prototype a disarm yields (ledger TT6): only for a native trap script; a PC-placed trap gives the
    /// explosives (6047), or 15101 when the counters' top byte is set; any other by the script's table entry (0: none).</summary>
    [OriginalFunction(0x4BC9C0, OriginalFunctionScope.Full)]
    public static int DevicePrototype(int script, int objectFlags, int counters)
    {
        if (!IsNativeScript(script)) return 0;
        if ((objectFlags & PlacedByPcFlag) != 0) return (counters & unchecked((int)0xFF000000)) != 0 ? PlacedDeviceSecond : PlacedDevice;
        return DeviceByScript[script - FirstNativeScript];
    }

    /// <summary>The run of a native trap script (ledger TT8), or null when the trap code does not take it (no trap file
    /// flag, or a number outside 30000..30007), so the interpreter runs it as an ordinary script. 30007 is taken and does
    /// nothing. The others hit every PC and NPC in the square of half side 4, 2 or 1 around the trapped object (file flag
    /// 0x80, 0x40, 0x20), or the triggerer alone, and clear their own number when the header has flag 0x4.</summary>
    [OriginalFunction(0x4BCB70, OriginalFunctionScope.Partial)]
    public static NativeTrapRun? NativeRun(int script, uint headerFlags)
    {
        if (Classify(script, headerFlags) == TrapClass.None || !IsNativeScript(script)) return null;
        if (script == LastNativeScript) return new(script, NativeTrapEffect.None, 0, false, -1, -1, -1);
        var radius = (headerFlags & SquareFourFlag) != 0 ? 4 : (headerFlags & SquareTwoFlag) != 0 ? 2 : (headerFlags & SquareOneFlag) != 0 ? 1 : 0;
        var line = 3 * (script - FirstNativeScript);
        return new(script, script == FirstNativeScript ? NativeTrapEffect.Spell : NativeTrapEffect.Attack, radius,
            (headerFlags & AutoRemoveFlag) != 0, line, line + 1, line + 2);
    }

    /// <summary>Whether a mover that arrives on a tile sets off the trap object there (ledger TT9): not with spell flag 0x2 or
    /// 0x4 (field 0x14); the trap's use script then runs through the object entry point and the walk stops.</summary>
    [OriginalFunction(0x4305D0, OriginalFunctionScope.Partial)]
    public static bool StepTriggers(int moverSpellFlags) => (moverSpellFlags & StepImmuneSpellFlags) == 0;
}
