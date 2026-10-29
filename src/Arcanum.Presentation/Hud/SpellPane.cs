namespace Arcanum.Presentation.Hud;

/// <summary>
/// ARC-UI-053: the original's spell pane, bar mode 1 of the bottom strip's message window (docs/research/ledger/spell-runs.md "Spell pane",
/// SPN1–SPN6): the pane's background art, one button per college the hero has a rank in, and the selected college's known spells as
/// icon buttons. Coordinates are the original's 800 × 600 screen. Engine-free; the host draws it and carries the presses out (a college
/// is selected on its button's press, a spell activated on the release over its button, a right press starts a drag onto the hotkey bar).
/// </summary>
public static class SpellPaneRules
{
    public const int Colleges = 16, Ranks = 5;
    /// <summary>The bar mode the pane is (0x5506C0 with 1; its name area is the mode's text area (208, 574, 387 × 18), HK21).</summary>
    public const int BarMode = 1;
    /// <summary>The mode's background (`spell_ro.art`, 410 × 107) and its place (table 0x5C6548, drawn by 0x5503F0, SPN1).</summary>
    public const int BackgroundArt = 8, BackgroundX = 196, BackgroundY = 492;
    /// <summary>The college buttons' top and the spell buttons' top (records 0x5C6618, 0x5C6718, SPN2); the arts are 24 × 24 and 32 × 32.</summary>
    public const int CollegeButtonY = 497, CollegeButtonSize = 24, SpellButtonY = 528, SpellButtonSize = 32;

    /// <summary>The college buttons' left edges, college 0 first (records 0x5C6618).</summary>
    public static IReadOnlyList<int> CollegeButtonX { get; } = [201, 226, 251, 276, 301, 326, 351, 376, 400, 425, 450, 475, 500, 525, 550, 575];

    /// <summary>The spell buttons' left edges by rank, the same five places for every college (records 0x5C6718 + 0x50 × college).</summary>
    public static IReadOnlyList<int> SpellButtonX { get; } = [284, 334, 384, 435, 485];

    // The college buttons' arts (table 0x5B55B0): s_conveyance.art 11, s_divination 44, s_air 38, s_earth 92, s_fire 50, s_water 86,
    // s_forc 56, s_mental 98, s_meta 62, s_morph 68, s_nature 74, s_evilnecro 104, s_goodnecro 110, s_phantasm 80, s_summoning 116,
    // s_temporal 122; three frames each (idle, down, lit).
    private static readonly int[] CollegeArts = [11, 44, 38, 92, 50, 86, 56, 98, 62, 68, 74, 104, 110, 80, 116, 122];

    /// <summary>A college button's art (0x4B1A80, table 0x5B55B0); null outside the colleges (the original reads past its table).</summary>
    [OriginalFunction(0x4B1A80, OriginalFunctionScope.Partial)]
    public static int? CollegeButtonArt(int college) => college is >= 0 and < Colleges ? CollegeArts[college] : null;

    /// <summary>Every interface art number the pane draws: its background, the college buttons and the spells' icons
    /// (<see cref="HotkeyBarRules.SpellIcon"/>).</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } =
        [BackgroundArt, .. CollegeArts, .. Enumerable.Range(0, Colleges * Ranks).Select(spell => HotkeyBarRules.SpellIcon(spell)!.Value)];

    /// <summary>Whether a college's button shows (0x4B1B00): the hero has a rank in it. The original reads the rank only for a critter
    /// (0x4B1AB0); the host passes the hero's.</summary>
    [OriginalFunction(0x4B1B00, OriginalFunctionScope.Partial)]
    public static bool CollegeShown(int rank) => rank > 0;

    /// <summary>The colleges whose buttons show (0x551210's mode-1 case): those the hero has a rank in, in college order.
    /// <paramref name="ranks"/> holds the sixteen colleges' ranks.</summary>
    public static IReadOnlyList<int> ShownColleges(IReadOnlyList<int> ranks)
    {
        ArgumentNullException.ThrowIfNull(ranks);
        if (ranks.Count != Colleges) throw new ArgumentException($"expected {Colleges} college ranks", nameof(ranks));
        return [.. Enumerable.Range(0, Colleges).Where(college => CollegeShown(ranks[college]))];
    }

    /// <summary>The spells whose buttons show for <paramref name="college"/> (0x550C60): rank by rank while the hero knows the spell
    /// (0x4B1950: its rank within the college's), so a college with no rank shows none. The selected college need not be one the hero
    /// knows: the pane starts on college 0 (0x54A58C) and keeps a selection the hero later loses, and then shows no spell.</summary>
    [OriginalFunction(0x550C60, OriginalFunctionScope.Partial)]
    public static IReadOnlyList<int> ShownSpells(IReadOnlyList<int> ranks, int college)
    {
        ArgumentNullException.ThrowIfNull(ranks);
        if (ranks.Count != Colleges) throw new ArgumentException($"expected {Colleges} college ranks", nameof(ranks));
        if (college is < 0 or >= Colleges) throw new ArgumentOutOfRangeException(nameof(college));
        return [.. Enumerable.Range(0, Math.Clamp(ranks[college], 0, Ranks)).Select(rank => college * Ranks + rank)];
    }
}
