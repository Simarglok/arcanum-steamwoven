using Arcanum.Data.Area;
using Arcanum.Data.Combat;
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
/// container's lock and hit points, a critter's level and reaction line) are not built.
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
    public static AreaMessageEntry? Window(AreaHoverWindowKind kind, string? name, int? image)
    {
        if (string.IsNullOrEmpty(name) || kind is not (AreaHoverWindowKind.Portal or AreaHoverWindowKind.Container or AreaHoverWindowKind.Scenery
            or AreaHoverWindowKind.Critter)) return null;
        return new(AreaMessageKind.Object, name) { Icon = image, NameBlock = kind == AreaHoverWindowKind.Scenery };
    }

    private static uint Flags(AreaObjectRecord row) =>
        unchecked((uint)(row.EffectiveFields.SingleOrDefault(field => field.Slot == AreaHoverIndex.FlagsSlot && field.Presence == AreaPresence.Value)?.Number ?? 0));

    private static uint? Art(AreaObjectRecord row) =>
        row.EffectiveFields.SingleOrDefault(field => field.Slot == 0 && field.Presence == AreaPresence.Value)?.Number is { } art ? unchecked((uint)art) : null;
}
