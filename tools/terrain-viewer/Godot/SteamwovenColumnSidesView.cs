using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-2891 (TRD "Column mode"): what lies around the column behind a full-screen or two-strip menu. The window outside the column is black (<see cref="SteamwovenColumnMenu.BlackRects"/>); with
/// <see cref="SteamwovenColumnSides.Vignette"/> a soft fall-off of the plate's own edge tone hangs beside the plate (<see cref="SteamwovenColumnMenu.VignetteRects"/>); a vertical gap the plate leaves in the column
/// (16:10, or k = 0.8) takes the generated dark wood, painted off the main thread like every board. The menu's plate is a sibling drawn over it. The view takes the pointer over the whole window (the menu is modal).
/// </summary>
internal sealed partial class SteamwovenColumnSidesView : Control
{
    private const int Stops = 8;
    private readonly TextureRect _left = new() { Name = "VignetteLeft", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
    private readonly TextureRect _right = new() { Name = "VignetteRight", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
    private readonly SteamwovenBoardView _gapAbove = new() { Name = "ColumnGapAbove", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
    private readonly SteamwovenBoardView _gapBelow = new() { Name = "ColumnGapBelow", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
    private bool _wantAbove, _wantBelow;
    private IReadOnlyList<PixelRect> _black = [];
    private (SteamwovenFullScreenPlan Plan, SteamwovenColumnSides Sides, Color Tone)? _shown;

    /// <summary>The vignette fall-off texture, left or right: the tone with an alpha that follows <see cref="SteamwovenColumnMenu.VignetteWeight"/> from the plate's edge outwards.</summary>
    private static GradientTexture2D Fall(Color tone, bool leftSide)
    {
        var gradient = new Gradient { InterpolationMode = Gradient.InterpolationModeEnum.Linear };
        var offsets = new float[Stops + 1];
        var colours = new Color[Stops + 1];
        for (var i = 0; i <= Stops; i++)
        {
            offsets[i] = (float)i / Stops;
            colours[i] = new Color(tone.R, tone.G, tone.B, (float)SteamwovenColumnMenu.VignetteWeight((double)i / Stops));
        }
        gradient.Offsets = offsets; gradient.Colors = colours;
        // The left side's fall-off runs from its right edge (the plate) to its left; the right side's from its left edge to its right.
        return new GradientTexture2D
        {
            Gradient = gradient, Width = 256, Height = 4, Fill = GradientTexture2D.FillEnum.Linear,
            FillFrom = leftSide ? new Vector2(1, 0) : new Vector2(0, 0), FillTo = leftSide ? new Vector2(0, 0) : new Vector2(1, 0),
        };
    }

    public override void _Ready()
    {
        Name = "ColumnSides";
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        TextureFilter = TextureFilterEnum.Linear;
        AddChild(_gapAbove); AddChild(_gapBelow); AddChild(_left); AddChild(_right);
        _left.Visible = _right.Visible = false;
    }

    /// <summary>Shows the surroundings of the column of <paramref name="plan"/> (the window's size is its layout's), with the vignette tone <paramref name="tone"/> when the sides option asks for it. Idempotent for the same arguments.</summary>
    public void Present(SteamwovenFullScreenPlan plan, SteamwovenColumnSides sides, Color tone)
    {
        if (!plan.IsColumn) { Clear(); return; }
        Position = Vector2.Zero;
        Size = new Vector2(plan.Layout.WindowWidth, plan.Layout.WindowHeight);
        Visible = true;
        var column = plan.Column!;
        // The gaps take the generated wood; its grain is laid out in original units from the plate's top left, as the full-width backdrop's was.
        _wantAbove = Gap(_gapAbove, column.FullScreen.GapAbove, plan);
        _wantBelow = Gap(_gapBelow, column.FullScreen.GapBelow, plan);
        ShowPaintedGaps();
        if (_shown is { } last && last.Plan == plan && last.Sides == sides && last.Tone == tone) return;
        _shown = (plan, sides, tone);
        _black = SteamwovenColumnMenu.BlackRects(plan);
        if (sides == SteamwovenColumnSides.Vignette)
        {
            var (left, right) = SteamwovenColumnMenu.VignetteRects(plan);
            Fit(_left, left, Fall(tone, true));
            Fit(_right, right, Fall(tone, false));
        }
        else _left.Visible = _right.Visible = false;
        QueueRedraw();
    }

    private static void Fit(TextureRect rect, PixelRect at, Texture2D texture)
    {
        rect.Visible = at.Width > 0 && at.Height > 0;
        rect.Texture = texture;
        rect.Position = new Vector2(at.X, at.Y); rect.Size = new Vector2(at.Width, at.Height);
    }

    /// <summary>Requests the wood of a gap; false (and the board hidden) where the plate leaves none.</summary>
    private static bool Gap(SteamwovenBoardView board, PixelRect gap, SteamwovenFullScreenPlan plan)
    {
        if (gap.Height <= 0 || gap.Width <= 0) { board.Visible = false; return false; }
        board.Request(new(ChromeMaterial.WoodDark, ChromeSides.None, new Rect2I(gap.X, gap.Y, gap.Width, gap.Height), plan.Factor,
            (gap.X - plan.Zone.X) / plan.Factor, (gap.Y - plan.Zone.Y) / plan.Factor, null, null, 0, 0));
        return true;
    }

    // A board shows once its first paint is in (until then the black of the window shows, as with the full-width backdrop).
    private void ShowPaintedGaps()
    {
        _gapAbove.Visible = _wantAbove && _gapAbove.Texture is not null;
        _gapBelow.Visible = _wantBelow && _gapBelow.Texture is not null;
    }

    public override void _Process(double delta)
    {
        if (Visible) ShowPaintedGaps();
    }

    /// <summary>Hides the surroundings (the menu is not in the column).</summary>
    public void Clear()
    {
        if (_shown is null && !Visible) return;
        _shown = null; _black = []; _wantAbove = _wantBelow = false;
        Visible = false;
        _left.Visible = _right.Visible = _gapAbove.Visible = _gapBelow.Visible = false;
    }

    public override void _Draw()
    {
        foreach (var rect in _black) DrawRect(new Rect2(rect.X, rect.Y, rect.Width, rect.Height), Colors.Black);
    }

    /// <summary>For the self-test: the black rectangles now drawn.</summary>
    internal IReadOnlyList<PixelRect> BlackForSelfTest => Visible ? _black : [];

    /// <summary>For the self-test: the vignette rectangles shown (left, right; empty when none).</summary>
    internal IReadOnlyList<Rect2> VignetteForSelfTest => Visible ? [.. new[] { _left, _right }.Where(rect => rect.Visible).Select(rect => new Rect2(rect.Position, rect.Size))] : [];

    /// <summary>For the self-test: the wood gaps (above, below): the board's rectangle, whether its paint is in and shown, whether a paint is pending.</summary>
    internal IReadOnlyList<(Rect2 Rect, bool Shown, bool Pending)> GapsForSelfTest =>
        [.. new[] { _gapAbove, _gapBelow }.Select(board => (new Rect2(board.Position, board.Size), board.Visible, board.Pending))];

    /// <summary>For the self-test: true while a gap board is still being painted (the texture of a requested gap is not in yet).</summary>
    internal bool PaintPendingForSelfTest => new[] { _gapAbove, _gapBelow }.Any(board => board.Pending);
}
