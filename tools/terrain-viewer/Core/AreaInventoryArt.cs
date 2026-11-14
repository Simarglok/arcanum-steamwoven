using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

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
/// ARC-UI-052, ARC-UI-064: and the loot and barter windows', <see cref="Numbers"/>) and its texts.</summary>
public sealed record AreaInventoryArtContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, AreaInventoryTexts Texts, string Detail)
{
    /// <summary>True when every art number read: the window then shows the original look.</summary>
    public bool ArtReady => AreaInventoryArtLayout.ArtNumbers.All(Ready);

    /// <summary>ARC-UI-052: true when the loot window's own arts read too (<see cref="AreaLootArtLayout.ArtNumbers"/>): the loot window then
    /// shows the original look. A holder image that does not read is left out, not a reason for the diagnostic window.</summary>
    public bool LootArtReady => ArtReady && AreaLootArtLayout.ArtNumbers.All(Ready);

    /// <summary>ARC-UI-064: true when the barter window's own arts read too (<see cref="AreaBarterArtLayout.ArtNumbers"/>): the barter
    /// window then shows the original look.</summary>
    public bool BarterArtReady => ArtReady && AreaBarterArtLayout.ArtNumbers.All(Ready);

    private bool Ready(int number) => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready;

    /// <summary>Every art number the window reads: the inventory window's, the loot window's, (ARC-UI-064) the barter window's, the holders'
    /// images and (ARC-UI-054) the arts over the round view and (ARC-DEV-339) the arrange button's (ARC-UI-1917) the barter window's doll view and, with the loot window's, the toggle buttons, and (ARC-UI-1120) the barter window's cycle buttons, which are drawn when they read and are no reason for the diagnostic window.</summary>
    public static IReadOnlyList<int> Numbers { get; } =
        AreaInventoryArtLayout.ArtNumbers.Concat(AreaLootArtLayout.ArtNumbers).Concat(AreaLootArtLayout.DollArtNumbers).Concat(AreaHeroDollToggle.ArtNumbers).Concat(AreaBarterArtLayout.DollArtNumbers).Concat(AreaBarterArtLayout.ArtNumbers).Concat(AreaLootArtLayout.ImageNumbers)
            .Concat([AreaInventoryHole.InventoryArt, AreaInventoryHole.BarterArt, AreaInventoryHole.LootArt, AreaInventoryArtLayout.ArrangeArt, AreaBarterArtLayout.CycleLeftArt, AreaBarterArtLayout.CycleRightArt]).Distinct().ToArray();

    public static AreaInventoryArtContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), AreaInventoryTexts.Project(reason), reason);

    /// <summary>Reads the window's art and texts from the game folder of <paramref name="modulePath"/>; failures are reported, never thrown.</summary>
    public static AreaInventoryArtContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = AreaInterfaceArt.Load(stack, Numbers);
        var texts = AreaInventoryTexts.Load(stack);
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        return new(art, texts, $"art {art.Count - failed.Length}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}; {texts.Detail}");
    }
}
