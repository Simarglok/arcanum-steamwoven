using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>Which art font and colour a browser text is drawn with (MW14, MW20).</summary>
public sealed record AreaPremadeBrowserFonts(AreaArtFont Text, IReadOnlyDictionary<int, Texture2D> TextGlyphs, AreaArtFont Name,
    IReadOnlyDictionary<int, Texture2D> NameGlyphs);

/// <summary>
/// ARC-UI-066: the pre-made character browser (window 13) in the original's look (<see cref="AreaPremadeBrowserArtLayout"/>, ledger
/// MW17–MW21): the strips and the background over the 800 × 600 screen at the bars' whole factor, centred in the window; the browsed
/// hero's large portrait, its name, gender, race and background name centred in their fields, its story (or its race's text) and
/// its fifteen stats, each field filled black first; the four art buttons with their idle, down and lit frames (GB4) and push
/// behaviour (GB3: down on the press, acting on a release over it), 3000 on the press and 3001 on the release (GB7). PROJECT: a glyph's
/// grey levels scale the font's colour (as MW5) and a story's lines below its field are not drawn (the text draw's clip is not read).
/// </summary>
public partial class AreaPremadeBrowserArtView : Control
{
    private static readonly AreaPremadeBrowserButton[] AllButtons = Enum.GetValues<AreaPremadeBrowserButton>();
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private AreaPremadeBrowserFonts? _fonts;
    private AreaPremadeBrowserView? _view;
    private Texture2D? _portrait;
    private AreaPremadeBrowserButton? _hovered, _pressed;

    /// <summary>A button released over itself after its press.</summary>
    public event Action<AreaPremadeBrowserButton>? ButtonPressed;
    /// <summary>A button's interface sound (3000 the press, 3001 the release).</summary>
    public event Action<int>? SoundRequested;

    public bool ArtSet => _fonts is not null;
    internal AreaPremadeBrowserView? ViewForSelfTest => Visible ? _view : null;
    internal bool PortraitForSelfTest => Visible && _portrait is not null;
    internal int FrameForSelfTest(AreaPremadeBrowserButton button) => Frame(button);
    /// <summary>The window position of a screen point of the 800 × 600 screen.</summary>
    internal Vector2 ScreenPointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);

    public override void _Ready()
    {
        Name = "PremadeBrowserArt";
        Size = new Vector2(AreaPremadeBrowserArtLayout.ScreenWidth, AreaPremadeBrowserArtLayout.ScreenHeight);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
    }

    /// <summary>The art's frames by number and the two fonts with their glyph frames (by frame index).</summary>
    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaPremadeBrowserFonts fonts)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
    }

    /// <summary>Shows <paramref name="view"/> with its portrait (null when it did not read: the field stays black).</summary>
    public void Render(AreaPremadeBrowserView view, Texture2D? portrait)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_fonts is null) throw new InvalidOperationException("ARC-UI-066: the browser art is not set");
        if (!Visible) (_hovered, _pressed) = (null, null);
        (_view, _portrait) = (view, portrait);
        Visible = true;
        Place();
        QueueRedraw();
    }

    public void CloseView()
    {
        Visible = false;
        (_hovered, _pressed, _view, _portrait) = (null, null, null, null);
    }

    /// <summary>The screen at the bars' whole factor, centred in the viewport.</summary>
    public void Place()
    {
        var size = GetViewportRect().Size;
        var factor = AreaGameBarPanel.ScreenFactor(size);
        Scale = new Vector2(factor, factor);
        Position = new Vector2(AreaGameBarPanel.ScreenLeft(size, factor), MathF.Round((size.Y - AreaPremadeBrowserArtLayout.ScreenHeight * factor) / 2));
    }

    private Texture2D? Art(int number, int frame = 0) => _art.TryGetValue(number, out var frames) && frame < frames.Length ? frames[frame] : null;

    private int Frame(AreaPremadeBrowserButton button)
    {
        var frames = _art.TryGetValue(AreaPremadeBrowserArtLayout.Button(button).Art, out var art) ? art.Length : 1;
        // GB3/GB4: a held button shows down over it and idle away from it; another button is not lit while one is held.
        var look = _pressed is { } held ? held == button && _hovered == button ? AreaBarButtonLook.Down : AreaBarButtonLook.Idle
            : _hovered == button ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle;
        return AreaGameBarLayout.Frame(look, Math.Max(1, frames));
    }

    private void Fill((int X, int Y, int Width, int Height) field) => DrawRect(new Rect2(field.X, field.Y, field.Width, field.Height), Colors.Black);

    private void Glyphs(IReadOnlyDictionary<int, Texture2D> glyphs, IEnumerable<(int X, AreaArtGlyph Glyph)> placed, int x, int y, (byte R, byte G, byte B) colour)
    {
        // PROJECT: a glyph's grey levels scale the font's colour (the text blit's blend is not read, F0; as the menu's labels).
        var tint = Color.Color8(colour.R, colour.G, colour.B);
        foreach (var (gx, glyph) in placed)
            if (glyphs.TryGetValue(glyph.Frame, out var texture)) DrawTextureRect(texture, new Rect2(x + gx, y, glyph.Width, glyph.Height), false, tint);
    }

    /// <summary>A one-line text centred in its field (0x542DF0), the field filled black first.</summary>
    private void Centred((int X, int Y, int Width, int Height) field, string text, AreaArtFont font, IReadOnlyDictionary<int, Texture2D> glyphs,
        (byte R, byte G, byte B) colour)
    {
        Fill(field);
        Glyphs(glyphs, font.Layout(text), AreaPremadeBrowserArtLayout.Centred(field, font.Measure(text).Width), field.Y, colour);
    }

    /// <summary>A text from its field's top-left, wrapped at its width; lines below the field are not drawn (PROJECT).</summary>
    private void Lines((int X, int Y, int Width, int Height) field, string text, AreaArtFont font, IReadOnlyDictionary<int, Texture2D> glyphs,
        (byte R, byte G, byte B) colour)
    {
        var height = font.Measure("A").Height;
        var y = field.Y;
        foreach (var line in AreaPremadeBrowserArtLayout.Wrap(font, text, field.Width))
        {
            if (height <= 0 || y + height > field.Y + field.Height) break;
            Glyphs(glyphs, line, field.X, y, colour);
            y += height;
        }
    }

    public override void _Draw()
    {
        if (_fonts is not { } fonts || _view is not { } view) return;
        DrawRect(new Rect2(0, 0, AreaPremadeBrowserArtLayout.ScreenWidth, AreaPremadeBrowserArtLayout.ScreenHeight), Colors.Black);
        if (Art(AreaPremadeBrowserArtLayout.TopStripArt) is { } top) DrawTextureRect(top, new Rect2(0, 0, top.GetWidth(), top.GetHeight()), false);
        if (Art(AreaPremadeBrowserArtLayout.BottomStripArt) is { } bottom)
            foreach (var (x, y, width, height) in AreaSaveListArtLayout.BottomStripParts)
                DrawTextureRectRegion(bottom, new Rect2(x, y, width, height), new Rect2(x, 0, width, height));
        if (Art(AreaPremadeBrowserArtLayout.BackgroundArt) is { } background)
            DrawTextureRect(background, new Rect2(0, AreaPremadeBrowserArtLayout.WindowTop, background.GetWidth(), background.GetHeight()), false);
        // The character draw 0x5448E0: the portrait's field, then the large portrait at its corner (0x4CE640).
        Fill(AreaPremadeBrowserArtLayout.PortraitFill);
        if (_portrait is not null)
            DrawTextureRect(_portrait, new Rect2(AreaPremadeBrowserArtLayout.Portrait.X, AreaPremadeBrowserArtLayout.Portrait.Y, _portrait.GetWidth(), _portrait.GetHeight()), false);
        var (white, red) = (AreaPremadeBrowserArtLayout.White, AreaPremadeBrowserArtLayout.Red);
        Centred(AreaPremadeBrowserArtLayout.Gender, view.Gender, fonts.Text, fonts.TextGlyphs, red);
        Centred(AreaPremadeBrowserArtLayout.Race, view.Race, fonts.Text, fonts.TextGlyphs, red);
        Centred(AreaPremadeBrowserArtLayout.Background, view.BackgroundName, fonts.Text, fonts.TextGlyphs, red);
        Fill(AreaPremadeBrowserArtLayout.Description);
        if (view.DescriptionTitle is { } title)
        {
            Centred(AreaPremadeBrowserArtLayout.RaceTitle, title, fonts.Name, fonts.NameGlyphs, white);
            Lines(AreaPremadeBrowserArtLayout.RaceText, view.Description, fonts.Text, fonts.TextGlyphs, white);
        }
        else Lines(AreaPremadeBrowserArtLayout.Description, view.Description, fonts.Text, fonts.TextGlyphs, white);
        Centred(AreaPremadeBrowserArtLayout.StatsTitle, view.StatsTitle, fonts.Name, fonts.NameGlyphs, white);
        for (var slot = 0; slot < view.Stats.Count && slot < AreaPremadeBrowserArtLayout.StatFields.Count; slot++)
        {
            var field = AreaPremadeBrowserArtLayout.StatFields[slot];
            Fill(field);
            // 0x5418A0 from the field's corner; a stat's line is taller than its 15-row field, so it is not clipped to it.
            Glyphs(fonts.TextGlyphs, fonts.Text.Layout(view.Stats[slot]), field.X, field.Y, white);
        }
        // The redraw 0x5458D0: the player name over the character draw.
        Centred(AreaPremadeBrowserArtLayout.Name, view.Name, fonts.Name, fonts.NameGlyphs, red);
        foreach (var button in AllButtons)
        {
            var (art, x, y) = AreaPremadeBrowserArtLayout.Button(button);
            if (Art(art, Frame(button)) is { } texture) DrawTextureRect(texture, new Rect2(x, y, texture.GetWidth(), texture.GetHeight()), false);
        }
    }

    private AreaPremadeBrowserButton? ButtonAt(Vector2 at)
    {
        foreach (var button in AllButtons)
        {
            var (art, x, y) = AreaPremadeBrowserArtLayout.Button(button);
            if (Art(art) is { } texture && AreaPremadeBrowserArtLayout.Contains((x, y, texture.GetWidth(), texture.GetHeight()), at.X, at.Y)) return button;
        }
        return null;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || _hovered is null) return;
        _hovered = null;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                var over = ButtonAt(motion.Position);
                if (over == _hovered) break;
                _hovered = over;
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var at = ButtonAt(button.Position);
                // The press lights what is under it even when the pointer did not move since the opening (GB3: down over it).
                _hovered = at;
                if (button.Pressed)
                {
                    _pressed = at;
                    if (at is not null) SoundRequested?.Invoke(AreaGameBarLayout.PressSound);
                }
                else if (_pressed is { } held)
                {
                    _pressed = null;
                    // GB3: a release elsewhere only lets the button go, silently.
                    if (at == held) { SoundRequested?.Invoke(AreaGameBarLayout.ReleaseSound); ButtonPressed?.Invoke(held); }
                }
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }
}
