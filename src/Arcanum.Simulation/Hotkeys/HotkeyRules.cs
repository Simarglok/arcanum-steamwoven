using Arcanum.Simulation.Magic;

namespace Arcanum.Simulation.Hotkeys;

/// <summary>A hotkey entry's type (the entry's field +0x8): none, an item, a skill-bar button, a spell, or an item and one of its spells.</summary>
public enum HotkeyKind { None = 0, Item = 1, Skill = 2, Spell = 3, ItemSpell = 4 }

/// <summary>The skill bar's four buttons, the index a type-2 entry holds (0x579FA0 → 0x57A0A0; the target masks of the table at
/// 0x5CB250 tell 1 and 2 apart: a critter for pick pocket, an object or a tile for disarm traps).</summary>
public enum SkillBarButton { Prowling = 0, PickPocket = 1, DisarmTraps = 2, Repair = 3 }

/// <summary>One hotkey entry or recent action: its kind, its item (types 1 and 4) and its index (the skill-bar button of type 2, the spell
/// of types 3 and 4; −1 otherwise). <see cref="Empty"/> is a free entry.</summary>
public sealed record HotkeyEntry(HotkeyKind Kind, string? Item = null, int Index = -1)
{
    public static HotkeyEntry Empty { get; } = new(HotkeyKind.None);

    public static HotkeyEntry ForItem(string item) => new(HotkeyKind.Item, item);
    public static HotkeyEntry ForSkill(SkillBarButton button) => new(HotkeyKind.Skill, null, (int)button);
    public static HotkeyEntry ForSpell(int spell) => new(HotkeyKind.Spell, null, spell);
    public static HotkeyEntry ForItemSpell(string item, int spell) => new(HotkeyKind.ItemSpell, item, spell);

    public bool IsEmpty => Kind == HotkeyKind.None;

    /// <summary>Whether the fields fit the kind: an item exactly for types 1 and 4, a skill-bar button for 2, a spell 0 or above for 3 and 4.</summary>
    public bool IsWellFormed => Kind switch
    {
        HotkeyKind.None => Item is null && Index == -1,
        HotkeyKind.Item => !string.IsNullOrEmpty(Item) && Index == -1,
        HotkeyKind.Skill => Item is null && Index is >= 0 and <= (int)SkillBarButton.Repair,
        HotkeyKind.Spell => Item is null && Index >= 0,
        HotkeyKind.ItemSpell => !string.IsNullOrEmpty(Item) && Index >= 0,
        _ => false,
    };
}

/// <summary>What an item entry does (0x54FCF0's type-1 branch): nothing, the item use (0x462CC0) on the PC or with no target, the hotkey
/// wear into a slot (0x550000), or the item held on the cursor (0x573740).</summary>
public enum HotkeyItemAction { Nothing, Use, Wear, Hold }

/// <summary>An item entry's action and the item it acts on (the entry's own item, or another of its prototype the hero holds).</summary>
public readonly record struct HotkeyItemPlan(HotkeyItemAction Action, string? Item = null, bool TargetSelf = false, int WearSlot = -1);

/// <summary>What the hotkey activation reads of an item: its object type (field 0x14F; weapon 5, ammunition 6, armour 7, gold 8, written
/// 13), its item flags (field 0x57: can use 0x80, use is throw 0x10000) and its wear location (0x4655D0, −1 for none).</summary>
public readonly record struct HotkeyItemFacts(string Entity, int Type, uint Flags, int WearSlot);

/// <summary>An item-and-spell entry's steps (0x54FCF0's type-4 branch): nothing, or the spell activation (0x57C080), first wielding the
/// item into the weapon slot when it is not the wielded weapon.</summary>
public enum HotkeyItemSpellAction { Nothing, Activate, WieldThenActivate }

/// <summary>
/// ARC-UI-022: the original's hotkey bar (ledger "Hotkey bar", HK rows, and IP7–IP9): ten entries on the keys 1..0 and two recent actions
/// (the latest on the key A). The activation (0x54FCF0) refuses a dragged entry and a PC that cannot act, then dispatches on the entry's type;
/// the recent actions follow the spell pane's and the skill bar's presses (0x57EFA0). Pure functions; the session keeps the entries and the
/// host carries out the steps with the session's item use, wear, concealment and spell activation.
/// </summary>
public static class HotkeyRules
{
    /// <summary>The bar's entries and the recent actions (0x6835E0 and 0x683518, 0x58 bytes each).</summary>
    public const int Entries = 10, RecentActions = 2;

    /// <summary>The keys 1..0 (scan codes 2..11) activate the entries 0..9 (0x54DE50 → 0x57F1D0).</summary>
    public const int FirstScancode = 2, LastScancode = 11;

    /// <summary>An item entry's item sits in the inventory location 2000 + entry (0x465760 on the drop, 0x4657A0 reads it back): it takes
    /// no cell of the grid.</summary>
    public const int ItemLocationBase = 2000;

    /// <summary>Object types the item branch tests.</summary>
    public const int WeaponType = 5, AmmoType = 6, ArmorType = 7, GoldType = 8, WrittenType = 13;

    /// <summary>Item flags the item branch tests: the use box (OIF_CAN_USE_BOX, 0x462C30 answers 0 for it, else 6) and use-is-throw.</summary>
    public const uint CanUseFlag = 0x80, UseIsThrowFlag = 0x10000;

    /// <summary>The weapon slot the type-4 branch and a weapon's wear use (1004).</summary>
    public const int WeaponSlot = 1004;

    /// <summary>The entry a key's scan code activates (0x54DE50: 2..11 → 0..9), or null for another key.</summary>
    public static int? EntryOfScancode(int scancode) =>
        scancode is >= FirstScancode and <= LastScancode ? scancode - FirstScancode : null;

    /// <summary>The key handler's test before 0x57F1D0 (0x54DE50): a press (byte +0xC 0, F1), no other critter's turn in a turn-based round
    /// and the condition 0x566EF0 false (<paramref name="blocked"/>); 0x57F1D0 then skips an entry being dragged or empty.</summary>
    [OriginalFunction(0x57F1D0, OriginalFunctionScope.Partial)]
    public static bool KeyActivates(bool pressed, bool otherCritterActs, bool blocked, HotkeyEntry entry, bool dragging) =>
        pressed && !otherCritterActs && !blocked && !dragging && !entry.IsEmpty;

    /// <summary>The activation's first tests (0x54FCF0): nothing for an entry being dragged (flag 0x1), without a local PC, for a dead or
    /// unconscious PC (0x45D8D0, 0x45D800), or one whose critter flags (field 0xDA) hold 0x20 or 0x40 (<paramref name="heldFast"/>).</summary>
    public static bool PcCanActivate(bool dragging, bool pcPresent, bool dead, bool unconscious, bool heldFast) =>
        !dragging && pcPresent && !dead && !unconscious && !heldFast;

    /// <summary>
    /// The item branch of the activation (0x54FCF0, types by object field 0x14F). Nothing unless the PC holds the entry's item. The item
    /// acted on is another item of the same prototype the PC holds (<paramref name="sameKind"/>, 0x462930: the first other one in its list)
    /// when there is one, except for a written item that can be used or is thrown (a written item's text is its own). An item that can be
    /// used and whose entry item is not thrown is used (0x462CC0): on the PC with Shift held (scan codes 0x2A, 0x36), else with no target.
    /// Otherwise by the type of the item acted on: a weapon is worn into slot 1004 and an armour into its wear location (0x550000, both
    /// with the entry's own item); ammunition and gold do nothing; any other item is held on the cursor (0x573740) after the inventory
    /// window closes (0x573440) and an item held before is put back (0x575770), unless the entry's own item is the one held already.
    /// </summary>
    [OriginalFunction(0x54FCF0, OriginalFunctionScope.Partial)]
    public static HotkeyItemPlan PlanItem(HotkeyItemFacts entry, HotkeyItemFacts? sameKind, bool heldByPc, bool shiftHeld, string? onCursor)
    {
        if (!heldByPc) return new(HotkeyItemAction.Nothing);
        var thrown = (entry.Flags & UseIsThrowFlag) != 0;
        var keepsItself = entry.Type == WrittenType && (CanUse(entry) || thrown);
        var item = !keepsItself && sameKind is { } other ? other : entry;
        if (CanUse(item) && !thrown) return new(HotkeyItemAction.Use, item.Entity, shiftHeld);
        return item.Type switch
        {
            WeaponType => new(HotkeyItemAction.Wear, entry.Entity, WearSlot: WeaponSlot),
            ArmorType => item.WearSlot < 0 ? new(HotkeyItemAction.Nothing) : new(HotkeyItemAction.Wear, entry.Entity, WearSlot: item.WearSlot),
            AmmoType or GoldType => new(HotkeyItemAction.Nothing),
            _ => entry.Entity == onCursor ? new(HotkeyItemAction.Nothing) : new(HotkeyItemAction.Hold, item.Entity),
        };
    }

    /// <summary>The item-and-spell branch of the activation (0x54FCF0): nothing unless the PC holds the item (its parent, field 0x58); the
    /// wielded weapon goes straight to the activation (0x57C080); any other item is first wielded into slot 1004.</summary>
    public static HotkeyItemSpellAction PlanItemSpell(bool heldByPc, bool isWieldedWeapon) =>
        !heldByPc ? HotkeyItemSpellAction.Nothing : isWieldedWeapon ? HotkeyItemSpellAction.Activate : HotkeyItemSpellAction.WieldThenActivate;

    /// <summary>The hotkey spell activation (0x57C080): the activation (0x57BC70) runs for a spell that is not −1 and whose record has no
    /// item triggers (0x45A030); the same test as an item slot's press (<see cref="ItemSpellPaneRules.CanActivate"/>).</summary>
    [OriginalFunction(0x57C080, OriginalFunctionScope.Partial)]
    public static bool SpellActivates(int spell, Func<int, bool?> triggers) => ItemSpellPaneRules.CanActivate(spell, triggers);

    /// <summary>
    /// The recent actions after a press of the spell pane (type 3) or the skill bar (type 2) (0x57EFA0): an action equal to the latest
    /// (the same type and, by type, the same item, index, or both) changes nothing; otherwise the latest becomes the earlier one and the
    /// action the latest. An empty action changes nothing.
    /// </summary>
    [OriginalFunction(0x57EFA0, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<HotkeyEntry> RecordAction(IReadOnlyList<HotkeyEntry> recent, HotkeyEntry action)
    {
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(action);
        if (recent.Count != RecentActions) throw new ArgumentException("Two recent actions.", nameof(recent));
        if (action.IsEmpty || SameAction(recent[0], action)) return recent;
        return [action, recent[0]];
    }

    /// <summary>0x57EFA0's comparison: the type, then the item for type 1, the index for types 2 and 3, both for type 4.</summary>
    public static bool SameAction(HotkeyEntry a, HotkeyEntry b) => a.Kind == b.Kind && a.Kind switch
    {
        HotkeyKind.Item => a.Item == b.Item,
        HotkeyKind.Skill or HotkeyKind.Spell => a.Index == b.Index,
        HotkeyKind.ItemSpell => a.Item == b.Item && a.Index == b.Index,
        _ => true,
    };

    private static bool CanUse(HotkeyItemFacts item) => (item.Flags & CanUseFlag) != 0;
}
