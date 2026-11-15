namespace Arcanum.Presentation.Hud;

/// <summary>
/// ARC-UI-2304: the original's magick weapon pane, bar mode 8 of the bottom strip's message window (docs/research/ledger/ui-audio.md BM6,
/// docs/research/ledger/spell-runs.md "Item spell pane", IP1–IP10): the strip's weapon button, and in mode 8 the mode's background with
/// one icon button per spell slot of the wielded item that shows (<c>ItemSpellPaneRules.PaneButtons</c> decides which). Coordinates are the
/// original's 800 × 600 screen. Engine-free; the host draws it and carries the releases out (the weapon button toggles the mode, a slot's
/// button activates the slot's spell through 0x57C040, a right press starts a drag onto the hotkey bar).
/// </summary>
public static class MagickWeaponPaneRules
{
    /// <summary>The bar mode the pane is (0x556EA0 → 0x5506C0 with 8; its name area is the mode's text area (355, 506, 227 × 18)).</summary>
    public const int BarMode = 8;

    /// <summary>The mode's background (`MagickWeapon_Rot.art`) and its place (table 0x5C6548, every mode at (196, 492), BM0).</summary>
    public const int BackgroundArt = 564, BackgroundX = 196, BackgroundY = 492;

    /// <summary>The strip's weapon button (`MagickWeapon_Butt.art`, record 0x5C6F68, made by the bar's button creator 0x54AAE0 with the
    /// push sounds 3000 and 3001); it has no hover message.</summary>
    public const int ButtonArt = 563, ButtonX = 161, ButtonY = 443;

    /// <summary>The slot buttons' top (records 0x5C6C18) and their size (the spell icons are 32 × 32).</summary>
    public const int SlotButtonY = 542, SlotButtonSize = 32;

    /// <summary>The slots' buttons' left edges, slot 0 first (records 0x5C6C18, 16 bytes each): slot i always stands at entry i, so an
    /// empty or technological slot leaves a gap.</summary>
    public static IReadOnlyList<int> SlotButtonX { get; } = [353, 402, 451, 501, 550];

    /// <summary>The icon of a spell outside 0..79 (0x4B1570): art 35 for a technological record, else 36. The pane never shows a
    /// technological slot (0x459FC0), so it draws 36 there.</summary>
    public const int OtherTechIcon = 35, OtherSpellIcon = 36;

    /// <summary>A slot button's icon (0x4B1570): the spell's own (<see cref="HotkeyBarRules.SpellIcon"/>, table 0x5B5630) for spells
    /// 0..79, else <see cref="OtherTechIcon"/> or <see cref="OtherSpellIcon"/>.</summary>
    [OriginalFunction(0x4B1570, OriginalFunctionScope.Full)]
    public static int SpellIcon(int spell, bool technological) =>
        HotkeyBarRules.SpellIcon(spell) ?? (technological ? OtherTechIcon : OtherSpellIcon);

    /// <summary>Every interface art number the pane and its button draw beyond the spell pane's icons
    /// (<see cref="SpellPaneRules.ArtNumbers"/> holds the icons of spells 0..79).</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt, ButtonArt, OtherSpellIcon];

    /// <summary>The bar mode after the weapon button's release (0x556EA0 → 0x5506C0 with 8): mode 8, or the message mode 0 when the bar
    /// already shows mode 8 (a second release closes the pane).</summary>
    [OriginalFunction(0x5506C0, OriginalFunctionScope.Partial)]
    public static int ModeAfterButton(int mode) => mode == BarMode ? 0 : BarMode;
}
