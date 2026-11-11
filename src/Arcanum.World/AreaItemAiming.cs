using Arcanum.Simulation;
using Arcanum.Simulation.Magic;

namespace Arcanum.World;

/// <summary>What the item cursor's begin does (ledger IA4).</summary>
public enum AreaItemAimBegin
{
    /// <summary>The user cannot act (dead, unconscious, off) or the picker returned silently (IA2): nothing opens and nothing shows.</summary>
    Nothing,
    /// <summary>A mask of 0, exactly the radius bit, or the self bit without the tile bit: the item is applied at once at the user's own tile as a tile target.</summary>
    UseAtOwnTile,
    /// <summary>The action points are refused (IA4): nothing opens and nothing shows.</summary>
    NoActionPoints,
    /// <summary>The mask holds the inventory bit: the cursor aims at an item of a window (the viewer has none: NOT READY).</summary>
    InventoryTarget,
    /// <summary>The cursor opens (interface mode 0x10).</summary>
    OpenCursor,
    /// <summary>The cursor opens and, with the object bit in the mask and Shift held, the item is applied at once to the PC as the target object (IA4: no <c>Tgt_Obj_No_Self</c> test).</summary>
    UseOnPlayer,
}

/// <summary>How the apply on an object goes on (ledger IA8).</summary>
public enum AreaItemAimObject
{
    /// <summary>The use runs at once, from where the hero stands (0x462CC0 with the target).</summary>
    UseNow,
    /// <summary>The item-use goal 39 starts: it walks next to the target, then uses.</summary>
    WalkThenUse,
}

/// <summary>How the apply on a tile goes on (ledger IA9).</summary>
public enum AreaItemAimTile
{
    /// <summary>A charged or magical item: the use on a tile (0x462FC0) at once, from where the hero stands (the spell's own range and line tests decide).</summary>
    UseNow,
    /// <summary>A trap device: goal 41 (it walks next to the tile and uses the device).</summary>
    TrapGoal,
    /// <summary>Anything else: the throw starter (goal 37) at the tile (no installed item with a needs-target flag reaches it, IA16).</summary>
    Throw,
}

/// <summary>
/// ARC-DEV-365 (T-g, ledger IA1–IA14): the item cursor's rules: the target mask an item opens the cursor with (IA3), the begin (IA4), and the choice of the apply on an
/// object (IA8) and on a tile (IA9). Godot-free; the viewer asks them for each cursor and press.
/// </summary>
public static class AreaItemAiming
{
    /// <summary>
    /// The cursor's 64-bit target mask (0x5719F0, IA3). An item with spell charges (not 0, a negative count included) or the magical flag takes the BEGIN mask of its slot-0 spell
    /// (<paramref name="slotZeroBegin"/>; an empty slot reads before the record table in the original, TP1, and no installed item has one: read here as 0, an apply at once); otherwise
    /// a trap device takes an object or a tile; otherwise an item with no slot-0 spell takes any object (<see cref="SpellUiRules.ObjectBit"/> alone: no tile, no radius test, the target reaches
    /// its use script); otherwise the slot-0 spell's mask.
    /// </summary>
    [OriginalFunction(0x5719F0, OriginalFunctionScope.Partial)]
    public static ulong Mask(bool chargedOrMagical, bool trapDevice, ulong? slotZeroBegin)
    {
        if (chargedOrMagical) return slotZeroBegin ?? 0;
        if (trapDevice) return SpellUiRules.ObjectBit | SpellUiRules.TileBit;
        return slotZeroBegin ?? SpellUiRules.ObjectBit;
    }

    /// <summary>
    /// The begin of the cursor (0x571AA0 after the mask, IA2, IA4): nothing for a user that cannot act; a mask the spell activation applies at once for (<see cref="SpellUiRules.CastsAtOnce"/>) is an apply at the user's
    /// own tile; the inventory bit keeps the inventory window (the viewer has no such cursor); otherwise the action points are asked and the cursor opens, and with the object bit and Shift held the item is applied to the PC at once.
    /// </summary>
    [OriginalFunction(0x571AA0, OriginalFunctionScope.Partial)]
    public static AreaItemAimBegin Begin(ulong mask, bool userActive, bool shiftHeld, bool actionPointsAllow)
    {
        if (!userActive) return AreaItemAimBegin.Nothing;
        if (SpellUiRules.TargetsInventory(mask)) return AreaItemAimBegin.InventoryTarget;
        if (SpellUiRules.CastsAtOnce(mask)) return AreaItemAimBegin.UseAtOwnTile;
        if (!actionPointsAllow) return AreaItemAimBegin.NoActionPoints;
        return shiftHeld && (mask & SpellUiRules.ObjectBit) != 0 ? AreaItemAimBegin.UseOnPlayer : AreaItemAimBegin.OpenCursor;
    }

    /// <summary>
    /// The apply on an object (0x571E9D..0x571F48, IA8). An item with charges or the magical flag compares its slot-0 spell's range with the distance (<paramref name="inRange"/>): in range and without
    /// <c>OIF_NO_RANGED_USE</c> the use runs at once; out of range, or with the flag, goal 39 walks next to the target first. An item with neither charges nor the magical flag uses at once, unless the flag asks for the walk.
    /// </summary>
    [OriginalFunction(0x571CB0, OriginalFunctionScope.Partial)]
    public static AreaItemAimObject ApplyOnObject(bool chargedOrMagical, bool inRange, bool noRangedUse) =>
        noRangedUse || chargedOrMagical && !inRange ? AreaItemAimObject.WalkThenUse : AreaItemAimObject.UseNow;

    /// <summary>The apply on a tile (0x571FD3..0x57203B, IA9; the same function 0x571CB0 as <see cref="ApplyOnObject"/>, which carries its mark): a charged or magical item uses at once, a trap device starts goal 41, anything else is thrown (goal 37).</summary>
    public static AreaItemAimTile ApplyOnTile(bool chargedOrMagical, bool trapDevice) =>
        chargedOrMagical ? AreaItemAimTile.UseNow : trapDevice ? AreaItemAimTile.TrapGoal : AreaItemAimTile.Throw;
}
