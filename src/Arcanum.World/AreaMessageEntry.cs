namespace Arcanum.World;

/// <summary>
/// One message-log entry. A level-up entry keeps its three lines as composed (title, level line, points or scheme line; <paramref
/// name="Scheme"/> when the last one is the scheme text, which gets two lines); a text entry (message, plain, hover) keeps its whole text
/// in <paramref name="Title"/>, split at its first line break when drawn (ML4). ARC-UI-035: a skill hint keeps its name and description
/// (<paramref name="Title"/>, <paramref name="Detail"/>) and its right-hand number line in <see cref="Value"/>; a spell hint its name,
/// willpower line (<paramref name="Line"/>), cost line (<see cref="Value"/>), description and the college's icon (<see cref="Icon"/>); an
/// item window its name in <see cref="TitleColour"/> and the item's type icon. ARC-UI-057: a hovered object's window (a portal, container,
/// scenery or critter in the world) its name and image, the name in the scenery's name block when <see cref="NameBlock"/>; ARC-UI-107:
/// a portal's or container's lock state in <paramref name="Line"/> and the hit points of a portal, container or scenery in
/// <see cref="Value"/>, on the fourth line in red (left- and right-aligned). ARC-UI-109: a critter's window has a second line
/// (<see cref="SecondLine"/> left, <see cref="SecondValue"/> right, both white unless <see cref="SecondColour"/> says otherwise).
/// ARC-UI-124: with the hero in combat mode a living critter's window shows the combat panel (<see cref="CombatPanel"/>) in place of the image.
/// </summary>
public sealed record AreaMessageEntry(AreaMessageKind Kind, string Title, string Line = "", string Detail = "", bool Scheme = false)
{
    /// <summary>An icon art drawn in place of the type's own (a spell hint's college, an item's type icon); null the type's.</summary>
    public int? Icon { get; init; }
    /// <summary>A text drawn right-aligned (0x550A10 flag 2): a skill hint's number line, a spell hint's cost line, an object window's hit points.</summary>
    public string Value { get; init; } = "";
    public AreaMessageColour TitleColour { get; init; } = AreaMessageColour.White;
    /// <summary>ARC-UI-109 (OH7, OH11): a hovered critter's second line (0x5C70D8), left-aligned: a living NPC's sneak state, alignment or reaction, a
    /// dead critter's "dead" line (in <see cref="SecondColour"/>).</summary>
    public string SecondLine { get; init; } = "";
    /// <summary>ARC-UI-109: the second line's right-aligned text, a living critter's "Level: N".</summary>
    public string SecondValue { get; init; } = "";
    /// <summary>ARC-UI-109: the colour of <see cref="SecondLine"/> (red for the dead line, else white).</summary>
    public AreaMessageColour SecondColour { get; init; } = AreaMessageColour.White;
    /// <summary>ARC-UI-057: an object window's name goes in the scenery's three-line block (0x5C7118), not on the title's line.</summary>
    public bool NameBlock { get; init; }
    /// <summary>ARC-UI-124 (OH12): the combat panel (0x554830) of a hovered living critter while the hero is in combat mode; it takes the
    /// place of <see cref="Icon"/>. Null: the image.</summary>
    public AreaCombatPanel? CombatPanel { get; init; }

    public static AreaMessageEntry Text(AreaMessageKind kind, string text) =>
        kind is AreaMessageKind.LevelUp or AreaMessageKind.SkillHint or AreaMessageKind.SpellHint or AreaMessageKind.Item or AreaMessageKind.Object
            ? throw new ArgumentException("a level-up entry, a hint and an item or object window have lines", nameof(kind)) : new(kind, text ?? throw new ArgumentNullException(nameof(text)));

    /// <summary>Hover texts and hints are shown, never stored (the original's types 6–12, ML2), and so is the item window.</summary>
    public bool Stored => Kind is AreaMessageKind.LevelUp or AreaMessageKind.Message or AreaMessageKind.Plain;
}

/// <summary>
/// ARC-UI-124 (OH12): the combat panel's facts: the hit chance <paramref name="Percent"/> (drawn "N%"), the icons' flag bits as the
/// executable holds them (0x40 weapon too heavy, 0x80 out of range, 0x100 distance, 0x200 cover, 0x400 darkness, 0x800 injury, 0x20000
/// blocked line, 0x40000 magick or technology penalty) and the label of the layout without icons (<c>intgame.mes</c> 20, "To Hit:").
/// </summary>
public sealed record AreaCombatPanel(int Percent, int Flags, string Label);
