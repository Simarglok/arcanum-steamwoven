using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Ai;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-025: what the item use (0x462CC0) and the inventory window's use paths read of an item the hero holds: its object type, its
/// item flags (field 0x57), its generic flags (field 0xD3, a generic item's), whether it has a use script (point 1) and whether it has a
/// wear location (0x4655D0 answers a worn slot, not −1: a weapon, an armour item, a generic item with the torch/shield flag, EQ6).
/// </summary>
public sealed record AreaItemUseFacts(string Entity, int Type, uint ItemFlags, uint GenericFlags, bool UseScript, bool Wearable)
{
    public const int FoodType = 9, ScrollType = 10, WrittenType = 13, GenericType = 14;
    /// <summary>OIF_IS_MAGICAL, OIF_CAN_USE_BOX, OIF_NEEDS_TARGET and OIF_USE_IS_THROW (bit order of the item flag names).</summary>
    public const uint MagicalFlag = 0x4, CanUseBoxFlag = 0x80, NeedsTargetFlag = 0x100, UseIsThrowFlag = 0x10000;
    /// <summary>The generic flags of lockpicks, a trap device and a healing kit (ledger TP1, ARC-SKILLS-005/006).</summary>
    public const uint ShieldLocationFlag = 0x1, LockpickFlag = 0x2, TrapDeviceFlag = 0x4, HealingKitFlag = 0x8;
    private const int WeaponType = 5, ArmorType = 7;

    public bool CanUseBox => (ItemFlags & CanUseBoxFlag) != 0;
    public bool NeedsTarget => (ItemFlags & NeedsTargetFlag) != 0;
    public bool UseIsThrow => (ItemFlags & UseIsThrowFlag) != 0;
    public bool Magical => (ItemFlags & MagicalFlag) != 0;
    public bool Lockpick => Type == GenericType && (GenericFlags & LockpickFlag) != 0;
    public bool HealingKit => Type == GenericType && (GenericFlags & HealingKitFlag) != 0;
    public bool TrapDevice => Type == GenericType && (GenericFlags & TrapDeviceFlag) != 0;

    /// <summary>The facts of an item from its stored fields (an instance's over its prototype's, or a prototype's); null for no item.</summary>
    public static AreaItemUseFacts? From(string entity, CombatFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Item is not { } item) return null;
        var type = fields.Object.Type;
        var generic = fields.Generic?.Flags ?? 0;
        return new(entity, type, item.Flags, generic, fields.Object.ScriptPoints.Contains(SessionScriptPoint.Use),
            type is WeaponType or ArmorType || type == GenericType && (generic & ShieldLocationFlag) != 0);
    }
}

/// <summary>Where a use comes from: a right click on an item of the window (the original acts on its release, IW5), or a held item released
/// on the use icon (IW6).</summary>
public enum AreaItemUseSource { RightClick, UseIcon }

public enum AreaItemUseGate
{
    /// <summary>The use runs (<see cref="AiExecutorRules.UseItemNow"/>).</summary>
    Use,
    /// <summary>A worn item, or one with a wear location: the right click does nothing (IW5).</summary>
    Wearable,
    /// <summary>No <c>OIF_CAN_USE_BOX</c>: the use check (0x462C30) refuses; the item stays (IW5, IW6).</summary>
    NotUsable,
    /// <summary>In a turn-based round while another critter has the turn: nothing (IW5, IW6).</summary>
    NotHeroTurn,
    /// <summary>An item whose use is a throw, released on the use icon: it takes the map drop's path (flag 4), the item leaves the hero onto
    /// the cursor and the window closes, and a click on a tile throws it (goal 37). NOT READY in the viewer (no game cursor holds an item).</summary>
    Throw,
}

/// <summary>
/// ARC-UI-025: the inventory window's item use rules (ledger IW5, IW6) and the use's facts for <see cref="AiExecutorRules.UseItemNow"/>.
/// </summary>
public static class AreaItemUse
{
    /// <summary>
    /// Whether a use runs: a right click does nothing for a worn or wearable item (IW5); the use icon sends an item whose use is a throw
    /// to the map drop's path (IW6; checked after the use check); both need <c>OIF_CAN_USE_BOX</c> (0x462C30) and, in a turn-based round,
    /// the hero's turn (0x4B6D70, 0x4B6D80).
    /// </summary>
    public static AreaItemUseGate Gate(AreaItemUseSource source, AreaItemUseFacts facts, bool worn, bool inRound, bool heroTurn)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (source == AreaItemUseSource.RightClick && (worn || facts.Wearable)) return AreaItemUseGate.Wearable;
        if (!facts.CanUseBox) return AreaItemUseGate.NotUsable;
        if (source == AreaItemUseSource.UseIcon && facts.UseIsThrow) return AreaItemUseGate.Throw;
        return inRound && !heroTurn ? AreaItemUseGate.NotHeroTurn : AreaItemUseGate.Use;
    }

    /// <summary>The use's target: the hero for a right click; for the use icon the hero with a Shift key held, else none (IW6).</summary>
    public static string? Target(AreaItemUseSource source, bool shift) =>
        source == AreaItemUseSource.RightClick || shift ? AiSchedulerRules.Pc : null;

    /// <summary>The facts <see cref="AiExecutorRules.UseItemNow"/> reads: the kind, the scroll test, the flags, and the spell charges and
    /// magical flag of the item's spell facts (none without them).</summary>
    public static AiUsedItem Used(AreaItemUseFacts facts, SessionItemSpells? spells, bool scrollTooHard)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var kind = facts.Type switch
        {
            AreaItemUseFacts.FoodType => AiItemKind.Food,
            AreaItemUseFacts.ScrollType => AiItemKind.Scroll,
            AreaItemUseFacts.WrittenType => AiItemKind.Written,
            AreaItemUseFacts.GenericType => AiItemKind.Generic,
            _ => AiItemKind.Other,
        };
        return new(kind, scrollTooHard, facts.NeedsTarget, facts.TrapDevice, spells?.Charges ?? 0, facts.Magical || spells?.Magical == true,
            facts.Lockpick, facts.HealingKit);
    }
}
