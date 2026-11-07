using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-075: the character sheet's original look (docs/ui/original-windows.md; ledger CHW1–CHW9). The window's interface art and texts are read
// once a module; without a game folder (the synthetic self-tests) or when an art does not read the sheet keeps its diagnostic look.
public partial class TerrainViewerApp
{
    private AreaCharacterSheetArtContent? _sheetArtContent;
    private string? _sheetArtModule;
    private AreaCharacterSheetPanel? _sheetArtPanel;

    /// <summary>Gives the sheet panel the current module's art, read on first use; nothing when it did not read.</summary>
    private void AttachSheetArt(AreaCharacterSheetPanel panel)
    {
        var module = _arguments?.ModulePath;
        if (_sheetArtContent is not null && module == _sheetArtModule && ReferenceEquals(panel, _sheetArtPanel)) return;
        (_sheetArtModule, _sheetArtPanel) = (module, panel);
        var started = Time.GetTicksMsec();
        _sheetArtContent = SelfTestBootstrap || module is null ? AreaCharacterSheetArtContent.Empty("self-test: no game folder") : AreaCharacterSheetArtContent.Load(module);
        if (!_sheetArtContent.ArtReady)
        {
            GD.Print($"ARC-UI-075 sheet art not ready: {_sheetArtContent.Detail}");
            return;
        }
        var art = _sheetArtContent.Art;
        var frames = AreaCharacterSheetArtLayout.AllArtNumbers.Where(number => art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready
                && load.Frames.Count > 0 && number is not (AreaCharacterSheetArtLayout.LabelFontArt or AreaCharacterSheetArtLayout.FlareFontArt or AreaCharacterSheetArtLayout.CloisterFontArt
                    or AreaCharacterSheetPagesLayout.IconsFontArt or AreaCharacterSheetPagesLayout.NickFontArt or AreaCharacterSheetPagesLayout.GarmondFontArt))
            .ToDictionary(number => number, number => art[number].Frames.Select(frame => (Texture2D)HotkeyTexture(frame)).ToArray());
        var pages = _sheetArtContent.PagesReady;
        AreaArtFontFace? PageFont(int number) => pages ? new(art[number].Frames) : null;
        var fonts = new AreaCharacterSheetFonts(new(art[AreaCharacterSheetArtLayout.LabelFontArt].Frames), new(art[AreaCharacterSheetArtLayout.FlareFontArt].Frames),
            new(art[AreaCharacterSheetArtLayout.CloisterFontArt].Frames), PageFont(AreaCharacterSheetPagesLayout.IconsFontArt),
            PageFont(AreaCharacterSheetPagesLayout.NickFontArt), PageFont(AreaCharacterSheetPagesLayout.GarmondFontArt));
        var hots = art[AreaCharacterSheetArtLayout.AlignmentArt].Frames.Select(frame => (frame.HotX, frame.HotY)).ToArray();
        panel.SetArt(frames, fonts, _sheetArtContent.Texts, hots, pages);
        GD.Print($"ARC-UI-075 sheet art ready {_sheetArtContent.Detail}; ARC-UI-076 pages art ready={pages}; ms={Time.GetTicksMsec() - started}");
    }

    /// <summary>The hero's portrait for the sheet's slot (the stored portrait's small image), or null for none or one that did not read.</summary>
    private Texture2D? SheetPortrait()
    {
        if (_areaSession?.QueryHero() is not { Portrait: > 0 } hero) return null;
        LoadPortraits([hero.Portrait]);
        return PortraitTexture(hero.Portrait);
    }

    /// <summary>The pointer over a part of the sheet writes that part's text to the message window (the exe's 0x550750, type 6); leaving takes it
    /// away. A refusal is a message (type 4).</summary>
    private void SheetHoverEntry(AreaMessageEntry? entry)
    {
        if (entry is not null) PostMessage(entry, "sheet hover");
        else { _messageLog.ClearHover(); _gameBarPanel?.MessageLog.Render(); }
    }

    private void SheetHover(string? text)
    {
        if (text is not null) PostMessage(AreaMessageEntry.Text(AreaMessageKind.Hover, text), "sheet hover");
        else { _messageLog.ClearHover(); _gameBarPanel?.MessageLog.Render(); }
    }
}
