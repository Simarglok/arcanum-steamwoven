namespace Arcanum.Presentation.Hud;

/// <summary>What a drag on the hotkey bar carries (0x57E5D0): an entry of the bar, a spell of the spell pane, a skill-bar button, a
/// spell of the weapon's pane (an item and its spell), or an item held from the inventory.</summary>
public enum HotkeyDragSourceKind { Entry, Spell, SkillButton, ItemSpell, HeldItem }

/// <summary>What a release does with a drag (0x57E8D0): nothing yet (the drag goes on), end it with no change, end it on its own
/// entry, trade two entries, put the dragged thing on an entry, or empty the dragged entry.</summary>
public enum HotkeyDropKind { Continue, End, SameEntry, Move, Put, Clear }

/// <param name="Source">The entry the drag started from (a move or a clear), −1 otherwise.</param>
/// <param name="Target">The entry under the pointer (a move or a put), −1 otherwise.</param>
public readonly record struct HotkeyDrop(HotkeyDropKind Kind, int Source = -1, int Target = -1);

/// <summary>What a left release over a bar button does (0x57DC60): activate the entry or the recent action, or drop the held item on
/// the entry.</summary>
public enum HotkeyClickKind { Nothing, Activate, DropHeld }

/// <summary>Where an icon is drawn in its slot (0x57E140): its left and top in the bottom strip and the part of the art drawn from
/// its top-left corner.</summary>
public readonly record struct HotkeyIconPlace(int X, int Y, int Width, int Height);

/// <summary>What an item's type icon is chosen by (0x554C20, ARC-UI-032), as the item's fields store them: its object type (field 0x14F,
/// 5..14 for items), magick/tech complexity (field 0x60), whether it is identified (0x464370) and hexed (item flag 0x40, field 0x57), the
/// art subtype of its use-art fragment (field 0x5F, bits 6..9), the coverage of its inventory art (field 0x5D, bits 14..16), its
/// ammunition type (field 0x8F), its written subtype (field 0xCA) and its discipline (field 0x61). A field another type does not have is
/// never read.</summary>
public readonly record struct HotkeyItemIconFacts(int Type, int Complexity, bool Identified, bool Hexed, int UseArtSubtype, int Coverage,
    int AmmoType = 0, int WrittenSubtype = 0, int Discipline = 0);

/// <summary>
/// ARC-UI-027: the original's hotkey bar on the bottom strip (docs/research/ledger/spell-runs.md "Hotkey bar art", HK10–HK16): the ten
/// entry slots of the strip's top row, their icons and item counts, the two recent-action buttons, the light button, the right-button
/// drag onto the bar and the left clicks on its buttons. Coordinates are the original's 800 × 600 screen unless a member says the strip.
/// Engine-free; the host draws and carries the actions out through the game session.
/// </summary>
public static class HotkeyBarRules
{
    public const int Entries = 10;
    /// <summary>The bottom strip's top on the screen (window 1 at (0, 441), ledger GB1).</summary>
    public const int StripTop = 441;
    /// <summary>The slots' top in the bottom strip (screen 445) and the hit band's top and height on the screen (HK10).</summary>
    public const int SlotTop = 4, BandTop = 440, BandHeight = 37;
    /// <summary>The width a slot takes the pointer over (HK10); the slot art itself is 32 × 32.</summary>
    public const int HitWidth = 32;
    /// <summary>The art every slot draws under its icon (HKSlt1.ART; the table's other nine slot arts are never drawn, HK11).</summary>
    public const int SlotArt = 173;
    /// <summary>The light button at (616, 455) (HKTshOFF.ART, HK14).</summary>
    public const int LightArt = 182, LightX = 616, LightY = 455;
    /// <summary>The count printed on an item entry is drawn while it lies in 0..9999 (HK12).</summary>
    public const int MaxCount = 9999;
    /// <summary>An item's inventory art is its icon when it fits a slot (HK12).</summary>
    public const int IconSize = 32;

    /// <summary>The slots' left edges on the screen, entry 0 (key 1) first (table 0x5CB4BC, HK10).</summary>
    public static IReadOnlyList<int> SlotX { get; } = [198, 237, 276, 315, 354, 418, 456, 495, 534, 573];

    /// <summary>The two recent-action buttons, the latest first (records 0x5C6F40, HK13).</summary>
    public static IReadOnlyList<(int X, int Y)> RecentButtons { get; } = [(69, 548), (114, 548)];

    /// <summary>The skill-bar buttons' arts, by button (0 prowling, 1 pick pocket, 2 disarm traps, 3 repair; table 0x5CB230).</summary>
    public static IReadOnlyList<int> SkillArts { get; } = [279, 280, 282, 278];

    // The college spells' icons, spell 0..79 (table 0x5B5630).
    private static readonly int[] SpellArts =
    [
        12, 13, 14, 15, 16, 45, 46, 47, 48, 49, 39, 40, 41, 42, 43, 93, 94, 95, 96, 97, 51, 52, 53, 54, 55, 87, 88, 89, 90, 91,
        57, 58, 59, 60, 61, 99, 100, 101, 102, 103, 63, 64, 65, 66, 67, 69, 70, 71, 72, 73, 75, 76, 77, 78, 79, 105, 106, 107, 108, 109,
        111, 112, 113, 114, 115, 81, 82, 83, 84, 85, 117, 118, 119, 120, 121, 123, 124, 125, 126, 127,
    ];

    // ARC-UI-032: the type icons' tables as the executable lays them out from 0x5C6FE4, one after the other (0x554C20 indexes them without a
    // bound, so an index past a table reads the next): weapons by art subtype (15), ammunition by type (4, 0x5C7020), armour by art subtype
    // (9, 0x5C7030), armour by coverage (7, 0x5C7054; coverage 0 never reads it), written items by subtype (7, 0x5C7070), then the message
    // window's icons (0x5C708C, ledger ML5). What lies past them was not read.
    private const int AmmoTable = 15, ArmorTable = 19, CoverageTable = 28, WrittenTable = 35;
    private static readonly int[] TypeIconTables =
    [
        391, 391, 391, 391, 391, 391, 392, 391, 395, 391, 392, 391, 391, 391, 391,
        398, 399, 400, 401,
        402, 402, 405, 405, 405, 408, 405, 405, 402,
        0, 405, 405, 405, 405, 411, 414,
        428, 429, 431, 430, 428, 462, 428,
        438, 439, 440, 441, 442, 443, 444,
    ];

    /// <summary>The type icons that are not in the tables: coins, food (potions, therapeutics, herbal, chemical, plain), scroll, key, key
    /// ring, the generic item's three, the hexed icons (melee, bow, shield, clothes, medallion, ring, robes) and the plain hexed icon.</summary>
    public const int GoldIcon = 417, PotionIcon = 418, TherapeuticIcon = 419, HerbalIcon = 420, ChemicalIcon = 421, FoodIcon = 423,
        ScrollIcon = 425, KeyIcon = 426, KeyRingIcon = 427, GenericIcon = 432, MagickGenericIcon = 433, TechGenericIcon = 434, HexedIcon = 440,
        HexedMeleeIcon = 791, HexedBowIcon = 792, HexedShieldIcon = 793, HexedClothesIcon = 794, HexedMedallionIcon = 795, HexedRingIcon = 796,
        HexedRobesIcon = 797;

    private static readonly int[] FixedTypeIcons =
    [
        GoldIcon, PotionIcon, TherapeuticIcon, HerbalIcon, ChemicalIcon, FoodIcon, ScrollIcon, KeyIcon, KeyRingIcon, GenericIcon,
        MagickGenericIcon, TechGenericIcon, HexedIcon, HexedMeleeIcon, HexedBowIcon, HexedShieldIcon, HexedClothesIcon, HexedMedallionIcon,
        HexedRingIcon, HexedRobesIcon,
    ];

    /// <summary>Every type icon an item of types 5..14 can get (the tables' weapon, ammunition, armour and written parts with the
    /// complexity steps, and the fixed icons).</summary>
    public static IReadOnlyList<int> TypeIconNumbers { get; } =
    [
        .. TypeIconTables[..AmmoTable].Concat(TypeIconTables[ArmorTable..WrittenTable]).Where(art => art != 0)
            .SelectMany(art => new[] { art, art + 1, art + 2 })
            .Concat(TypeIconTables[AmmoTable..ArmorTable]).Concat(TypeIconTables[WrittenTable..(WrittenTable + 7)]).Concat(FixedTypeIcons)
            .Distinct().Order(),
    ];

    /// <summary>Every interface art number the bar draws: the slot art, the light button, the skill-bar arts, the spell icons and
    /// (ARC-UI-032) the items' type icons.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [.. new[] { SlotArt, LightArt }.Concat(SkillArts).Concat(SpellArts).Concat(TypeIconNumbers).Distinct()];

    /// <summary>
    /// ARC-UI-032: an item's type icon (0x554C20), the icon of an item entry whose inventory art does not fit a slot. An identified hexed
    /// weapon shows the hexed bow icon for art subtype 8, the hexed melee icon for subtypes 2, 3, 4, 5, 7 and 13, else the plain hexed icon;
    /// an identified hexed armour by coverage 5 the hexed ring, 6 the hexed medallion, another coverage the hexed shield, and with coverage
    /// 0 by its subtype 5 the hexed robes, 1 or 8 the hexed clothes, else the hexed shield; any other identified hexed item the plain hexed
    /// icon. Otherwise by type: a weapon (5) its subtype's icon and an armour (7) its coverage's (or with coverage 0 its subtype's), each one
    /// art further for a positive complexity (magick) and two for a negative one (technology); ammunition (6) its type's; gold (8) coins;
    /// food (9) potions when magickal, plain food when neutral, and when technological therapeutics for discipline 7, herbal for 0, chemical
    /// for 1, else plain food; a scroll (10), key (11) or key ring (12) its icon; a written item (13) its subtype's; a generic item (14) its
    /// own by complexity. Any other type returns the type itself (only items reach it). Null where an index leaves the tables read.
    /// </summary>
    [OriginalFunction(0x554C20, OriginalFunctionScope.Partial)]
    public static int? ItemTypeIcon(HotkeyItemIconFacts item)
    {
        if (item.Identified && item.Hexed)
            return item.Type switch
            {
                5 => item.UseArtSubtype == 8 ? HexedBowIcon : item.UseArtSubtype is 2 or 3 or 4 or 5 or 7 or 13 ? HexedMeleeIcon : HexedIcon,
                7 when item.Coverage == 0 => item.UseArtSubtype == 5 ? HexedRobesIcon : item.UseArtSubtype is 8 or 1 ? HexedClothesIcon : HexedShieldIcon,
                7 => item.Coverage switch { 5 => HexedRingIcon, 6 => HexedMedallionIcon, _ => HexedShieldIcon },
                _ => HexedIcon,
            };
        int Step(int art) => art + (item.Complexity > 0 ? 1 : item.Complexity < 0 ? 2 : 0);
        return item.Type switch
        {
            5 => Table(item.UseArtSubtype) is { } art ? Step(art) : null,
            6 => Table(AmmoTable + (long)item.AmmoType),
            7 => Table(item.Coverage == 0 ? ArmorTable + (long)item.UseArtSubtype : CoverageTable + (long)item.Coverage) is { } art ? Step(art) : null,
            8 => GoldIcon,
            9 => item.Complexity > 0 ? PotionIcon : item.Complexity == 0 ? FoodIcon
                : item.Discipline switch { 7 => TherapeuticIcon, 0 => HerbalIcon, 1 => ChemicalIcon, _ => FoodIcon },
            10 => ScrollIcon,
            11 => KeyIcon,
            12 => KeyRingIcon,
            13 => Table(WrittenTable + (long)item.WrittenSubtype),
            14 => item.Complexity > 0 ? MagickGenericIcon : item.Complexity < 0 ? TechGenericIcon : GenericIcon,
            _ => item.Type,
        };
    }

    private static int? Table(long index) => index >= 0 && index < TypeIconTables.Length ? TypeIconTables[index] : null;

    /// <summary>A spell's icon (0x4B1570): the table's art for spells 0..79; null for any other spell, whose icon depends on the
    /// spell record's technological flag (<see cref="MagickWeaponPaneRules.SpellIcon"/>, ARC-UI-2304).</summary>
    public static int? SpellIcon(int spell) => spell is >= 0 and < 80 ? SpellArts[spell] : null;

    /// <summary>A skill-bar button's icon (0x579F70); a button outside 0..3 shows prowling's.</summary>
    [OriginalFunction(0x579F70, OriginalFunctionScope.Full)]
    public static int SkillIcon(int button) => button is >= 0 and < 4 ? SkillArts[button] : SkillArts[0];

    private const int FirstCollegeIcon = 446, Colleges = 16;

    /// <summary>ARC-UI-035: a college's icon (0x4B1A90, table 0x5B55F0), which a spell hint draws: arts 446..461 for the colleges
    /// 0..15; a college outside them gets college 0's.</summary>
    [OriginalFunction(0x4B1A90, OriginalFunctionScope.Full)]
    public static int CollegeIcon(int college) => FirstCollegeIcon + (college is >= 0 and < Colleges ? college : 0);

    /// <summary>The sixteen colleges' icons.</summary>
    public static IReadOnlyList<int> CollegeIcons { get; } = [.. Enumerable.Range(FirstCollegeIcon, Colleges)];

    // The skill each skill-bar button uses (table 0x5CB240): 6 prowling, 5 pick pocket, tech 3 disarm traps (15), tech 0 repair (12).
    private static readonly int[] ButtonSkills = [6, 5, 15, 12];

    /// <summary>ARC-UI-035: the skill a skill-bar button uses (0x57A6A0, table 0x5CB240), numbered as the skill texts number them (basic
    /// 0..11, then tech 12..15); null outside buttons 0..3 (the original reads past the table there).</summary>
    [OriginalFunction(0x57A6A0, OriginalFunctionScope.Partial)]
    public static int? ButtonSkill(int button) => button is >= 0 and < 4 ? ButtonSkills[button] : null;

    /// <summary>The entry under the screen point (0x57E460): inside the band from 5 pixels left of the first slot, at y 440..476,
    /// the slot whose 32 pixels hold x; null between slots and outside the band. (The band's right edge, the last slot's left plus
    /// the band's left plus 32, reaches past the last slot; only an entry is ever acted on.)</summary>
    [OriginalFunction(0x57E460, OriginalFunctionScope.Partial)]
    public static int? EntryAt(int x, int y)
    {
        var left = SlotX[0] - 5;
        if (x < left || y < BandTop || x >= SlotX[^1] + left + HitWidth || y >= BandTop + BandHeight) return null;
        for (var i = 0; i < Entries; i++)
            if (x >= SlotX[i] && x < SlotX[i] + HitWidth) return i;
        return null;
    }

    /// <summary>Where an icon of <paramref name="width"/> × <paramref name="height"/> is drawn in the slot of
    /// <paramref name="entry"/> (0x57E140): centred in the slot art's size (each half of the spare pixels, truncated), or clipped to it
    /// from the art's top-left when larger.</summary>
    [OriginalFunction(0x57E140, OriginalFunctionScope.Partial)]
    public static HotkeyIconPlace IconPlace(int entry, int width, int height, int slotWidth = IconSize, int slotHeight = IconSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (entry is < 0 or >= Entries) throw new ArgumentOutOfRangeException(nameof(entry));
        var x = SlotX[entry]; var y = SlotTop;
        if (width > slotWidth) width = slotWidth; else x += (slotWidth - width) / 2;
        if (height > slotHeight) height = slotHeight; else y += (slotHeight - height) / 2;
        return new(x, y, width, height);
    }

    /// <summary>An item entry's count as printed at the slot's left + 2 and the strip's top (HK12); null outside 0..9999 or for no
    /// count (−1, every other entry kind).</summary>
    public static string? CountText(int count) => count is >= 0 and <= MaxCount ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>An item entry shows its inventory art when the art fits a slot (both sides at most 32); otherwise the item's type icon
    /// (0x554BE0, HK12).</summary>
    public static bool ItemShowsInventoryArt(int width, int height) => width <= IconSize && height <= IconSize;

    /// <summary>A right press starts a drag (0x57E5D0) while none is going on and no item is held; from an entry only when it is not
    /// empty.</summary>
    [OriginalFunction(0x57E5D0, OriginalFunctionScope.Partial)]
    public static bool StartsDrag(bool dragging, bool itemHeld, HotkeyDragSourceKind source, bool entryEmpty) =>
        !dragging && !itemHeld && source != HotkeyDragSourceKind.HeldItem && !(source == HotkeyDragSourceKind.Entry && entryEmpty);

    /// <summary>
    /// A release while a drag goes on (0x57E8D0): a left release goes on with the drag unless an item is held; otherwise the entry under
    /// the pointer decides. On the dragged entry itself the drag ends there; on another entry a dragged entry trades places with it
    /// (HK5's fix) and anything else is put on it. Off the bar a dragged entry is emptied (HK15's fix) and anything else ends with no change.
    /// </summary>
    [OriginalFunction(0x57E8D0, OriginalFunctionScope.Partial)]
    public static HotkeyDrop Drop(HotkeyDragSourceKind source, int sourceEntry, int? target, bool leftRelease, bool itemHeld)
    {
        if (source == HotkeyDragSourceKind.Entry && sourceEntry is < 0 or >= Entries) throw new ArgumentOutOfRangeException(nameof(sourceEntry));
        if (target is < 0 or >= Entries) throw new ArgumentOutOfRangeException(nameof(target));
        if (leftRelease && !itemHeld) return new(HotkeyDropKind.Continue);
        if (target is not { } entry)
            return source == HotkeyDragSourceKind.Entry ? new(HotkeyDropKind.Clear, sourceEntry) : new(HotkeyDropKind.End);
        if (source == HotkeyDragSourceKind.Entry)
            return entry == sourceEntry ? new(HotkeyDropKind.SameEntry, sourceEntry, entry) : new(HotkeyDropKind.Move, sourceEntry, entry);
        return new(HotkeyDropKind.Put, -1, entry);
    }

    /// <summary>A left release over an entry's button (0x57DC60): with an item held it drops the item there; while nothing is dragged
    /// it activates the entry; an empty entry does nothing.</summary>
    [OriginalFunction(0x57DC60, OriginalFunctionScope.Partial)]
    public static HotkeyClickKind ClickEntry(bool dragging, bool itemHeld, bool entryEmpty) =>
        itemHeld ? HotkeyClickKind.DropHeld : dragging || entryEmpty ? HotkeyClickKind.Nothing : HotkeyClickKind.Activate;
}
