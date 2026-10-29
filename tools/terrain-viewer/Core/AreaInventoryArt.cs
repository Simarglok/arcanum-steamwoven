using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-051: the original look of the player's own inventory window (mode 0), read from the redraw 0x576520 and the open 0x572340
/// (ledger IW20–IW25). The window is 800 × 400 at (0, 41) of the 800 × 600 screen, the frame of <see cref="AreaInventoryDrag"/>; its
/// background is the paper doll art 223 at (0, 0) and the pack art 221 at (358, 0) (0x5782D0 gives 223 to modes 0, 5 and 6); an empty wear
/// slot shows its cover art at the slot's top-left (table 0x5CAF58 in the slot order of 0x5CAC58); the drop box (224, with the pointer
/// and a held item over it 225) is at (703, 344) and the use box (342 / 343) at (703, 290), their hit areas the art's size (IW23); three
/// text lines from (145, 29) 15 rows apart and the gold and four ammunition counts down the pack's right column (IW24). Godot-free.
/// </summary>
public static class AreaInventoryArtLayout
{
    public const int Width = 800, Height = 400, ScreenY = 41;
    public const int DollArt = 223, PackArt = 221, PackX = 358;
    public const int DropOffArt = 224, DropOnArt = 225, UseOffArt = 342, UseOnArt = 343;
    public static readonly (int X, int Y) DropBox = (703, 344), UseBox = (703, 290);

    /// <summary>The cover art an empty wear slot shows (0x5CAF58).</summary>
    public static IReadOnlyDictionary<EquipmentSlot, int> Covers { get; } = new Dictionary<EquipmentSlot, int>
    {
        [EquipmentSlot.Helmet] = 241, [EquipmentSlot.Ring1] = 246, [EquipmentSlot.Ring2] = 247, [EquipmentSlot.Medallion] = 245,
        [EquipmentSlot.Weapon] = 249, [EquipmentSlot.Shield] = 248, [EquipmentSlot.Armor] = 244, [EquipmentSlot.Gauntlets] = 243,
        [EquipmentSlot.Boots] = 242,
    };

    /// <summary>The weight, encumbrance and speed lines' rectangles (0x5CAE98).</summary>
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> Lines { get; } = [(145, 29, 185, 15), (145, 44, 185, 15), (145, 59, 185, 15)];

    /// <summary>The gold count's and the four ammunition counts' rectangles (x 0x5CAFC0, y 0x5CAFD4, 58 × 20).</summary>
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> Counts { get; } =
        [(729, 70, 58, 20), (729, 93, 58, 20), (729, 116, 58, 20), (729, 139, 58, 20), (729, 162, 58, 20)];

    public static IReadOnlyList<int> ArtNumbers { get; } = [DollArt, PackArt, DropOffArt, DropOnArt, UseOffArt, UseOnArt, .. Covers.Values];

    /// <summary>The top-left of a box's art in the window.</summary>
    public static (int X, int Y) Box(AreaInventoryIcon icon) => icon == AreaInventoryIcon.Drop ? DropBox : UseBox;

    /// <summary>The art a box shows: its "on" art while an item is held over it (the move handler 0x573D74, mode 0), else its "off" art.</summary>
    public static int BoxArt(AreaInventoryIcon icon, bool heldOver) => icon == AreaInventoryIcon.Drop
        ? heldOver ? DropOnArt : DropOffArt
        : heldOver ? UseOnArt : UseOffArt;

    /// <summary>The place of an item's art in its rectangle: centred, as the redraw centres a grid item's art in its cells
    /// ((cells × 32 − art) / 2) and a worn item's in its slot. A grid item's cells come from its art's size (0x461780, ARC-UI-055), so its
    /// art fits them. PROJECT: an art larger than the rectangle is scaled down to fit (a grid item sized without its art, when the game's
    /// art did not read for the session, or an art larger than its doll slot).</summary>
    public static (float X, float Y, float Width, float Height) Place((float X, float Y, float Width, float Height) rect, int artWidth, int artHeight)
    {
        if (artWidth <= 0 || artHeight <= 0) throw new ArgumentOutOfRangeException(nameof(artWidth));
        var scale = Math.Min(1f, Math.Min(rect.Width / artWidth, rect.Height / artHeight));
        var (w, h) = (artWidth * scale, artHeight * scale);
        return (rect.X + (int)((rect.Width - w) / 2), rect.Y + (int)((rect.Height - h) / 2), w, h);
    }
}

/// <summary>ARC-UI-051: the texts of the window's lines: <c>mes\inven_ui.mes</c> 9 (total weight), 6 (its unit), 10 (encumbrance),
/// 11 (speed) and the encumbrance levels' names, <c>mes\critter.mes</c> 18 + level (0x45F930); the project's English when a file or line
/// does not read.</summary>
public sealed class AreaInventoryTexts
{
    public const string InvenUiPath = "mes/inven_ui.mes", CritterPath = "mes/critter.mes";
    public const int WeightLine = 9, UnitLine = 6, EncumbranceLine = 10, SpeedLine = 11, LevelNameBase = CarryRules.NameLine;
    private static readonly IReadOnlyDictionary<int, string> ProjectInvenUi = new Dictionary<int, string>
    {
        [WeightLine] = "Total weight", [UnitLine] = "stone", [EncumbranceLine] = "Encumbrance", [SpeedLine] = "Speed",
    };
    private readonly IReadOnlyDictionary<int, string> _invenUi;
    private readonly IReadOnlyDictionary<int, string> _levels;

    private AreaInventoryTexts(IReadOnlyDictionary<int, string> invenUi, IReadOnlyDictionary<int, string> levels, string detail)
    {
        _invenUi = invenUi; _levels = levels; Detail = detail;
    }

    public string Detail { get; }

    public static AreaInventoryTexts Project(string reason) =>
        new(new Dictionary<int, string>(), new Dictionary<int, string>(), reason ?? throw new ArgumentNullException(nameof(reason)));

    public static AreaInventoryTexts Load(ContentStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var invenUi = new Dictionary<int, string>();
        var levels = new Dictionary<int, string>();
        var details = new List<string>();
        if (AreaStartingHero.Table(stack, InvenUiPath, ContentLoadPhase.GameStart, required: true, out var invenDetail) is { } inven)
            foreach (var number in ProjectInvenUi.Keys) { if (Decoded(inven, number) is { } text) invenUi[number] = text; }
        else details.Add($"{InvenUiPath} not read ({invenDetail})");
        if (AreaStartingHero.Table(stack, CritterPath, ContentLoadPhase.GameStart, required: true, out var critterDetail) is { } critter)
            for (var level = 0; level <= CarryRules.HeaviestLevel; level++) { if (Decoded(critter, LevelNameBase + level) is { } text) levels[level] = text; }
        else details.Add($"{CritterPath} not read ({critterDetail})");
        return new(invenUi, levels, $"texts {invenUi.Count}/{ProjectInvenUi.Count} + levels {levels.Count}" + (details.Count == 0 ? "" : "; " + string.Join("; ", details)));
    }

    private static string? Decoded(MesFile table, int number) =>
        table.Find(number) is { } message && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 and <= 200 } text }
            ? text.Trim() : null;

    public string Line(int number) => _invenUi.GetValueOrDefault(number) ?? ProjectInvenUi.GetValueOrDefault(number, $"[{number}]");

    public string LevelName(int level) => _levels.GetValueOrDefault(level) ?? $"level {level}";

    /// <summary>The three lines of the redraw's mode-0 block (IW9, IW24): <c>"%s: %d %s"</c> with the total weight and the unit,
    /// <c>"%s: %s (%d)"</c> with the level's name and the carry weight times the level's threshold / 100, and <c>"%s: %d"</c> with the
    /// speed (stat 11). A line whose value is unknown is empty.</summary>
    public IReadOnlyList<string> Lines(SessionHeroCarry? carry, int? speed) =>
    [
        carry is { } weight ? $"{Line(WeightLine)}: {weight.Weight} {Line(UnitLine)}" : string.Empty,
        carry is { } level ? $"{Line(EncumbranceLine)}: {LevelName(level.Level)} ({CarryRules.Threshold(level.Level, level.CarryWeight)})" : string.Empty,
        speed is { } value ? $"{Line(SpeedLine)}: {value}" : string.Empty,
    ];
}

/// <summary>ARC-UI-051: what the inventory window draws from the game folder: its interface art (<see cref="AreaInventoryArtLayout.ArtNumbers"/>;
/// ARC-UI-052: and the loot window's, <see cref="Numbers"/>) and its texts.</summary>
public sealed record AreaInventoryArtContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, AreaInventoryTexts Texts, string Detail)
{
    /// <summary>True when every art number read: the window then shows the original look.</summary>
    public bool ArtReady => AreaInventoryArtLayout.ArtNumbers.All(Ready);

    /// <summary>ARC-UI-052: true when the loot window's own arts read too (<see cref="AreaLootArtLayout.ArtNumbers"/>): the loot window then
    /// shows the original look. A holder image that does not read is left out, not a reason for the diagnostic window.</summary>
    public bool LootArtReady => ArtReady && AreaLootArtLayout.ArtNumbers.All(Ready);

    private bool Ready(int number) => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready;

    /// <summary>Every art number the window reads: the inventory window's, the loot window's and the holders' images.</summary>
    public static IReadOnlyList<int> Numbers { get; } =
        AreaInventoryArtLayout.ArtNumbers.Concat(AreaLootArtLayout.ArtNumbers).Concat(AreaLootArtLayout.ImageNumbers).Distinct().ToArray();

    public static AreaInventoryArtContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), AreaInventoryTexts.Project(reason), reason);

    /// <summary>Reads the window's art and texts from the game folder of <paramref name="modulePath"/>; failures are reported, never thrown.</summary>
    public static AreaInventoryArtContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = AreaInterfaceArt.Load(stack, Numbers);
        var texts = AreaInventoryTexts.Load(stack);
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        return new(art, texts, $"art {art.Count - failed.Length}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}; {texts.Detail}");
    }
}
