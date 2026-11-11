using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-074: the logbook in the original's look (docs/ui/original-windows.md, ledger LB14-LB17). The book's art is read once a module, the
// fonts come with the message window's (TextFonts, ARC-DEV-156). Without a game folder (the synthetic self-tests) or when the art or the fonts
// did not read the logbook keeps its diagnostic list; a self-test injects a synthetic art set instead.
public partial class TerrainViewerApp
{
    private AreaLogbookArtContent? _logbookArtContent;
    private string? _logbookArtModule;
    private IReadOnlyDictionary<int, Texture2D[]>? _logbookArtTextures;
    private (IReadOnlyDictionary<int, Texture2D[]> Art, AreaTextFonts Fonts)? _logbookArtForSelfTest;

    /// <summary>The book's art and fonts of the current module, read on first use; null when either did not read.</summary>
    private (IReadOnlyDictionary<int, Texture2D[]> Art, AreaTextFonts Fonts)? LogbookArtLook()
    {
        if (_logbookArtForSelfTest is { } synthetic) return synthetic;
        var module = _arguments?.ModulePath;
        if (_logbookArtContent is null || module != _logbookArtModule)
        {
            _logbookArtModule = module;
            _logbookArtTextures = null;
            _logbookArtContent = SelfTestBootstrap || module is null ? AreaLogbookArtContent.Empty("self-test: no game folder")
                : WarmedWindowArt(module, slots => slots.Logbook) ?? AreaLogbookArtContent.Load(module);
            if (_logbookArtContent.ArtReady)
            {
                // ARC-UI-125: the view's canvas is premultiplied (art text, AreaArtTextDraw.Prepare); a straight-alpha HD frame would add the RGB of its
                // transparent texels (Book_Quests is all rim and hole) to the page under it, as ARC-UI-114 fixed for the sheet.
                _logbookArtTextures = LogbookLookLoads(_logbookArtContent)
                    .ToDictionary(load => load.Number, load => load.Frames.Select(frame => (Texture2D)InterfaceHd.Texture(frame, premultipliedAlpha: true)).ToArray());
                GD.Print($"ARC-UI-074 logbook art ready {_logbookArtContent.Detail}");
            }
            else GD.Print($"ARC-UI-074 logbook art not ready: {_logbookArtContent.Detail}");
        }
        // A font with no line height cannot lay a spread out: the list stays.
        return _logbookArtTextures is { } art && TextFonts() is { TextLineHeight: > 0 } fonts ? (art, fonts) : null;
    }

    /// <summary>The arts the logbook look turns into textures: those that read, every frame.</summary>
    private static IEnumerable<AreaInterfaceArtLoad> LogbookLookLoads(AreaLogbookArtContent content) =>
        content.Art.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0);
}
