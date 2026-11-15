using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-UI-2305: what the order line shows besides its text: the background art (art 291, null: it did not read) and the text face (the message window's).</summary>
public sealed record AreaOrderLineLook(Texture2D? Background, AreaArtFontFace Font);

/// <summary>
/// ARC-UI-2305: the original's typed order line (bar mode 7, ledger BM0, BM5) drawn in the bottom strip over the message window: art 291 at the place
/// every strip pane uses, and the text rectangle (220, 503, 350 × 82) filled black with the typed text in the entry's colour from its left edge and the
/// caret mark "*" after the end of the text (0x5521B0; it is drawn after the text whatever the caret position, and does not blink). The text is
/// clipped to the rectangle: the original neither scrolls nor wraps it (mode 7 sets no scrolling flag; the clip of a long text is not read, F0).
/// </summary>
public partial class AreaOrderLineView : Control
{
    private TextureRect _background = null!;
    private TextLayer _text = null!;
    private AreaOrderLineLook? _look;

    internal Texture2D? BackgroundForSelfTest => _background.Visible ? _background.Texture : null;
    internal string TextForSelfTest => _text.Shown;

    public override void _Ready()
    {
        Name = "OrderLine";
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(AreaGameBarLayout.ScreenWidth, AreaGameBarLayout.BottomHeight);
        // The background takes the clicks over the line, so the message window under it does not scroll its entries.
        _background = new TextureRect { Name = "Background", Position = Local(OrderLine.BackgroundX, OrderLine.BackgroundY), MouseFilter = MouseFilterEnum.Stop };
        AddChild(_background);
        _text = new TextLayer { Name = "Text", MouseFilter = MouseFilterEnum.Ignore, Position = Local(OrderLine.TextX, OrderLine.TextY), ClipContents = true,
            Size = new Vector2(OrderLine.TextWidth, OrderLine.TextHeight) };
        AreaArtTextDraw.Prepare(_text);
        AddChild(_text);
        Render();
    }

    /// <summary>The strip is the bottom one (origin 0, 441): screen coordinates become the strip's own.</summary>
    private static Vector2 Local(int x, int y) => new(x, y - AreaGameBarLayout.BottomTop);

    /// <summary>Draws <paramref name="look"/>.</summary>
    public void Bind(AreaOrderLineLook look)
    {
        _look = look ?? throw new ArgumentNullException(nameof(look));
        if (IsNodeReady()) Render();
    }

    /// <summary>The text the line shows (the entry's drawn text: one space for an empty one).</summary>
    public void SetText(string shown)
    {
        ArgumentNullException.ThrowIfNull(shown);
        _text.Set(_look?.Font, shown);
    }

    private void Render()
    {
        _background.Texture = _look?.Background;
        _background.Visible = _look?.Background is not null;
        if (_look?.Background is { } background) _background.Size = background.GetSize();
        _text.Set(_look?.Font, _text.Shown);
    }

    private sealed partial class TextLayer : Control
    {
        private AreaArtFontFace? _face;
        private string _shown = " ";

        public string Shown => _shown;

        public void Set(AreaArtFontFace? face, string shown)
        {
            if (ReferenceEquals(face, _face) && shown == _shown) return;
            _face = face; _shown = shown;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_face is not { } face) return;
            DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);
            AreaArtTextDraw.Text(this, face, _shown, 0, 0, OrderLine.TextColour);
            var width = face.Font.Measure(_shown).Width;
            AreaArtTextDraw.Text(this, face, OrderLine.CaretMark, OrderLine.CaretOffset + width, 0, OrderLine.TextColour);
        }
    }
}
