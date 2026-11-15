using Arcanum.Data.Content;
using Arcanum.Presentation.Hud;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-UI-027: the hotkey bar's interface art from the game folder (<see cref="HotkeyBarRules.ArtNumbers"/>: the slot art, the
/// light button, the skill-bar arts and the spell icons); ARC-UI-053: with the spell pane's (<see cref="SpellPaneRules.ArtNumbers"/>: its
/// background and the college buttons; the spell icons are the bar's); ARC-UI-1113: and the skill pane's background (<see cref="SkillPaneRules.ArtNumbers"/>; its buttons are the bar's skill arts); ARC-UI-1884: and the quantity pane's background and buttons (<see cref="QuantityPaneRules.ArtNumbers"/>). Godot-free.</summary>
public sealed record AreaHotkeyBarContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, string Detail)
{
    /// <summary>The numbers read: the hotkey bar's and the spell pane's arts.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [.. HotkeyBarRules.ArtNumbers.Concat(SpellPaneRules.ArtNumbers).Concat(SkillPaneRules.ArtNumbers).Concat(QuantityPaneRules.ArtNumbers).Concat(OrderLine.ArtNumbers).Concat(MapNoteEntry.ArtNumbers).Distinct()];

    /// <summary>The numbers that read, of <see cref="ArtNumbers"/>.</summary>
    public int ReadyCount => Art.Values.Count(load => load.Status == AreaInterfaceArtStatus.Ready);

    /// <summary>No game folder (the self-test): no art.</summary>
    public static AreaHotkeyBarContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), reason ?? throw new ArgumentNullException(nameof(reason)));

    /// <summary>Reads the bar's art from the game folder of <paramref name="modulePath"/>; failures are reported, never thrown.</summary>
    public static AreaHotkeyBarContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = AreaInterfaceArt.Load(stack, ArtNumbers);
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status}").ToArray();
        return new(art, $"hotkey art {art.Count - failed.Length}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}");
    }
}
