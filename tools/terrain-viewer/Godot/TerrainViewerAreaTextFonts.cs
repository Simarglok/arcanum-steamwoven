using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-156 (B27, B28): the original's text fonts (art 27 and 229; ARC-UI-124 art 230, the combat panel's number) for the message window, its hover text, the floating tooltip and
// the floaters, read once a module. Without a game folder (a self-test) or when the art did not read they are null and every user draws
// with the viewer's own font, as before.
public partial class TerrainViewerApp
{
    private AreaTextFontContent? _textFontContent;
    private string? _textFontModule;
    private AreaTextFonts? _textFonts;
    // ARC-UI-2968: a self-test with no game folder gives the users of the fonts a synthetic face for the length of one check.
    private AreaTextFonts? _textFontsForSelfTest;
    internal AreaTextFonts? TextFontsForSelfTest { set => _textFontsForSelfTest = value; }

    /// <summary>The text fonts of the shown module, null without a game folder or when their art did not read.</summary>
    internal AreaTextFonts? TextFonts()
    {
        if (_textFontsForSelfTest is { } synthetic) return synthetic;
        var module = _arguments?.ModulePath;
        if (SelfTestBootstrap || module is null) return null;
        if (_textFontContent is null || module != _textFontModule)
        {
            _textFontModule = module; _textFonts = null;
            _textFontContent = AreaTextFontContent.Load(module);
            GD.Print("ARC-DEV-156 text fonts " + _textFontContent.Detail);
            if (_textFontContent.ArtReady)
                _textFonts = new(new AreaArtFontFace(_textFontContent.Art[AreaTextFontLayout.TitleArt].Frames),
                    new AreaArtFontFace(_textFontContent.Art[AreaTextFontLayout.TextArt].Frames),
                    _textFontContent.NumberArtReady ? new AreaArtFontFace(_textFontContent.Art[AreaTextFontLayout.NumberArt].Frames) : null)
                {
                    Roller = _textFontContent.RollerArtReady ? new AreaArtFontFace(_textFontContent.Art[AreaTextFontLayout.RollerArt].Frames) : null,
                    Bar = _textFontContent.BarArtReady ? new AreaArtFontFace(_textFontContent.Art[AreaTextFontLayout.BarArt].Frames) : null,
                    Rating = _textFontContent.RatingArtReady ? new AreaArtFontFace(_textFontContent.Art[AreaTextFontLayout.RatingArt].Frames) : null,
                };
            if (_textFonts is { } made) GD.Print($"ARC-DEV-156 text fonts line heights title={made.TitleLineHeight} text={made.TextLineHeight}");
        }
        return _textFonts;
    }
}
