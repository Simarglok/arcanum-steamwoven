using System.Globalization;
using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Locks;
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
/// fourth line is built (<see cref="Window(AreaHoverWindowKind, string?, int?, AreaHoverCondition?, AreaGameBarTexts?)"/>); a critter's
/// level and reaction line and the combat panel are not.
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
        [.. new[] { WindowImage, DoorImage, SceneryImage }.Concat(AreaLootArtLayout.ImageNumbers).Distinct()];

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
