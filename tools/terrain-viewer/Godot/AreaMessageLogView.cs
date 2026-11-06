using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-023: the original's message window in the bottom strip of the interface bars (ledger ML1–ML8), drawn in the strip's own
/// 800×600-screen coordinates from <see cref="AreaMessageLog"/>: in the message mode the mode's background art, and for the shown entry
/// a black text area with the entry's icon and texts where <see cref="AreaMessageLayout"/> puts them; a text entry whose last text does
/// not fit is drawn again in its wide layout without the icon, cut until it fits. The upper half of the window shows an older entry and
/// the lower half a newer one (invisible areas, no sound, acting on a release over the area pressed). ARC-DEV-156: the texts are drawn
/// in the original's font art (title font 27, text font 229, ML6) with its blend, broken and cut as the text draw 0x550A10 does; without
/// the fonts (<see cref="SetFonts"/> null: no game folder) they use the viewer's font in the original's colours. ARC-UI-035: the hints and the item window draw like the entries
/// (a text flagged right-aligned at its rectangle's right edge, an item name in its colour), and while a pane owns the window a hover's
/// name is drawn centred on a black fill of the pane mode's text area (0x5509C0).
/// </summary>
public partial class AreaMessageLogView : Control
{
    private const int TitleFontSize = 13, TextFontSize = 12;
    private AreaMessageLog _log = new();
    private IReadOnlyDictionary<int, ImageTexture[]> _art = new Dictionary<int, ImageTexture[]>();
    private TextureRect _background = null!, _icon = null!;
    private ColorRect _clear = null!, _paneClear = null!;
    private Label _paneName = null!;
    private readonly List<Label> _labels = [];
    private Control _older = null!, _newer = null!;
    private Control? _pressed;
    private bool _wide;
    // ARC-DEV-156: the original's text fonts and the control that draws the placed texts with them (null fonts: the labels draw).
    private AreaTextFonts? _fonts;
    private AreaArtTextBlock _text = null!;
    private readonly List<(string Text, AreaMessageColour Colour, bool Drawn)> _artTexts = [];
    private bool _paneShown;
    private string _paneText = "";

    /// <summary>The texts drawn now (a text with at least one line on screen), joined by line breaks ("" when nothing is drawn).</summary>
    internal string ShownTextForSelfTest => Visible && _clear.Visible ? string.Join("\n", DrawnTexts().Select(text => text.Text)) : "";
    /// <summary>How many of the drawing's texts have at least one line on screen.</summary>
    internal int DrawnTextsForSelfTest => Visible && _clear.Visible ? DrawnTexts().Count() : 0;
    /// <summary>Whether the original's font art draws the texts (false: the viewer's font).</summary>
    internal bool ArtFontsForSelfTest => _fonts is not null;
    private IEnumerable<(string Text, AreaMessageColour Colour, bool Drawn)> DrawnTexts() => _fonts is not null ? _artTexts.Where(text => text.Drawn)
        : _labels.Where(Drawn).Select(label => (label.Text, AreaMessageColour.White, true));
    private static bool Drawn(Label label) => label.Visible && label.Text.Length > 0 && label.GetVisibleLineCount() > 0;
    /// <summary>The hover text drawn now ("" when the window shows no hover text).</summary>
    internal string HoverTextForSelfTest => _log.Shown is { Kind: AreaMessageKind.Hover } && Visible && _clear.Visible ? ShownTextForSelfTest : "";
    /// <summary>ARC-UI-035: the kind of the drawing shown now (null when nothing is drawn), the colour of its first text, the icon drawn
    /// and the name a hover wrote into a pane's text area ("" none).</summary>
    internal AreaMessageKind? ShownKindForSelfTest => Visible && _clear.Visible ? _log.Shown?.Kind : null;
    internal Color FirstTextColourForSelfTest => _fonts is not null ? (_artTexts.Count > 0 ? Colour(_artTexts[0].Colour) : Colors.White) : _labels[0].Modulate;
    internal int? IconForSelfTest => _icon.Visible && _icon.Texture is not null ? _iconNumber : null;
    private int? _iconNumber;
    internal string PaneNameForSelfTest => Visible && _paneShown ? _paneText : "";
    internal bool IconShownForSelfTest => _icon.Visible && _icon.Texture is not null;
    internal bool BackgroundShownForSelfTest => _background.Visible && _background.Texture is not null;
    internal bool WideForSelfTest => _wide;
    internal Vector2 OlderCentreForSelfTest => _older.GetGlobalRect().GetCenter();
    internal Vector2 NewerCentreForSelfTest => _newer.GetGlobalRect().GetCenter();

    public override void _Ready()
    {
        Name = "MessageLog";
        MouseFilter = MouseFilterEnum.Ignore;
        _background = new TextureRect { Name = "Background", Position = Local(AreaMessageLayout.BackgroundX, AreaMessageLayout.BackgroundY), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_background);
        var (x, y, w, h) = AreaMessageLayout.Window;
        _clear = new ColorRect { Name = "Clear", Color = Colors.Black, Position = Local(x, y), Size = new(w, h), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_clear);
        _icon = new TextureRect { Name = "Icon", Position = Local(AreaMessageLayout.IconX, AreaMessageLayout.IconY), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_icon);
        for (var i = 0; i < 4; i++)
        {
            var label = new Label
            {
                MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart, ClipText = true,
                VerticalAlignment = VerticalAlignment.Top, Visible = false, Modulate = Colors.White,
            };
            label.AddThemeConstantOverride("line_spacing", 0);
            _labels.Add(label);
            AddChild(label);
        }
        _paneClear = new ColorRect { Name = "PaneClear", Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_paneClear);
        _paneName = new Label
        {
            Name = "PaneName", MouseFilter = MouseFilterEnum.Ignore, ClipText = true, Visible = false, Modulate = Colors.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _paneName.AddThemeFontSizeOverride("font_size", TextFontSize);
        AddChild(_paneName);
        // Over the clears, the icon and the labels: the texts the original's font art draws.
        _text = new AreaArtTextBlock { Name = "ArtText" };
        AddChild(_text);
        _older = Area("OlderArea", AreaMessageLayout.OlderArea);
        _newer = Area("NewerArea", AreaMessageLayout.NewerArea);
        Render();
    }

    /// <summary>The bar strip is the bottom one (origin 0, 441): screen coordinates become the strip's own.</summary>
    private static Vector2 Local(int x, int y) => new(x, y - AreaGameBarLayout.BottomTop);

    private Control Area(string name, (int X, int Y, int Width, int Height) rect)
    {
        var area = new Control { Name = name, Position = Local(rect.X, rect.Y), Size = new(rect.Width, rect.Height), MouseFilter = MouseFilterEnum.Stop };
        area.GuiInput += input => AreaInput(area, input);
        AddChild(area);
        return area;
    }

    /// <summary>The log the window draws and the interface art by number (each art's frames).</summary>
    public void Bind(AreaMessageLog log, IReadOnlyDictionary<int, ImageTexture[]> art)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _art = art ?? throw new ArgumentNullException(nameof(art));
        if (IsNodeReady()) Render();
    }

    /// <summary>ARC-DEV-156: gives the window the original's text fonts (null: the viewer's own font) and draws it again.</summary>
    public void SetFonts(AreaTextFonts? fonts)
    {
        if (ReferenceEquals(_fonts, fonts)) return;
        _fonts = fonts;
        if (IsNodeReady()) Render();
    }

    private void AreaInput(Control area, InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
        area.AcceptEvent();
        if (click.Pressed) { _pressed = area; return; }
        // A push button without art or sound: it acts on the release over the area that took the press.
        var over = ReferenceEquals(_pressed, area) && new Rect2(Vector2.Zero, area.Size).HasPoint(click.Position);
        _pressed = null;
        if (!over) return;
        if (ReferenceEquals(area, _older)) _log.Older(); else _log.Newer();
        Render();
    }

    private ImageTexture? Art(int number) => _art.TryGetValue(number, out var frames) && frames.Length > 0 ? frames[0] : null;

    /// <summary>Redraws the window from the log.</summary>
    public void Render()
    {
        // The scroll areas exist in the message mode only (the mode's build shows them, 0x551210).
        _older.Visible = _newer.Visible = !_log.PaneMode;
        _background.Visible = !_log.PaneMode;
        _background.Texture = Art(AreaMessageLayout.BackgroundArt);
        _wide = false;
        // A hover's name in a pane mode: the mode's text area cleared to black and the name centred in it (0x5509C0, flags 0x4 | 0x8).
        _paneShown = _log.PaneMode && _log.PaneName is not null;
        _paneClear.Visible = _paneShown;
        _paneName.Visible = _paneShown && _fonts is null;
        var items = new List<AreaArtTextItem>();
        _artTexts.Clear();
        _paneText = "";
        if (_log.PaneName is { } pane && _log.PaneMode)
        {
            var (px, py, pw, ph) = AreaMessageLayout.PaneText(pane.Mode);
            _paneClear.Position = _paneName.Position = Local(px, py);
            _paneClear.Size = _paneName.Size = new(pw, ph);
            _paneName.Text = _paneText = pane.Name;
            if (_fonts is { } fonts)
            {
                var placed = AreaTextFontLayout.Centred(fonts.Text.Font, fonts.TextLineHeight, (px, py, pw, ph), pane.Name);
                var at = Local(px, py);
                items.Add(new(fonts.Text, fonts.TextLineHeight, placed.Lines, (int)at.X, (int)at.Y, ColourBytes(AreaMessageColour.White)));
            }
        }
        if (_log.PaneMode || _log.Shown is not { } entry)
        {
            _clear.Visible = _icon.Visible = false;
            foreach (var label in _labels) label.Visible = false;
            _text.Set(items);
            return;
        }
        _clear.Visible = true;
        var draw = AreaMessageLayout.Draw(entry);
        if (!Apply(draw, items) && draw.Widens) { _wide = true; items.Clear(); Apply(AreaMessageLayout.Draw(entry, wide: true), items); }
        _text.Set(items);
    }

    /// <summary>Draws one layout; false when its last text did not fit.</summary>
    private bool Apply(AreaMessageDraw draw, List<AreaArtTextItem> items)
    {
        _icon.Texture = draw.Icon is { } icon ? Art(icon) : null;
        _iconNumber = draw.Icon;
        _icon.Visible = _icon.Texture is not null;
        if (_fonts is { } fonts) return ApplyArt(draw, fonts, items);
        var fits = true;
        for (var i = 0; i < _labels.Count; i++)
        {
            var label = _labels[i];
            if (i >= draw.Texts.Count) { label.Visible = false; continue; }
            var text = draw.Texts[i];
            label.AddThemeFontSizeOverride("font_size", text.Font == AreaMessageFont.Title ? TitleFontSize : TextFontSize);
            label.Modulate = Colour(text.Colour);
            label.HorizontalAlignment = text.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            label.Position = Local(text.Rect.X, text.Rect.Y);
            label.Text = text.Text;
            // A rectangle at least one of the viewer's lines high: Godot draws no line taller than the label (the original's fonts
            // are smaller than the stand-in's).
            label.Size = new(text.Rect.Width, Math.Max(text.Rect.Height, label.GetLineHeight()));
            label.Visible = true;
            var shown = text.Text;
            // The wide layout cuts characters from the end until the text fits (flag 0x20 of 0x550A10).
            while (text.Truncate && shown.Length > 0 && !Fits(label)) { shown = shown[..^1]; label.Text = shown; }
            fits = Fits(label);
        }
        return fits;
    }

    /// <summary>ARC-DEV-156: one layout in the original's font art; false when its last text did not fit its rectangle. The wide layout
    /// cuts characters from the end until the text fits (flag 0x20 of 0x550A10).</summary>
    private bool ApplyArt(AreaMessageDraw draw, AreaTextFonts fonts, List<AreaArtTextItem> items)
    {
        foreach (var label in _labels) label.Visible = false;
        _artTexts.Clear();
        var fits = true;
        foreach (var text in draw.Texts)
        {
            var (face, lineHeight) = (fonts.Face(text.Font), fonts.LineHeight(text.Font));
            var shown = text.Text;
            var placed = AreaTextFontLayout.Place(face.Font, lineHeight, text.Rect, shown, text.Right);
            while (text.Truncate && shown.Length > 0 && !placed.Fits)
            {
                shown = shown[..^1];
                placed = AreaTextFontLayout.Place(face.Font, lineHeight, text.Rect, shown, text.Right);
            }
            fits = placed.Fits;
            var at = Local(text.Rect.X, text.Rect.Y);
            items.Add(new(face, lineHeight, placed.Lines, (int)at.X, (int)at.Y, ColourBytes(text.Colour)));
            _artTexts.Add((shown, text.Colour, shown.Length > 0 && placed.Lines.Count > 0));
        }
        return fits;
    }

    private static (byte R, byte G, byte B) ColourBytes(AreaMessageColour colour) => colour switch
    {
        AreaMessageColour.Red => (255, 0, 0),
        AreaMessageColour.Magick => (100, 100, 255),
        AreaMessageColour.Hexed => (255, 114, 0),
        _ => (255, 255, 255),
    };

    // The fonts' colours (ML6, HK22): white, red (255, 0, 0), a magickal item's blue (100, 100, 255), a hexed item's orange (255, 114, 0).
    private static Color Colour(AreaMessageColour colour) => colour switch
    {
        AreaMessageColour.Red => new Color(1, 0, 0),
        AreaMessageColour.Magick => Color.Color8(100, 100, 255),
        AreaMessageColour.Hexed => Color.Color8(255, 114, 0),
        _ => Colors.White,
    };

    // The rectangle holds as many lines as its height takes, at least one (the text library draws a line while one fits, 0x535570).
    private static bool Fits(Label label) => label.GetLineCount() <= Math.Max(1, label.GetVisibleLineCount());
}
