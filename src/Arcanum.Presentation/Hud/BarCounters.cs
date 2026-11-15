namespace Arcanum.Presentation.Hud;

/// <summary>Counter 3 of the bottom strip: the number it draws and the interface art of the icon beside it (null: the original's table
/// read past its four entries, which is not reproduced, so no icon).</summary>
public readonly record struct BarCounter3(int Value, int? Icon);

/// <summary>
/// ARC-UI-2311: the two number slots of the bottom strip that 0x54B3C0 refreshes (docs/research/ledger/spell-runs.md BC1–BC8, slots 3 and
/// 4 of the table 0x5C63F8 drawn by 0x54AD00): counter 3 beside the weapon, counter 4 over the magick weapon pane. Coordinates are the
/// original's 800 × 600 screen. Engine-free; the host reads the hero's facts, draws the icon and the text, and refreshes on the
/// original's callbacks (an inventory change of the hero, a wield on or off, a gold change, a spell payment, a mode-8 open).
/// </summary>
public static class BarCounterRules
{
    /// <summary>Counter 3's rectangle (104, 512, 50 × 20) and its places; counter 4's (264, 562, 50 × 20) and its places.</summary>
    public static (int X, int Y, int Width, int Height) Counter3Rect { get; } = (104, 512, 50, 20);
    public static (int X, int Y, int Width, int Height) Counter4Rect { get; } = (264, 562, 50, 20);
    public const int Counter3Digits = 6, Counter4Digits = 3;

    /// <summary>The icon's top left (0x54B500 blits the whole frame at window (61, 68) of the bar window, which stands at screen y 441).</summary>
    public const int IconX = 61, IconY = 509;

    /// <summary>The icons: gold, a weapon's mana store, and the ammunition types 0..3 (the table 0x5C728C).</summary>
    public const int GoldIcon = 474, ManaStoreIcon = 469;
    public static IReadOnlyList<int> AmmoIcons { get; } = [250, 251, 252, 253];

    /// <summary>The bar mode in which counter 4 draws (the magick weapon pane).</summary>
    public const int Counter4Mode = MagickWeaponPaneRules.BarMode;

    /// <summary>Every interface art the counters draw.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [.. AmmoIcons, ManaStoreIcon, GoldIcon];

    /// <summary>
    /// What counter 3 shows (0x54B3C0): the wielded weapon's ammunition count with its type's icon when the weapon has an ammunition type
    /// (<paramref name="ammoType"/>, null for none; <paramref name="ammoCount"/> is the hero's first ammunition item of that type, 0 for
    /// none); otherwise the wielded item's mana store (field 92) with icon 469 when it is above 0; otherwise the hero's gold with icon 474.
    /// Without a hero (<paramref name="hasHero"/> false) 0 with icon 474.
    /// </summary>
    [OriginalFunction(0x54B3C0, OriginalFunctionScope.Partial)]
    public static BarCounter3 Counter3(bool hasHero, int? ammoType, int ammoCount, int manaStore, int gold)
    {
        if (!hasHero) return new(0, GoldIcon);
        if (ammoType is { } type) return new(ammoCount, type >= 0 && type < AmmoIcons.Count ? AmmoIcons[type] : null);
        return manaStore > 0 ? new(manaStore, ManaStoreIcon) : new(gold, GoldIcon);
    }

    /// <summary>The value counter 4 draws in bar mode 8 with a stored item: an identified item's spell charges (field 105; a negative count
    /// as −1, unlimited), −2 (nothing known) for an unidentified one.</summary>
    public static int Counter4(bool identified, int charges) => !identified ? -2 : charges < 0 ? -1 : charges;

    /// <summary>The text the original's digit writer puts into slot 3 or 4 (ledger BC5): −1 as one '-' per digit, any other negative value as one '?' per digit, otherwise
    /// the value zero-padded to <paramref name="digits"/> places (C's <c>%0Nd</c>: a longer number is not cut). The other slots' number is <c>LifeBarRules.GaugeNumber</c>.</summary>
    public static string Text(int value, int digits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(digits);
        if (value < 0) return new(value == -1 ? '-' : '?', digits);
        return value.ToString("D" + digits, System.Globalization.CultureInfo.InvariantCulture);
    }
}
