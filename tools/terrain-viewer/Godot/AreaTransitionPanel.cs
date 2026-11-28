using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-204 (B16): the black veil and the load indicator of a change of place over the whole screen (<see cref="AreaTransitionScreen"/>).
/// The veil is black at the screen's alpha; the indicator is the original's clock (<c>loadindicator.art</c>) in the top-right corner while
/// a map loads. It takes the mouse while it shows: the original's change is modal.
/// </summary>
public partial class AreaTransitionPanel : CanvasLayer
{
    private ColorRect _veil = null!;
    private TextureRect _clock = null!;
    private AreaSaveLoadingTextView _loadingText = null!;
    private TextureRect _still = null!;
    private Texture2D[] _frames = [];
    private int _framesPerSecond;

    /// <summary>The veil's alpha as drawn (for the self-test).</summary>
    public float VeilAlpha => _veil.Color.A;

    /// <summary>The clock frame drawn, or -1 when the clock is not shown.</summary>
    public int ClockFrame { get; private set; } = -1;

    /// <summary>True when the clock art was read (its frames are drawn).</summary>
    public bool HasClockArt => _frames.Length > 0;

    /// <summary>ARC-UI-3503: the saved game's loading line (for the self-test).</summary>
    public AreaSaveLoadingTextView LoadingText => _loadingText;

    /// <summary>ARC-UI-3503: true while the standing frame of a load window shows (for the self-test).</summary>
    public bool StillShown => _still.Visible;

    public override void _Ready()
    {
        Layer = 8;
        Visible = false;
        _veil = new ColorRect { Name = "TransitionVeil", Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        _veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_veil);
        _still = new TextureRect
        {
            Name = "LoadWindowStill", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        _still.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_still);
        _clock = new TextureRect
        {
            Name = "LoadIndicator", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        AddChild(_clock);
        _loadingText = new AreaSaveLoadingTextView { Name = "SaveLoadingText", Visible = false };
        AddChild(_loadingText);
    }

    /// <summary>ARC-UI-3503: the frame that stands while a load window's save loads; null (a headless run) leaves the screen black.</summary>
    public void SetStill(Image? frame) => _still.Texture = frame is null || frame.IsEmpty() ? null : ImageTexture.CreateFromImage(frame);

    /// <summary>ARC-UI-3503: gives the panel the saved game's loading line and its font (null: no line is drawn).</summary>
    public void SetLoadingText(AreaArtFontFace? face, string text) => _loadingText.SetContent(face, text);

    /// <summary>Gives the panel the clock's frames (art 601) and their rate; none draws a black screen without the clock.</summary>
    public void SetClock(IReadOnlyList<AreaInterfaceFrame> frames, int framesPerSecond)
    {
        _framesPerSecond = framesPerSecond;
        _frames = [.. frames.Where(frame => frame.Width > 0 && frame.Height > 0 && frame.Rgba.Length == frame.Width * frame.Height * 4)
            .Select(frame => (Texture2D)ImageTexture.CreateFromImage(Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, frame.Rgba)))];
    }

    /// <summary>Draws <paramref name="screen"/>: the veil at its alpha, the clock at its frame in the top-right corner.</summary>
    public void Apply(AreaTransitionScreen screen)
    {
        Visible = screen.Covering;
        _veil.Color = new Color(0, 0, 0, (float)screen.Alpha);
        var still = screen.Picture == AreaLoadPicture.FrozenWindow && _still.Texture is not null;
        _still.Visible = still;
        // The standing frame is let go with the load window's load.
        if (screen.Picture != AreaLoadPicture.FrozenWindow) _still.Texture = null;
        var text = screen.Picture == AreaLoadPicture.SavedGameText;
        // Fitted on every draw: the window may change size while the load runs.
        if (text) _loadingText.Fit(GetViewport().GetVisibleRect().Size);
        _loadingText.Visible = text;
        var clockShown = screen.IndicatorShown && _frames.Length > 0;
        _clock.Visible = clockShown;
        ClockFrame = -1;
        if (!clockShown) return;
        ClockFrame = screen.IndicatorFrame(_frames.Length, _framesPerSecond);
        var texture = _frames[ClockFrame];
        _clock.Texture = texture;
        // Top-right corner, AreaTransitionScreen.IndicatorMargin pixels in (init 0x4BBB20).
        _clock.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _clock.OffsetRight = -AreaTransitionScreen.IndicatorMargin;
        _clock.OffsetLeft = -AreaTransitionScreen.IndicatorMargin - texture.GetWidth();
        _clock.OffsetTop = AreaTransitionScreen.IndicatorMargin;
        _clock.OffsetBottom = AreaTransitionScreen.IndicatorMargin + texture.GetHeight();
    }
}
