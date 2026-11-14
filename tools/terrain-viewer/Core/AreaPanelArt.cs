using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-024: the compact panel the original shows instead of the interface bars when the interface is hidden (ledger PA4, PA5):
/// its "Health/Fatigue Bar" window at the bottom right of the 800 × 600 screen, with a 1 px outline and a divider in (9, 9, 9),
/// and two 23-pixel columns, health (red, green while poisoned) and fatigue (blue), filled by
/// <see cref="Arcanum.Presentation.Hud.LifeBarRules.CompactFill"/>; each column's number in white at its fill's top and a
/// poisoned PC's poison level 40 rows below the health number. The viewer shows it while the interface bars are not drawn.
/// </summary>
public static class AreaCompactPanelLayout
{
    public static readonly (int X, int Y, int Width, int Height) Window = (751, 498, 49, 102);
    public static readonly (int R, int G, int B) Outline = (9, 9, 9);
    public static readonly (int X, int Top, int Bottom) Divider = (24, 1, 100);
    public const int HealthX = 1, FatigueX = 25, ColumnWidth = 23;
    public static readonly (int R, int G, int B) Health = (255, 0, 0), PoisonedHealth = (0, 255, 0), Fatigue = (0, 0, 255);
}

/// <summary>
/// ARC-UI-024: the original's follower portraits (ledger CH9, PA6): up to six 40 × 49 frames (art 503) down the screen's left
/// edge at 11, 50 + 62 × slot, each with the follower's image at 4, 4 (its portrait at 32 × 32), a health bar (art 505, 616
/// while poisoned) at 3, 39 and a fatigue bar (art 504) at 3, 44, the bars cropped to the follower's percent
/// (<see cref="Arcanum.Presentation.Hud.LifeBarRules.PortraitFill"/>), and a red fill added over the 32 × 32 image below 20 %
/// health.
/// </summary>
public static class AreaFollowerFrameLayout
{
    public const int FrameArt = 503, FatigueArt = 504, HealthArt = 505, PoisonedHealthArt = 616;
    public const int Slots = 6, X = 11, FirstY = 50, Step = 62, Width = 40, Height = 49;
    public const int ImageX = 4, ImageY = 4, PortraitSize = 32;
    public const int BarX = 3, HealthBarY = 39, FatigueBarY = 44;
    /// <summary>The bar arts' width (504, 505, 616 are 34 × 3); the viewer's fallback bars use it too.</summary>
    public const int BarWidth = 34, BarHeight = 3;

    public static IReadOnlyList<int> ArtNumbers { get; } = [FrameArt, FatigueArt, HealthArt, PoisonedHealthArt];

    /// <summary>The top left of slot <paramref name="slot"/> (0..5) on the 800 × 600 screen.</summary>
    public static (int X, int Y) Slot(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, Slots);
        return (X, FirstY + Step * slot);
    }
}

public enum AreaPortraitStatus { Ready, NotRead, CatalogueMissing, NotFound, MissingFile, Unreadable, LimitExceeded }

/// <param name="Bytes">The image file's bytes (a BMP the host decodes); null unless ready.</param>
public sealed record AreaPortraitFile(int Id, AreaPortraitStatus Status, string? Path, byte[]? Bytes, string Detail);

/// <summary>
/// ARC-UI-024: portrait images for the viewer's panels. A critter's stored portrait is its field 0xE7 (critter slot 49, as the
/// pre-made heroes' reader names it); its image is the portrait catalogue's file for the drawn size (ARC-DATA-045: under 128 the
/// small <c>portrait\&lt;stem&gt;.bmp</c>, from 128 the large one, falling back to the small one as the original's draw does),
/// read from the content stack's winner. NOT READY: the clash rule of 0x4CEB80 (an NPC showing the PC's own portrait takes another),
/// which needs the PC's portrait in the session. Godot-free.
/// </summary>
public static class AreaPortraits
{
    public const int PortraitSlot = 49;
    public const int MaxFileBytes = 256 * 1024;

    /// <summary>The stored portrait of a PC or NPC record (its effective field 0xE7); null when it is not a critter or stores
    /// none (0 or no plain number).</summary>
    public static int? Stored(AreaObjectRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Type is not (Arcanum.Data.Combat.CombatFieldReader.PcType or Arcanum.Data.Combat.CombatFieldReader.NpcType)) return null;
        var field = record.EffectiveFields.FirstOrDefault(value => value.Slot == PortraitSlot);
        return field is { Presence: AreaPresence.Value, Number: { } number and > 0 and <= int.MaxValue } ? (int)number : null;
    }

    /// <summary>Reads the images of <paramref name="ids"/> drawn at <paramref name="size"/>; each id gets its own outcome.</summary>
    public static IReadOnlyDictionary<int, AreaPortraitFile> Load(ContentStack stack, PortraitCatalogue? catalogue, IEnumerable<int> ids, int size)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(ids);
        var files = new Dictionary<int, AreaPortraitFile>();
        foreach (var id in ids.Distinct()) files[id] = catalogue is null ? new(id, AreaPortraitStatus.CatalogueMissing, null, null, PortraitCatalogue.GamePath) : Read(stack, catalogue, id, size);
        return files;
    }

    private static AreaPortraitFile Read(ContentStack stack, PortraitCatalogue catalogue, int id, int size)
    {
        var image = catalogue.Image(id, size);
        if (image.Status != PortraitImageStatus.Found || image.Path is null) return new(id, AreaPortraitStatus.NotFound, null, null, $"portrait {id}: {image.Status} {image.Stem.Status}");
        foreach (var path in image.Fallback is { } fallback ? new[] { image.Path, fallback } : [image.Path])
        {
            if (stack.Resolve(path).Winner is not { } winner) continue;
            if (winner.Length > MaxFileBytes) return new(id, AreaPortraitStatus.LimitExceeded, path, null, $"{path} {winner.Length} bytes");
            var read = stack.Read(winner);
            return read.Status == ContentReadStatus.Read && read.Bytes is { } bytes
                ? new(id, AreaPortraitStatus.Ready, path, bytes, $"{path} {bytes.Length} bytes")
                : new(id, AreaPortraitStatus.Unreadable, path, null, $"{path} {read.Status}");
        }
        return new(id, AreaPortraitStatus.MissingFile, image.Path, null, image.Fallback is null ? image.Path : $"{image.Path} and {image.Fallback}");
    }
}

/// <summary>ARC-UI-024: what the follower frames draw from the game folder: their interface art (503–505, 616), the portrait
/// catalogue, the portrait images read so far (32 × 32 draws, the small files) and, from ARC-UI-1786, the order menu's words
/// (<c>mes\follower_ui.mes</c>). Failures are reported, never thrown. Godot-free.</summary>
public sealed record AreaPortraitContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, PortraitCatalogue? Catalogue,
    IReadOnlyDictionary<int, AreaPortraitFile> Portraits, string Detail, AreaFollowerMenuTexts? Menu = null)
{
    public bool ArtReady => AreaFollowerFrameLayout.ArtNumbers.All(number => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready);

    /// <summary>No game folder (the self-test): no art and no portraits.</summary>
    public static AreaPortraitContent Empty(string reason) =>
        new(new Dictionary<int, AreaInterfaceArtLoad>(), null, new Dictionary<int, AreaPortraitFile>(), reason ?? throw new ArgumentNullException(nameof(reason)));

    /// <summary>Reads the frame art, the catalogue and the images of <paramref name="ids"/> from the game folder of
    /// <paramref name="modulePath"/>; <paramref name="known"/> (a previous load's) keeps its art and catalogue and is extended with
    /// the ids it lacks.</summary>
    public static AreaPortraitContent Load(string modulePath, IEnumerable<int> ids, AreaPortraitContent? known = null)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.Where(id => known?.Portraits.ContainsKey(id) != true).Distinct().ToArray();
        if (known is not null && wanted.Length == 0) return known;
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Failed(known, wanted, $"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Failed(known, wanted, $"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = known?.Art ?? AreaInterfaceArt.Load(stack, AreaFollowerFrameLayout.ArtNumbers.Concat(AreaFollowerPanelRules.ArtNumbers));
        PortraitCatalogue? catalogue = known?.Catalogue;
        var catalogueDetail = known is null ? "" : "catalogue kept";
        if (known is null)
        {
            var load = PortraitCatalogue.Load(stack);
            catalogue = load.Catalogue;
            catalogueDetail = $"catalogue {load.Status}";
        }
        var menu = known?.Menu ?? AreaFollowerMenuTexts.Load(stack);
        var portraits = new Dictionary<int, AreaPortraitFile>(known?.Portraits ?? new Dictionary<int, AreaPortraitFile>());
        foreach (var (id, file) in AreaPortraits.Load(stack, catalogue, wanted, AreaFollowerFrameLayout.PortraitSize)) portraits[id] = file;
        var failed = art.Values.Count(load => load.Status != AreaInterfaceArtStatus.Ready);
        return new(art, catalogue, portraits,
            $"art {art.Count - failed}/{art.Count} read; {catalogueDetail}; portraits {portraits.Values.Count(p => p.Status == AreaPortraitStatus.Ready)}/{portraits.Count} read; menu {menu.Detail}", menu);
    }

    // The game folder did not open: the wanted ids are recorded as unread, so a later load does not try them again.
    private static AreaPortraitContent Failed(AreaPortraitContent? known, IEnumerable<int> wanted, string reason)
    {
        var content = known ?? Empty(reason);
        var portraits = new Dictionary<int, AreaPortraitFile>(content.Portraits);
        foreach (var id in wanted) portraits[id] = new(id, AreaPortraitStatus.NotRead, null, null, reason);
        return content with { Portraits = portraits };
    }
}
