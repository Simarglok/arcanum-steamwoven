using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-1742: the auto-level scheme page of the character sheet in the original's look (<see cref="AreaCharacterSheetSchemesLayout"/>, ledger ALP1..ALP16):
/// a control of 235 × 325 at the sheet's (503, 63), drawn in the page's own coordinates. Art 567, the critter's scheme name centred near the top in
/// Flare12 blue, fifteen rows of scheme names in Flare12 white (blue under the pointer) and the scroll bar at the right: the up and down arrows (art
/// 317, 318) and the thumb (arts 238, 787, 240, stretched to the bar's width). A row released over itself writes that scheme into the critter; the NPC
/// sheet (mode 2) hides the row buttons, so nothing answers there but the scroll bar. A button takes the pointer on its rectangle (PROJECT).
/// </summary>
public partial class AreaCharacterSheetSchemesView : Control
{
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private AreaCharacterSheetFonts? _fonts;
    private AreaSchemesPageView? _view;
    private AreaSheetScrollBar? _bar;
    private int _scroll, _hoverRow = -1, _pressedRow = -1, _dragOffset;
    private AreaSheetScrollBar.Part _hoverPart, _pressedPart;

    /// <summary>A row released over itself: the scheme number it shows.</summary>
    public event Action<int>? SchemeChosen;
    public event Action<int>? SoundRequested;

    /// <summary>ARC-UI-088: the NPC sheet of a dialog (mode 2) hides the row buttons (0x5597C0 hides the fifteen of 0x64C7E8): no hover, no press.</summary>
    public bool ReadOnly { get; set; }

    internal Vector2 PointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);
    internal AreaSchemesPageView? ViewForSelfTest => _view;
    internal AreaSheetScrollBar? BarForSelfTest => _bar;
    internal int ScrollForSelfTest => _scroll;
    internal int HoverRowForSelfTest => _hoverRow;

    public override void _Ready()
    {
        Name = "SheetSchemes";
        var (x, y, width, height) = AreaCharacterSheetArtLayout.Panel;
        Position = new Vector2(x, y);
        Size = new Vector2(width, height);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        AreaArtTextDraw.Prepare(this);
    }

    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaCharacterSheetFonts fonts)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        _bar = null;
    }

    /// <summary>The tab's page shows (0x55AC4D): the scroll position is kept while the sheet stays open, as the exe's list offset is (reset at each open).</summary>
    public void ShowPage()
    {
        (_hoverRow, _pressedRow, _hoverPart, _pressedPart) = (-1, -1, AreaSheetScrollBar.Part.None, AreaSheetScrollBar.Part.None);
        Visible = true;
        QueueRedraw();
    }

    public void Render(AreaSchemesPageView? view)
    {
        _view = view;
        if (view is null) _bar = null;
        else if (_bar is null || _bar.Maximum != view.ScrollMaximum)
        {
            _bar = AreaCharacterSheetSchemesLayout.MakeBar(view, Arrows(), Math.Min(_scroll, view.ScrollMaximum));
            _bar.Changed += value => { _scroll = value; QueueRedraw(); };
        }
        QueueRedraw();
    }

    public void CloseView()
    {
        (_view, _bar, _scroll, _hoverRow, _pressedRow, _hoverPart, _pressedPart, _dragOffset) = (null, null, 0, -1, -1, AreaSheetScrollBar.Part.None, AreaSheetScrollBar.Part.None, 0);
        Visible = false;
    }

    private Texture2D? Art(int number, int frame = 0) => AreaCharacterArtDraw.Art(_art, number, frame);

    private int Frames(int art) => _art.TryGetValue(art, out var frames) ? frames.Length : 1;

    private AreaScrollArrows Arrows() => new(Art(AreaCharacterSheetSchemesLayout.UpArrowArt)?.GetWidth() ?? 0, Art(AreaCharacterSheetSchemesLayout.UpArrowArt)?.GetHeight() ?? 0,
        Art(AreaCharacterSheetSchemesLayout.DownArrowArt)?.GetWidth() ?? 0, Art(AreaCharacterSheetSchemesLayout.DownArrowArt)?.GetHeight() ?? 0);

    private void DrawArt(Texture2D? texture, int x, int y, int width = -1, int height = -1)
    {
        if (texture is not null) DrawTextureRect(texture, new Rect2(x, y, width < 0 ? texture.GetWidth() : width, height < 0 ? texture.GetHeight() : height), false);
    }

    private void DrawText(string text, int x, int y, AreaSheetColour colour, bool centred)
    {
        if (_fonts?.Flare is not { } face || text.Length == 0) return;
        AreaArtTextDraw.Text(this, face, text, centred ? x - face.Font.Measure(text).Width / 2 : x, y, AreaCharacterSheetArtLayout.Rgb(colour));
    }

    /// <summary>The arrow's frame: down while held over it, lit under the pointer (the buttons' rule); frame 0 for both while the thumb is dragged (0x5807F0
    /// paints the hidden buttons' art).</summary>
    private int ArrowFrame(int art, AreaSheetScrollBar.Part part)
    {
        if (_bar is { Dragging: true }) return 0;
        return AreaCharacterArtDraw.Frame(Frames(art), _pressedPart != AreaSheetScrollBar.Part.None, _pressedPart == part, _hoverPart == part);
    }

    public override void _Draw()
    {
        if (_fonts is null) return;
        DrawArt(Art(AreaCharacterSheetSchemesLayout.BackgroundArt), 0, 0);
        if (_view is not { } view) return;
        if (view.SchemeName is { } name) DrawText(name, AreaCharacterSheetSchemesLayout.HeaderCentreX, AreaCharacterSheetSchemesLayout.HeaderY, AreaSheetColour.Blue, centred: true);
        for (var row = 0; row < view.ShownRows; row++)
        {
            if (AreaCharacterSheetSchemesLayout.EntryAt(view, _scroll, row) is not { } entry) continue;
            var (x, y, _, _) = AreaCharacterSheetSchemesLayout.Row(row);
            DrawText(entry.Name, x, y, row == _hoverRow && !ReadOnly ? AreaSheetColour.Blue : AreaSheetColour.White, centred: false);
        }
        if (_bar is { } bar) DrawBar(bar);
    }

    private void DrawBar(AreaSheetScrollBar bar)
    {
        var top = bar.DrawnThumbTop(bar.Dragging ? _dragOffset : 0);
        var (x, _, width, _) = AreaCharacterSheetSchemesLayout.Bar;
        var length = bar.ThumbLength;
        // 0x5807F0: the cap (5 high), one-pixel strips while the length less 5 is more than 7, then the foot (7 high), each stretched to the bar's width.
        DrawArt(Art(AreaCharacterSheetSchemesLayout.ThumbTopArt), x, top, width, AreaCharacterSheetSchemesLayout.ThumbTopHeight);
        var y = top + AreaCharacterSheetSchemesLayout.ThumbTopHeight;
        for (var rest = (float)length - AreaCharacterSheetSchemesLayout.ThumbTopHeight; rest > AreaCharacterSheetSchemesLayout.ThumbBottomHeight; rest -= 1f, y++)
            DrawArt(Art(AreaCharacterSheetSchemesLayout.ThumbMiddleArt), x, y, width, 1);
        DrawArt(Art(AreaCharacterSheetSchemesLayout.ThumbBottomArt), x, y, width, AreaCharacterSheetSchemesLayout.ThumbBottomHeight);
        var (ux, uy) = bar.UpArrow;
        var (dx, dy) = bar.DownArrow;
        DrawArt(Art(AreaCharacterSheetSchemesLayout.UpArrowArt, ArrowFrame(AreaCharacterSheetSchemesLayout.UpArrowArt, AreaSheetScrollBar.Part.Up)), ux, uy);
        DrawArt(Art(AreaCharacterSheetSchemesLayout.DownArrowArt, ArrowFrame(AreaCharacterSheetSchemesLayout.DownArrowArt, AreaSheetScrollBar.Part.Down)), dx, dy);
    }

    private int RowAt(Vector2 at) => ReadOnly || _view is null ? -1 : AreaCharacterSheetSchemesLayout.EntryAt(_view, _scroll, AreaCharacterSheetSchemesLayout.RowAt(at.X, at.Y)) is not null
        ? AreaCharacterSheetSchemesLayout.RowAt(at.X, at.Y) : -1;

    private AreaSheetScrollBar.Part PartAt(Vector2 at) => _bar?.PartAt((int)at.X, (int)at.Y) ?? AreaSheetScrollBar.Part.None;

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit) return;
        if (_hoverRow < 0 && _hoverPart == AreaSheetScrollBar.Part.None) return;
        (_hoverRow, _hoverPart) = (-1, AreaSheetScrollBar.Part.None);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        if (_bar is null) return;
        switch (input)
        {
            case InputEventMouseMotion motion:
                if (_bar.Dragging)
                {
                    _dragOffset = _bar.Drag((int)motion.Position.Y);
                    QueueRedraw();
                    break;
                }
                var (row, part) = (RowAt(motion.Position), PartAt(motion.Position));
                // The wheel rectangle's rows are the buttons; the bar's arrows answer only on the arrows (a thumb or track press is not a button).
                if (part is not (AreaSheetScrollBar.Part.Up or AreaSheetScrollBar.Part.Down)) part = AreaSheetScrollBar.Part.None;
                if (row == _hoverRow && part == _hoverPart) break;
                (_hoverRow, _hoverPart) = (row, part);
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                if (_bar.InWheelRectangle((int)wheel.Position.X, (int)wheel.Position.Y))
                {
                    _bar.Turn(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                    QueueRedraw();
                    AcceptEvent();
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                OnButton(button);
                AcceptEvent();
                break;
        }
    }

    private void OnButton(InputEventMouseButton button)
    {
        var bar = _bar!;
        var (x, y) = ((int)button.Position.X, (int)button.Position.Y);
        if (button.Pressed)
        {
            var part = bar.PartAt(x, y);
            switch (part)
            {
                case AreaSheetScrollBar.Part.Up or AreaSheetScrollBar.Part.Down:
                    _pressedPart = part;
                    SoundRequested?.Invoke(AreaCharacterSheetSchemesLayout.ArrowPressSound);
                    break;
                case AreaSheetScrollBar.Part.Thumb:
                    bar.BeginDrag(y);
                    _dragOffset = 0;
                    break;
                case AreaSheetScrollBar.Part.AboveThumb or AreaSheetScrollBar.Part.BelowThumb:
                    bar.PressTrack(part == AreaSheetScrollBar.Part.AboveThumb);
                    break;
                default:
                    _pressedRow = RowAt(button.Position);
                    break;
            }
        }
        else
        {
            if (bar.Dragging) { bar.EndDrag(); _dragOffset = 0; }
            var held = _pressedPart;
            _pressedPart = AreaSheetScrollBar.Part.None;
            if (held != AreaSheetScrollBar.Part.None && bar.PartAt(x, y) == held)
            {
                SoundRequested?.Invoke(AreaCharacterSheetSchemesLayout.ArrowReleaseSound);
                bar.PressArrow(held == AreaSheetScrollBar.Part.Up);
            }
            var pressedRow = _pressedRow;
            _pressedRow = -1;
            if (pressedRow >= 0 && RowAt(button.Position) == pressedRow && _view is { } view
                && AreaCharacterSheetSchemesLayout.EntryAt(view, _scroll, pressedRow) is { } entry) SchemeChosen?.Invoke(entry.Number);
        }
        QueueRedraw();
    }
}
