using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-142: the schematic window's original look (docs/ui/original-windows.md; ledger SW9–SW14). The window's interface art and every record's art are read
// once a module in the background; without a game folder (the synthetic self-tests) or when a required art does not read the window keeps its diagnostic
// layout.
public partial class TerrainViewerApp
{
    private Task<AreaSchematicArtContent>? _schematicArtTask;
    private AreaSchematicArtContent? _schematicArt;
    private (Dictionary<int, Texture2D[]> Frames, AreaSchematicWindowFonts Fonts)? _schematicArtLook;
    private AreaSchematicsPanel? _schematicArtPanel;
    private AreaSchematicData? _schematicTextsFor;
    private readonly Dictionary<int, Texture2D?> _schematicPictures = [];

    /// <summary>Starts reading the window's art for this module (once; a reload, the cave or a map transfer reuses it).</summary>
    private void BindAreaSchematicArt(TerrainViewerArguments arguments)
    {
        _schematicArt = null; _schematicArtLook = null; _schematicArtPanel = null; _schematicPictures.Clear();
        var module = arguments.ModulePath;
        _schematicArtTask = SelfTestBootstrap || module is null ? Task.FromResult(AreaSchematicArtContent.Empty("self-test: no game folder"))
            : Task.Run(() => AreaSchematicArtContent.Load(module));
    }

    private AreaSchematicArtContent? AdoptSchematicArt()
    {
        if (_schematicArt is null && _schematicArtTask is { IsCompleted: true } task)
        {
            _schematicArt = task.IsCompletedSuccessfully ? task.Result : AreaSchematicArtContent.Empty("load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
            GD.Print($"ARC-UI-142 schematic window art ready={_schematicArt.Ready} {_schematicArt.Detail}");
        }
        return _schematicArt;
    }

    /// <summary>Gives the window's panel the original look once its art has read; nothing before that (or when it did not read): the diagnostic layout.</summary>
    private void AttachSchematicArt(AreaSchematicsPanel panel)
    {
        if (AdoptSchematicArt() is not { Ready: true } content) return;
        if (_schematicArtLook is null)
        {
            var art = content.Art;
            var frames = AreaSchematicWindowLayout.ArtNumbers.Concat(art.Keys).Distinct()
                .Where(number => art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0
                    && number is not (AreaSchematicWindowLayout.NameFontArt or AreaSchematicWindowLayout.DescriptionFontArt or AreaSchematicWindowLayout.GlyphFontArt
                        or AreaSchematicWindowLayout.SlotGlyphFontArt))
                .ToDictionary(number => number, number => art[number].Frames.Select(frame => (Texture2D)InterfaceHd.Texture(frame, premultipliedAlpha: true)).ToArray());
            AreaArtFontFace Font(int number) => new(art[number].Frames);
            _schematicArtLook = (frames, new(Font(AreaSchematicWindowLayout.NameFontArt), Font(AreaSchematicWindowLayout.DescriptionFontArt),
                Font(AreaSchematicWindowLayout.GlyphFontArt), Font(AreaSchematicWindowLayout.SlotGlyphFontArt)));
        }
        if (!ReferenceEquals(panel, _schematicArtPanel))
        {
            _schematicArtPanel = panel;
            panel.ArtView.SetArt(_schematicArtLook.Value.Frames, _schematicArtLook.Value.Fonts);
        }
        SetSchematicTexts(panel);
    }

    /// <summary>The window's hooks into the app, set once when its panel is made (the panel outlives a module change).</summary>
    private void WireSchematicArt(AreaSchematicsPanel panel)
    {
        panel.ArtView.SlotPicture = SchematicSlotPicture;
        panel.ArtView.SlotEntry = SchematicSlotEntry;
        panel.HolderView = InventoryHolderView;
        panel.SoundRequested += id => PlayInterfaceSound(id, "schematics button");
        panel.HoverEntryRequested += SchematicHoverEntry;
    }

    /// <summary>The list tabs' and the disciplines' hover texts, from the data once they have loaded.</summary>
    private void SetSchematicTexts(AreaSchematicsPanel panel)
    {
        if (_schematicData?.Data is not { } data || ReferenceEquals(_schematicTextsFor, data)) return;
        _schematicTextsFor = data;
        string Text(int message) => AreaSchematics.CatalogText(data, message, AreaHoverIndex.TextProfile);
        var disciplines = Enumerable.Range(0, AreaSchematics.Disciplines).Select(discipline =>
            string.Join("\n", new[] { data.Texts.DisciplineNames[discipline], data.Texts.DisciplineDescriptions[discipline] }
                .Where(line => !string.IsNullOrEmpty(line)).Select(line => AreaSchematics.DecodeText(line, AreaHoverIndex.TextProfile)))).ToArray();
        panel.ArtView.SetTexts(new(Text(SchematicListTabMessage), Text(SchematicListTabMessage + 1), disciplines));
    }

    // schematic_text.mes 4 (the learned tab's hover) and 5 (the found tab's), ledger SW4.
    private const int SchematicListTabMessage = 4;

    /// <summary>A component slot's item picture: its prototype's inventory art (field 0x5D), at any size, read once per prototype; null when it does not read.</summary>
    private Texture2D? SchematicSlotPicture(AreaSchematicSlot slot)
    {
        if (_schematicPictures.TryGetValue(slot.Prototype, out var known)) return known;
        Texture2D? texture = null;
        if (_schematicData?.Data?.Kind(slot.Prototype) is { } kind && InventoryItemIcons() is { } icons && icons.InventoryFrame(kind, null).Frame is { } frame)
            texture = HotkeyTexture(frame);
        _schematicPictures[slot.Prototype] = texture;
        return texture;
    }

    /// <summary>The pointer over a slot shows the item's name (PROJECT: the original's item window text 0x57CCF0 is not read for this window yet).</summary>
    private AreaMessageEntry? SchematicSlotEntry(AreaSchematicSlot slot) =>
        AreaMessageEntry.Text(AreaMessageKind.Hover, AreaSchematics.Name(slot.Description, _hoverNames?.Catalog));

    private void SchematicHoverEntry(AreaMessageEntry? entry)
    {
        if (entry is not null) PostMessage(entry, "schematics hover");
        else { _messageLog.ClearHover(); _gameBarPanel?.MessageLog.Render(); }
    }

    /// <summary>The window's message: in the original look the text bar shows it (type 4, ledger SW4), in the diagnostic layout the window does.</summary>
    private void ShowSchematicMessage(string? text)
    {
        _schematicMessage = text;
        if (_schematicsPanel is { ArtMode: true } && !string.IsNullOrEmpty(text)) PostMessage(AreaMessageEntry.Text(AreaMessageKind.Message, text), "schematics");
    }
}
