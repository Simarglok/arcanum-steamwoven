using System.Globalization;
using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Reaction;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Which of the message window's hovered-object windows an object type gets (0x553BE0's dispatch, OH5): an item's window, a
/// portal's, a container's, a scenery's, a critter's panel, a trap's name as a hover text, or nothing (a wall, a projectile).</summary>
public enum AreaHoverWindowKind { None, Item, Portal, Container, Scenery, Critter, Trap }

/// <summary>
/// ARC-UI-057: the world's hovered object as the original shows it (checked in the 1.0.7.4 executable, ledger OH1–OH9), Godot-free. The
/// pointer's object (the pick 0x4F2CB0 of the world's mouse move 0x551910, which sees through click-through objects) is highlighted
/// (0x43C270) and described in the message window (0x57CCF0 → 0x553BE0). The highlight of a portal, container, scenery or item is that
/// object drawn a second time over itself, additive with the constant colour (200, 200, 200) (0x43C690); a critter's coloured rings
/// (arts 467 and 468) are not built. The window draws the object's image at (217, 510) and its name; the further lines (a portal's or
/// container's lock and hit points, a critter's level and reaction line): since ARC-UI-107 a portal's, container's and scenery's
/// fourth line is built (<see cref="Window(AreaHoverWindowKind, string?, int?, AreaHoverCondition?, AreaGameBarTexts?)"/>); since
/// ARC-UI-109 a critter's second line (level, reaction, sneak state, alignment, the dead line) and the alignment image of a hero who detects
/// alignment are built (<see cref="CritterWindow"/>); the combat panel is not.
/// </summary>
public static class AreaHoverWindow
{
    /// <summary>The highlight's colour: palette level 200 of each channel (0x43C7F1), added to the destination over the object's
    /// opaque pixels.</summary>
    public const int HighlightLevel = 200;
    /// <summary>Object flags that keep the highlight off unless the player detects the invisible (0x43C866, the environment test
    /// 0x458A80 with the mask 0x200): <c>OF_DONTDRAW</c> and <c>OF_INVISIBLE</c>.</summary>
    public const uint HiddenFlags = 0x300;
    /// <summary>The scenery window's name rectangle (0x5C7118: three lines from the title's top); the other windows write the name in
    /// the title's rectangle (0x5C70C8).</summary>
    public static readonly (int X, int Y, int Width, int Height) SceneryName = (290, 504, 291, 55);
    /// <summary>ARC-UI-107 (OH6, OH10): the fourth line's rectangle (0x5C70F8), where a portal's and container's lock state is drawn
    /// left-aligned and the hit points right-aligned, both in the text font (0x64C49C, font art 229) in red.</summary>
    public static readonly (int X, int Y, int Width, int Height) FourthLine = (290, 564, 291, 18);
    /// <summary><c>intgame.mes</c> lines of the lock state (0x555F7E–0x555FA7, 0x5560FD–0x556126): jammed (field bit 0x2) and magically held
    /// (bit 0x4) first, else locked or unlocked by 0x441D40.</summary>
    public const int LockedMessage = 9, UnlockedMessage = 10, JammedMessage = 26, HeldMessage = 27;
    /// <summary>Object flag 0x400000 (<c>OF_INVULNERABLE</c>): a container or scenery with it shows no hit points.</summary>
    public const uint InvulnerableFlag = 0x400000;
    /// <summary>ARC-UI-109 (OH7, OH11): the critter window's second line, the rectangle 0x5C70D8 (290, 528, 291 × 18): a living critter's "Level: N"
    /// right-aligned and a living NPC's reaction, alignment or sneak line left-aligned, both in the white text font (0x64C498), or the dead
    /// critter's line in the red one (0x64C49C).</summary>
    public static readonly (int X, int Y, int Width, int Height) SecondLine = (290, 528, 291, 18);
    /// <summary><c>intgame.mes</c> lines of the second line (0x553F70): the level label (0), the reaction label (1), the dead line (16), the
    /// alignment label (0x24) and the sneak label (0x25), and the sneak states from "seen" (0x2A) through 0x29, 0x28 and 0x27 to "hidden" (0x26).</summary>
    public const int LevelMessage = 0, ReactionMessage = 1, DeadMessage = 0x10, AlignmentMessage = 0x24, SneakMessage = 0x25, SneakSeenMessage = 0x2A, SneakHiddenMessage = 0x26;
    /// <summary>The object type of an NPC (field 0x14F = 16): only an NPC has the left part of the second line.</summary>
    public const int NpcType = CombatFieldReader.NpcType;
    /// <summary>The alignment stat's range (stat 19, the minimum table 0x5B5194 and the maximum table 0x5B5204: −1000..1000) cut into six bands
    /// (<see cref="AlignmentBands"/>) the alignment image follows; its arts by band (table 0x5C6FCC), evil to good.</summary>
    public const int AlignmentMinimum = -1000, AlignmentMaximum = 1000, AlignmentBands = 6;
    public static IReadOnlyList<int> AlignmentImages { get; } = [634, 390, 389, 388, 387, 386];
    /// <summary>The images 0x553D10 gives a portal (by its art: <c>IconWindow</c> 786 or <c>Door_Icon</c> 436), scenery (437, named
    /// <c>LevelUpIcon</c> like 432) and an object type it has no rule for (432).</summary>
    public const int WindowImage = 786, DoorImage = 436, SceneryImage = 437, GeneralImage = AreaLootArtLayout.GeneralImage;

    /// <summary>The window an object type gets (0x553BE0 in the message mode, table 0x553CF4).</summary>
    public static AreaHoverWindowKind Kind(int type) => type switch
    {
        CombatFieldReader.PortalType => AreaHoverWindowKind.Portal,
        CombatFieldReader.ContainerType => AreaHoverWindowKind.Container,
        CombatFieldReader.SceneryType => AreaHoverWindowKind.Scenery,
        >= CombatFieldReader.WeaponType and <= CombatFieldReader.GenericType => AreaHoverWindowKind.Item,
        CombatFieldReader.PcType or CombatFieldReader.NpcType => AreaHoverWindowKind.Critter,
        CombatFieldReader.TrapType => AreaHoverWindowKind.Trap,
        _ => AreaHoverWindowKind.None,
    };

    /// <summary>Whether the hovered object is highlighted by the second draw (0x43C690's branch for an object that is not a critter): a
    /// portal, container, scenery or item (types 1–3 and 5–14) without <see cref="HiddenFlags"/>. NOT READY: the player who detects the
    /// invisible sees the highlight on such an object too (the viewer draws none of them).</summary>
    public static bool Highlighted(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Kind(row.Type) is AreaHoverWindowKind.Portal or AreaHoverWindowKind.Container or AreaHoverWindowKind.Scenery or AreaHoverWindowKind.Item
            && (Flags(row) & HiddenFlags) == 0;
    }

    /// <summary>The image 0x553D10 draws for a hovered portal (art class 3 with art bit 0x400: <see cref="DoorImage"/>, else
    /// <see cref="WindowImage"/>; 0x504260), container (by its description, <see cref="AreaLootArtLayout.HolderImage"/>) or scenery;
    /// null for other types (an item's is its type icon, a critter's its portrait or <see cref="AreaLootArtLayout.HolderImage"/>).</summary>
    public static int? Image(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Kind(row.Type) switch
        {
            AreaHoverWindowKind.Portal => Art(row) is { } art && art >> 28 == 3 && (art & 0x400) != 0 ? DoorImage : WindowImage,
            AreaHoverWindowKind.Container => AreaLootArtLayout.HolderImage(row.Type, 0, -1, row.PrototypeNumber - 20),
            AreaHoverWindowKind.Scenery => SceneryImage,
            _ => null,
        };
    }

    /// <summary>Every image the window can draw for a portal, container or scenery (the critters' come with the loot window's,
    /// <see cref="AreaLootArtLayout.ImageNumbers"/>).</summary>
    public static IReadOnlyList<int> ImageNumbers { get; } =
        [.. new[] { WindowImage, DoorImage, SceneryImage }.Concat(AreaLootArtLayout.ImageNumbers).Concat(AlignmentImages).Distinct()];

    /// <summary>The window of a hovered portal, container, scenery or critter: its image and its name (in the scenery's name block for
    /// scenery, else on the title's line). Null for a name the original does not draw (an empty scenery name draws nothing).</summary>
    public static AreaMessageEntry? Window(AreaHoverWindowKind kind, string? name, int? image) => Window(kind, name, image, null);

    /// <summary>ARC-UI-107 (OH6, OH10): the window with its fourth line. A portal's and container's lock state (<see cref="LockMessage"/>
    /// of <paramref name="texts"/>, the project's when null) goes in <see cref="AreaMessageEntry.Line"/> and the hit points
    /// (<see cref="HitPointText"/>, when <see cref="ShowsHitPoints"/>) in <see cref="AreaMessageEntry.Value"/>; without a
    /// <paramref name="condition"/> neither is drawn (a critter's lines are its bars').</summary>
    public static AreaMessageEntry? Window(AreaHoverWindowKind kind, string? name, int? image, AreaHoverCondition? condition, AreaGameBarTexts? texts = null)
    {
        if (string.IsNullOrEmpty(name) || kind is not (AreaHoverWindowKind.Portal or AreaHoverWindowKind.Container or AreaHoverWindowKind.Scenery
            or AreaHoverWindowKind.Critter)) return null;
        var entry = new AreaMessageEntry(AreaMessageKind.Object, name) { Icon = image, NameBlock = kind == AreaHoverWindowKind.Scenery };
        if (condition is not { } state) return entry;
        var lockLine = ShowsLock(kind) && state.LockFlags is { } flags ? (texts ?? AreaGameBarTexts.Project("hover")).Text(LockMessage(flags, state.Locked)) : "";
        var hitPoints = ShowsHitPoints(kind, state.Invulnerable) ? HitPointText(state.HitPoints, state.MaximumHitPoints) : "";
        return entry with { Line = lockLine, Value = hitPoints };
    }

    /// <summary>ARC-UI-109 (OH11): the band of an alignment value (0x553F70 before the image): the value less the minimum over a sixth of the
    /// range (333), at most 5 (the top value falls in a sixth band the original caps). A value below the minimum reads the first band (the
    /// original reads before its table).</summary>
    public static int AlignmentBand(int alignment) =>
        Math.Clamp((alignment - AlignmentMinimum) / ((AlignmentMaximum - AlignmentMinimum) / AlignmentBands), 0, AlignmentBands - 1);

    /// <summary>The image of a hero who detects alignment (spell flag 0x80, <c>OSF_DETECTING_ALIGNMENT</c>) for a critter of this alignment,
    /// in place of its portrait or type image (0x554560 with table 0x5C6FCC).</summary>
    public static int AlignmentImage(int alignment) => AlignmentImages[AlignmentBand(alignment)];

    /// <summary>The <c>intgame.mes</c> line of a sneak state: the excess 0 (seen) to 3 count down from 0x2A, any more is 0x26 (hidden).</summary>
    public static int SneakStateMessage(int excess) => excess is >= 0 and <= 3 ? SneakSeenMessage - excess : SneakHiddenMessage;

    /// <summary>
    /// ARC-UI-109 (OH7, OH11): a hovered critter's window with its second line. A dead critter (0x45D8D0: hit points 0 or less) gets the
    /// dead line (<see cref="DeadMessage"/>, red) alone. A living one gets "Level: N" right-aligned (<see cref="LevelMessage"/>, the level
    /// stat) and, for an NPC, a left part by what the hero is: concealed shows "Sneak: state" (<see cref="SneakStateMessage"/> of the
    /// sneak excess), else one who detects alignment "Alignment: N" (the alignment stat over 10), else "Reaction: R (category)" (the effective reaction
    /// toward the hero and its category name). A hero who detects alignment sees the alignment image in place of
    /// <paramref name="image"/> (a portrait too, which the caller then leaves out). Null for an empty name.
    /// </summary>
    public static AreaMessageEntry? CritterWindow(string? name, int? image, AreaHoverCritter critter, AreaGameBarTexts? texts = null)
    {
        if (string.IsNullOrEmpty(name)) return null;
        texts ??= AreaGameBarTexts.Project("hover");
        var entry = new AreaMessageEntry(AreaMessageKind.Object, name)
        {
            Icon = critter.ViewerDetectsAlignment ? AlignmentImage(critter.Alignment) : image,
        };
        if (critter.Dead) return entry with { SecondLine = texts.Text(DeadMessage), SecondColour = AreaMessageColour.Red };
        var level = string.Create(CultureInfo.InvariantCulture, $"{texts.Text(LevelMessage)}: {critter.Level}");
        if (critter.Type != NpcType) return entry with { SecondValue = level };
        string left;
        if (critter.ViewerSneaks) left = critter.SneakExcess is { } excess ? $"{texts.Text(SneakMessage)}: {texts.Text(SneakStateMessage(excess))}" : "";
        else if (critter.ViewerDetectsAlignment) left = string.Create(CultureInfo.InvariantCulture, $"{texts.Text(AlignmentMessage)}: {critter.Alignment / 10}");
        else if (critter.Reaction is { } reaction)
            left = string.Create(CultureInfo.InvariantCulture, $"{texts.Text(ReactionMessage)}: {reaction} ({texts.ReactionName((int)ReactionRules.Category(reaction))})");
        else left = "";
        return entry with { SecondLine = left, SecondValue = level };
    }

    /// <summary>Whether the window has a lock line: a portal (0x555F6E) and a container (0x5560ED).</summary>
    public static bool ShowsLock(AreaHoverWindowKind kind) => kind is AreaHoverWindowKind.Portal or AreaHoverWindowKind.Container;

    /// <summary>Whether the window has a hit-point line: a portal's always (0x555FE3, no flag test), a container's (0x556162) and scenery's
    /// (0x555E43) only without <see cref="InvulnerableFlag"/>.</summary>
    public static bool ShowsHitPoints(AreaHoverWindowKind kind, bool invulnerable) =>
        kind == AreaHoverWindowKind.Portal || kind is AreaHoverWindowKind.Container or AreaHoverWindowKind.Scenery && !invulnerable;

    /// <summary>The <c>intgame.mes</c> line of the lock state (0x555F6E for a portal, 0x5560ED for a container): the jammed bit gives
    /// <see cref="JammedMessage"/>, else the magically held bit <see cref="HeldMessage"/>, else <see cref="LockedMessage"/> when the
    /// object counts as locked (0x441D40, <c>SessionOpenableState.Locked</c>), else <see cref="UnlockedMessage"/>.</summary>
    public static int LockMessage(int lockFlags, bool locked) =>
        (lockFlags & LockRules.JammedBit) != 0 ? JammedMessage : (lockFlags & LockRules.MagicallyHeldBit) != 0 ? HeldMessage : locked ? LockedMessage : UnlockedMessage;

    /// <summary>The hit-point text "%d/%d" (0x5C7848): the current hit points first, then the maximum (0x43D600, 0x43D5A0); the
    /// current ones may be 0 or negative.</summary>
    public static string HitPointText(int current, int maximum) => string.Create(CultureInfo.InvariantCulture, $"{current}/{maximum}");

    /// <summary>The hover facts of a hovered portal, container or scenery the session knows (null for an object it holds no damage facts
    /// of): the current and maximum hit points (<c>hp_pts + hp_adj</c> less the damage, DU1–DU2), the invulnerable flag and, for a portal or
    /// container, its lock flags and whether it counts as locked now (a junk pile never does).</summary>
    public static AreaHoverCondition? Condition(GameSessionOwner session, AreaObjectAttackTarget target)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(target);
        if (target.Object is not { } obj) return null;
        try
        {
            var state = session.QueryObject(obj);
            int? flags = null;
            var locked = false;
            if (obj.Baseline.Openable is { } openable)
            {
                var current = session.QueryOpenable(new SessionOpenable(obj.Map, obj.Entity), openable);
                flags = current.Flags;
                locked = current.Locked;
            }
            return new(state.HitPoints, unchecked(state.HitPoints + state.Damage), obj.Baseline.Invulnerable, flags, locked);
        }
        catch (ArgumentException)
        {
            // The session refuses an object whose baseline is outside its rules: the window then has its image and name only.
            return null;
        }
    }

    private static uint Flags(AreaObjectRecord row) =>
        unchecked((uint)(row.EffectiveFields.SingleOrDefault(field => field.Slot == AreaHoverIndex.FlagsSlot && field.Presence == AreaPresence.Value)?.Number ?? 0));

    private static uint? Art(AreaObjectRecord row) =>
        row.EffectiveFields.SingleOrDefault(field => field.Slot == 0 && field.Presence == AreaPresence.Value)?.Number is { } art ? unchecked((uint)art) : null;
}

/// <summary>ARC-UI-107: what a hovered portal's, container's or scenery's fourth line (0x5C70F8) is made of: the object's current and
/// maximum hit points, its invulnerable flag, and for a portal or container its lock flags (portal field 0x2E, container 0x38) and
/// whether it counts as locked now.</summary>
public readonly record struct AreaHoverCondition(int HitPoints, int MaximumHitPoints, bool Invulnerable, int? LockFlags, bool Locked);

/// <summary>
/// ARC-UI-109 (OH7, OH11): what a hovered critter's second line is made of: its object type (15 PC, 16 NPC), whether it is dead, its level and
/// alignment stats, its effective reaction toward the hero (null when the host has none: the left part is then empty), and the hero's own
/// state: concealed (<see cref="SneakExcess"/> is then how well it hides, 0 seen; null when the host cannot tell: the left part is then empty), detecting alignment (spell flag 0x80).
/// </summary>
public readonly record struct AreaHoverCritter(int Type, bool Dead, int Level, int Alignment, int? Reaction, bool ViewerSneaks, int? SneakExcess, bool ViewerDetectsAlignment);
