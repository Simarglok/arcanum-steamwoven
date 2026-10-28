namespace Arcanum.Simulation.Magic;

/// <summary>What the item spell pane's gauge shows (interface bar mode 8, 0x54B47C): the charges, unlimited charges (a negative count),
/// or nothing known (the item is not identified).</summary>
public enum ItemSpellGaugeKind { Charges, Unlimited, Unknown }

/// <summary>The item spell pane's gauge: its kind and, for <see cref="ItemSpellGaugeKind.Charges"/>, the count.</summary>
public readonly record struct ItemSpellGauge(ItemSpellGaugeKind Kind, int Charges = 0);

/// <summary>One of the item spell pane's five buttons: the item's spell slot (0..4) and the spell it names, and whether a press activates
/// it (<see cref="ItemSpellPaneRules.CanActivate"/>: a spell with item triggers shows but a press does nothing).</summary>
public readonly record struct ItemSpellButton(int Slot, int Spell, bool Activates);

/// <summary>
/// ARC-UI-016: the original's item spell pane, the interface bar's mode 8 (ledger "Item spell pane", IP rows). The bar's weapon button
/// (0x556EF0, 0x556F80) is shown while the player's wielded weapon (slot 1004) has charges or the magical flag and a positive
/// complexity; a click on it (0x556EA0 with no object) shows the pane for that weapon, a second click returns the bar to mode 0
/// (0x5506C0). The pane (0x551660) shows one button per spell slot of an identified item with a positive complexity whose spell is not
/// technological; a press activates the slot through 0x57C040, which refuses a record with item triggers. The hotkey entry of an object
/// and a spell activates through 0x57C080 with the same test.
/// </summary>
public static class ItemSpellPaneRules
{
    /// <summary>The pane's buttons and an item's spell fields (0x64..0x68).</summary>
    public const int Buttons = 5;

    /// <summary>An empty spell field (10000) as stored; the slot read (0x4CB7A0) gives −1 for it.</summary>
    public const int NoSpell = 10000;

    /// <summary>Item flags (field 0x57): identified 0x1, magical 0x4.</summary>
    public const uint IdentifiedFlag = 0x1, MagicalFlag = 0x4;

    /// <summary>The bar's weapon button (0x556EF0, shown on an inventory insert; 0x556F80 hides it when the weapon leaves slot 1004):
    /// the weapon's charges (field 0x69) are not 0, negative included, or it has the magical flag, and its complexity (field 0x60) is
    /// above 0. No weapon, no button.</summary>
    [OriginalFunction(0x556EF0, OriginalFunctionScope.Partial)]
    public static bool WeaponButtonShown(int charges, uint flags, int complexity) =>
        (charges != 0 || (flags & MagicalFlag) != 0) && complexity > 0;

    /// <summary>The pane's buttons for an item (0x551660, bar mode 8): none unless the item is identified and its complexity is above
    /// 0; then, in slot order, every slot that is not empty (<paramref name="spells"/>: the stored fields, <see cref="NoSpell"/> for
    /// none) and whose record is not technological (record flag 0x4, 0x459FC0), each activating only when
    /// <paramref name="triggers"/> says its record has no item triggers. A value outside the record table
    /// (<paramref name="technological"/> and <paramref name="triggers"/> answer null) shows no button: the original reads outside
    /// the table there (none installed, census).</summary>
    [OriginalFunction(0x551660, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<ItemSpellButton> PaneButtons(IReadOnlyList<int> spells, uint flags, int complexity, Func<int, bool?> technological,
        Func<int, bool?> triggers)
    {
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(technological);
        ArgumentNullException.ThrowIfNull(triggers);
        if ((flags & IdentifiedFlag) == 0 || complexity <= 0) return [];
        var buttons = new List<ItemSpellButton>();
        for (var slot = 0; slot < Math.Min(Buttons, spells.Count); slot++)
        {
            var spell = spells[slot];
            if (spell == NoSpell || technological(spell) is not false || triggers(spell) is not { } triggered) continue;
            buttons.Add(new(slot, spell, !triggered));
        }
        return buttons;
    }

    /// <summary>The pane's gauge (0x54B47C): an unidentified item shows nothing known; a negative charge count is unlimited.</summary>
    public static ItemSpellGauge Gauge(int charges, uint flags) =>
        (flags & IdentifiedFlag) == 0 ? new(ItemSpellGaugeKind.Unknown)
        : charges < 0 ? new(ItemSpellGaugeKind.Unlimited) : new(ItemSpellGaugeKind.Charges, charges);

    /// <summary>The item and hotkey activation wrappers (0x57C040 for an item's slot, 0x57C080 for a given spell): the activation
    /// (0x57BC70) is called only for a spell that is not −1 (an empty slot or none) and whose record has no item triggers (0x45A030,
    /// read unmasked); a value outside the record table (<paramref name="triggers"/> null) activates nothing.</summary>
    [OriginalFunction(0x57C040, OriginalFunctionScope.Partial)]
    public static bool CanActivate(int spell, Func<int, bool?> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        return spell != -1 && spell != NoSpell && triggers(spell) is false;
    }
}
