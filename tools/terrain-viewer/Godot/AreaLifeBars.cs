using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-020: one health or fatigue bar drawn as rectangles (no original fill art): the filled part grows from the left, or from
/// the bottom when <see cref="Vertical"/>; the empty part is drawn only when <see cref="DrawEmpty"/> (inside the interface bar's
/// vials the art shows it) and only while above 0, as the original's gauges. Its size is fixed (<see cref="Length"/> pixels along
/// the fill), so the filled pixels are the rule's for that length. The owner sets the fill and the colour.
/// </summary>
public partial class AreaLifeBar : Control
{
    public static readonly Color HealthColor = new(.78f, .14f, .12f);
    public static readonly Color PoisonedHealthColor = new(.32f, .72f, .18f);
    public static readonly Color FatigueColor = new(.24f, .44f, .86f);
    private static readonly Color EmptyColor = new(.05f, .05f, .07f, .9f);

    public bool Vertical { get; init; }
    /// <summary>The bar's length along the fill and its thickness, in pixels.</summary>
    public int Length { get; init; } = 96;
    public int Thickness { get; init; } = 5;
    public int Filled { get; private set; }
    public Color FillColor { get; private set; } = HealthColor;
    private bool _drawEmpty = true;
    public bool DrawEmpty { get => _drawEmpty; set { if (_drawEmpty == value) return; _drawEmpty = value; QueueRedraw(); } }
    private Vector2 BarSize => Vertical ? new Vector2(Thickness, Length) : new Vector2(Length, Thickness);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = BarSize;
    }

    public void SetFill(int filled, Color color)
    {
        filled = Math.Clamp(filled, 0, Length);
        if (filled == Filled && color == FillColor) return;
        Filled = filled; FillColor = color;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (DrawEmpty && Filled < Length) DrawRect(new Rect2(Vector2.Zero, BarSize), EmptyColor);
        if (Filled <= 0) return;
        DrawRect(Vertical ? new Rect2(0, Length - Filled, Thickness, Filled) : new Rect2(0, 0, Filled, Thickness), FillColor);
    }
}

/// <summary>
/// ARC-UI-024: one of the PC's gauges in the interface bar's vial, drawn with the original's art (ledger PA1–PA3): the empty
/// vial's top rows over the empty part and the fill art from 8 rows above the fill line down to the gauge's bottom
/// (<see cref="LifeBarRules.GaugeArt"/>), both frame 0. Without the art (no game folder) the fill is a plain rectangle.
/// </summary>
public partial class AreaVialGauge : Control
{
    private Texture2D? _fillArt, _emptyArt;
    private Color _fallback = AreaLifeBar.HealthColor;
    public int Filled { get; private set; }
    /// <summary>The fill art's number drawn (null for the plain fallback) and whether the empty vial art is drawn.</summary>
    public int? FillArt { get; private set; }
    public bool EmptyArt => _emptyArt is not null;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(AreaGameBarLayout.HealthGauge.Width, LifeBarRules.GaugeHeight);
    }

    public void Set(int filled, int? art, Texture2D? fillArt, Texture2D? emptyArt, Color fallback)
    {
        filled = Math.Clamp(filled, 0, LifeBarRules.GaugeHeight);
        if (filled == Filled && FillArt == (fillArt is null ? null : art) && _fillArt == fillArt && _emptyArt == emptyArt && _fallback == fallback) return;
        Filled = filled; FillArt = fillArt is null ? null : art; _fillArt = fillArt; _emptyArt = emptyArt; _fallback = fallback;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var width = AreaGameBarLayout.HealthGauge.Width;
        var (empty, top, source, rows) = LifeBarRules.GaugeArt(Filled, LifeBarRules.GaugeHeight);
        // Each part is cut from its art's top left (the empty vial is wider and taller than the gauge).
        if (_emptyArt is not null && empty > 0)
            DrawTextureRectRegion(_emptyArt, new Rect2(0, 0, width, empty), new Rect2(0, 0, Math.Min(width, _emptyArt.GetWidth()), Math.Min(empty, _emptyArt.GetHeight())));
        if (rows <= 0) return;
        if (_fillArt is null) { DrawRect(new Rect2(0, LifeBarRules.GaugeHeight - Filled, width, Filled), _fallback); return; }
        var shown = Math.Min(rows, _fillArt.GetHeight() - source);
        if (shown > 0) DrawTextureRectRegion(_fillArt, new Rect2(0, top, width, shown), new Rect2(0, source, Math.Min(width, _fillArt.GetWidth()), shown));
    }
}

/// <summary>
/// ARC-UI-024: the original's compact panel (ledger PA4, PA5), shown while the interface bars are not drawn: a 49 × 102 window
/// with a (9, 9, 9) outline and divider, the health column (red, green while poisoned) and the fatigue column (blue) filled by
/// <see cref="LifeBarRules.CompactFill"/>, each number (<see cref="LifeBarRules.CompactNumber"/>) in white at its fill's top and
/// a poisoned PC's poison level 40 rows lower. The original's font (art 229) is not drawn: Godot's default font stands in.
/// </summary>
public partial class AreaCompactPanel : Control
{
    private (int Top, int Rows) _health, _fatigue;
    private bool _poisoned;
    private Label _healthNumber = null!, _fatigueNumber = null!, _poison = null!;

    internal ((int Top, int Rows) Health, (int Top, int Rows) Fatigue, string HealthText, string FatigueText, string? PoisonText, bool Poisoned) ValuesForSelfTest =>
        (_health, _fatigue, _healthNumber.Text, _fatigueNumber.Text, _poison.Visible ? _poison.Text : null, _poisoned);

    private static Color Rgb((int R, int G, int B) c) => Color.Color8((byte)c.R, (byte)c.G, (byte)c.B);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        var (_, _, width, height) = AreaCompactPanelLayout.Window;
        Size = CustomMinimumSize = new Vector2(width, height);
        _healthNumber = Number("HealthNumber"); _fatigueNumber = Number("FatigueNumber"); _poison = Number("Poison");
    }

    private Label Number(string name)
    {
        var label = new Label
        {
            Name = name, MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center, ClipText = false,
            Size = new Vector2(AreaCompactPanelLayout.ColumnWidth, 12),
        };
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeFontSizeOverride("font_size", 9);
        label.AddThemeConstantOverride("line_spacing", 0);
        AddChild(label);
        return label;
    }

    public void Set(AreaLife life)
    {
        _health = LifeBarRules.CompactFill(life.Health, life.MaxHealth, fatigue: false);
        _fatigue = LifeBarRules.CompactFill(life.Fatigue, life.MaxFatigue, fatigue: true);
        _poisoned = life.Poisoned;
        _healthNumber.Text = LifeBarRules.CompactNumber(life.Health);
        _fatigueNumber.Text = LifeBarRules.CompactNumber(life.Fatigue);
        _poison.Visible = life.Poisoned;
        _poison.Text = life.Poisoned ? LifeBarRules.CompactNumber(life.Poison) : "";
        _healthNumber.Position = new Vector2(AreaCompactPanelLayout.HealthX, _health.Top);
        _fatigueNumber.Position = new Vector2(AreaCompactPanelLayout.FatigueX, _fatigue.Top);
        _poison.Position = new Vector2(AreaCompactPanelLayout.HealthX, _health.Top + LifeBarRules.CompactPoisonOffset);
        QueueRedraw();
    }

    public override void _Draw()
    {
        // The window's colour key (0, 155, 0) is transparent: only the outline, the divider and the fills show.
        var (_, _, width, height) = AreaCompactPanelLayout.Window;
        var dark = Rgb(AreaCompactPanelLayout.Outline);
        DrawRect(new Rect2(0.5f, 0.5f, width - 1, height - 1), dark, filled: false, width: 1);
        var (x, top, bottom) = AreaCompactPanelLayout.Divider;
        DrawLine(new Vector2(x + .5f, top), new Vector2(x + .5f, bottom + 1), dark, 1);
        if (_health.Rows > 0)
            DrawRect(new Rect2(AreaCompactPanelLayout.HealthX, _health.Top, AreaCompactPanelLayout.ColumnWidth, _health.Rows),
                Rgb(_poisoned ? AreaCompactPanelLayout.PoisonedHealth : AreaCompactPanelLayout.Health));
        if (_fatigue.Rows > 0)
            DrawRect(new Rect2(AreaCompactPanelLayout.FatigueX, _fatigue.Top, AreaCompactPanelLayout.ColumnWidth, _fatigue.Rows), Rgb(AreaCompactPanelLayout.Fatigue));
    }
}

/// <summary>
/// ARC-UI-020 (ledger CH7): the PC's two gauges, health and fatigue, each 88 px high filled from the bottom
/// (<see cref="LifeBarRules.GaugeFill"/>), with the current value printed under it (<see cref="LifeBarRules.GaugeNumber"/>) and,
/// while the PC is poisoned, the health gauge in its poisoned art and the poison level over it. While the interface bars
/// (ARC-UI-018) are shown the gauges fill the bottom strip's vials at the original's places (health 14, 472 and fatigue 754, 473 of
/// the 800 × 600 screen, the numbers at 15, 578, 755, 578 and the poison at 15, 500), at the bars' scale and over them, with the
/// vial art (ARC-UI-024, <see cref="AreaVialGauge"/>); otherwise the original's compact panel (<see cref="AreaCompactPanel"/>)
/// stands at the screen's bottom right, as the original draws it with the interface hidden.
/// </summary>
public partial class AreaHeroGauges : CanvasLayer
{
    // The original's number slots (0x5C63F8: 0 health, 1 fatigue, 5 poison) in its 800 × 600 screen.
    private static readonly Rect2 HealthNumber = new(15, 578, 29, 12), FatigueNumber = new(755, 578, 27, 12), PoisonNumber = new(15, 500, 29, 12);
    private Control _root = null!;
    private AreaVialGauge _health = null!;
    private AreaVialGauge _fatigue = null!;
    private AreaCompactPanel _compact = null!;
    private Label _healthNumber = null!;
    private Label _fatigueNumber = null!;
    private Label _poison = null!;
    private AreaGameBarPanel? _bars;
    private AreaLife _life;

    /// <summary>The drawn fills (the vials' rows in the bars, the compact columns' rows otherwise), the numbers and the poison.</summary>
    internal (int Health, int Fatigue, string HealthText, string FatigueText, string? PoisonText, bool Poisoned) ValuesForSelfTest
    {
        get
        {
            if (_compact.Visible)
            {
                var c = _compact.ValuesForSelfTest;
                return (c.Health.Rows, c.Fatigue.Rows, c.HealthText, c.FatigueText, c.PoisonText, c.Poisoned);
            }
            return (_health.Filled, _fatigue.Filled, _healthNumber.Text, _fatigueNumber.Text, _poison.Visible ? _poison.Text : null, _life.Poisoned);
        }
    }
    internal bool ShownForSelfTest => Visible;
    /// <summary>Whether the gauges sit in the interface bar's vials, and the health and fatigue gauges' window rectangles (the
    /// compact panel's rectangle twice without the bars).</summary>
    internal (bool InBars, Rect2 Health, Rect2 Fatigue) PlacementForSelfTest =>
        _compact.Visible ? (false, _compact.GetGlobalRect(), _compact.GetGlobalRect()) : (true, _health.GetGlobalRect(), _fatigue.GetGlobalRect());
    /// <summary>The vials' fill art numbers drawn (null for a plain fill) and whether both draw the empty vial art.</summary>
    internal (int? Health, int? Fatigue, bool Empty) VialArtForSelfTest => (_health.FillArt, _fatigue.FillArt, _health.EmptyArt && _fatigue.EmptyArt);
    internal AreaCompactPanel CompactForSelfTest => _compact;

    public override void _Ready()
    {
        // Nearest, as the bars' strips: the numbers are scaled with them by the whole factor.
        _root = new Control { Name = "HeroGauges", MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        AddChild(_root);
        _health = new AreaVialGauge { Name = "HealthGauge" };
        _fatigue = new AreaVialGauge { Name = "FatigueGauge" };
        _compact = new AreaCompactPanel { Name = "CompactPanel" };
        _healthNumber = Number("HealthNumber"); _fatigueNumber = Number("FatigueNumber"); _poison = Number("Poison");
        _poison.AddThemeColorOverride("font_color", AreaLifeBar.PoisonedHealthColor);
        _poison.AddThemeColorOverride("font_outline_color", Colors.Black);
        _poison.AddThemeConstantOverride("outline_size", 3);
        _root.AddChild(_health); _root.AddChild(_fatigue); _root.AddChild(_compact);
        _root.AddChild(_healthNumber); _root.AddChild(_fatigueNumber); _root.AddChild(_poison);
        foreach (var label in new[] { _healthNumber, _fatigueNumber, _poison }) label.AddThemeFontSizeOverride("font_size", 9);
        GetViewport().SizeChanged += Place;
        AreaGameBarPanel.InsetsChanged += Place;
        Hide();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Place;
        AreaGameBarPanel.InsetsChanged -= Place;
    }

    private static Label Number(string name) => new()
    {
        Name = name, MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, ClipText = false,
    };

    /// <summary>Shows the PC's values, in the vials of <paramref name="bars"/> when given (shown), else in the compact panel; or
    /// hides the gauges (null life).</summary>
    public void Show(AreaLife? life, AreaGameBarPanel? bars)
    {
        if (life is not { } value) { Hide(); return; }
        _life = value;
        _bars = bars;
        var healthArt = value.Poisoned ? AreaGameBarLayout.PoisonedVialArt : AreaGameBarLayout.HealthVialArt;
        var empty = bars?.ArtFrame(AreaGameBarLayout.EmptyVialArt);
        _health.Set(LifeBarRules.GaugeFill(value.Health, value.MaxHealth, LifeBarRules.GaugeHeight), healthArt, bars?.ArtFrame(healthArt), empty,
            value.Poisoned ? AreaLifeBar.PoisonedHealthColor : AreaLifeBar.HealthColor);
        _fatigue.Set(LifeBarRules.GaugeFill(value.Fatigue, value.MaxFatigue, LifeBarRules.GaugeHeight), AreaGameBarLayout.FatigueVialArt,
            bars?.ArtFrame(AreaGameBarLayout.FatigueVialArt), empty, AreaLifeBar.FatigueColor);
        _healthNumber.Text = LifeBarRules.GaugeNumber(value.Health);
        _fatigueNumber.Text = LifeBarRules.GaugeNumber(value.Fatigue);
        _poison.Text = value.Poisoned ? LifeBarRules.GaugeNumber(value.Poison) : "";
        _compact.Set(value);
        Show();
        Place();
    }

    private static void Put(Control control, Rect2 rect) { control.Position = rect.Position; control.Size = rect.Size; }

    private void Place()
    {
        if (_root is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var inBars = _bars is { Visible: true } && AreaGameBarPanel.BottomInset > 0;
        // Over the bars (layer 5) in their vials; without them under the hover tooltip (layer 3), which may flip into the bottom
        // right corner, and under the windows (layer 4).
        Layer = inBars ? 6 : 2;
        _health.Visible = _fatigue.Visible = _healthNumber.Visible = _fatigueNumber.Visible = inBars;
        _poison.Visible = inBars && _life.Poisoned;
        _compact.Visible = !inBars;
        // The bars' own placement: the 800 × 600 screen centred at the whole factor, its bottom at the window's bottom.
        var factor = inBars ? _bars!.Factor : AreaGameBarPanel.ScreenFactor(size);
        _root.Position = new(AreaGameBarPanel.ScreenLeft(size, factor), size.Y - AreaGameBarLayout.ScreenHeight * factor);
        _root.Scale = new(factor, factor);
        if (!inBars)
        {
            var (x, y, width, height) = AreaCompactPanelLayout.Window;
            Put(_compact, new Rect2(x, y, width, height));
            return;
        }
        var (hx, hy, hw, hh) = AreaGameBarLayout.HealthGauge; var (fx, fy, fw, fh) = AreaGameBarLayout.FatigueGauge;
        Put(_health, new Rect2(hx, hy, hw, hh)); Put(_fatigue, new Rect2(fx, fy, fw, fh));
        // A label is at least its line high: each grows around the middle of the original's 12 px slot.
        foreach (var (label, slot) in new[] { (_healthNumber, HealthNumber), (_fatigueNumber, FatigueNumber), (_poison, PoisonNumber) })
        {
            var height = Math.Max(slot.Size.Y, label.GetCombinedMinimumSize().Y);
            Put(label, new Rect2(slot.Position.X, slot.Position.Y + (slot.Size.Y - height) / 2, slot.Size.X, height));
        }
    }
}

/// <summary>ARC-UI-020 (ledger CH8): the hovered critter's health and fatigue bars under its name in the tooltip (the original
/// draws them side by side on its description line; the tooltip stacks them), each with its text (<see cref="LifeBarRules.HoverText"/>: "current/maximum" for the PC's followers, a percent for anyone else) and the
/// original's fill (<see cref="LifeBarRules.HoverFill"/>); the health bar takes the poisoned colour while the critter is poisoned.</summary>
public partial class AreaHoverLifeBars : VBoxContainer
{
    public const int BarWidth = 120;
    private AreaLifeBar _health = null!;
    private AreaLifeBar _fatigue = null!;
    private Label _healthText = null!;
    private Label _fatigueText = null!;

    internal (int Health, string HealthText, int Fatigue, string FatigueText, bool Poisoned)? ValuesForSelfTest =>
        Visible ? (_health.Filled, _healthText.Text, _fatigue.Filled, _fatigueText.Text, _health.FillColor == AreaLifeBar.PoisonedHealthColor) : null;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 2);
        (_health, _healthText) = Pair("Health");
        (_fatigue, _fatigueText) = Pair("Fatigue");
        Visible = false;
    }

    private (AreaLifeBar, Label) Pair(string name)
    {
        var row = new HBoxContainer { Name = name, MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        var bar = new AreaLifeBar { Name = name + "Bar", Length = BarWidth, Thickness = 8, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var text = new Label { Name = name + "Text", MouseFilter = MouseFilterEnum.Ignore };
        row.AddChild(bar); row.AddChild(text); AddChild(row);
        return (bar, text);
    }

    /// <summary>Shows the bars of a critter (its life and whether it is on the PC's side), or hides them (null).</summary>
    public void Show((AreaLife Life, bool PcSide)? critter)
    {
        if (critter is not { } shown) { Visible = false; return; }
        var life = shown.Life;
        _health.SetFill(LifeBarRules.HoverFill(LifeBarRules.Percent(life.Health, life.MaxHealth), BarWidth), life.Poisoned ? AreaLifeBar.PoisonedHealthColor : AreaLifeBar.HealthColor);
        _fatigue.SetFill(LifeBarRules.HoverFill(LifeBarRules.Percent(life.Fatigue, life.MaxFatigue), BarWidth), AreaLifeBar.FatigueColor);
        _healthText.Text = LifeBarRules.HoverText(life.Health, life.MaxHealth, shown.PcSide);
        _fatigueText.Text = LifeBarRules.HoverText(life.Fatigue, life.MaxFatigue, shown.PcSide);
        Visible = true;
    }
}

/// <summary>ARC-UI-057 (ledger CH8, OH7): the hovered critter's bars in the message window, in the bottom strip's own frame: health on the
/// third line (290, 546) and fatigue on the fourth (290, 564), each 291 px long with its text (<see cref="LifeBarRules.HoverText"/>)
/// right-aligned on its line; the health bar takes the poisoned colour while the critter is poisoned. The original's bar art (463–466,
/// 665, 666) is not drawn: the viewer's bar colours stand in.</summary>
public partial class AreaWindowLifeBars : Control
{
    public const int BarWidth = 291, LineHeight = 18, BarThickness = 8;
    public static readonly (int X, int Y) HealthLine = (290, 546), FatigueLine = (290, 564);
    private AreaLifeBar _health = null!;
    private AreaLifeBar _fatigue = null!;
    private Label _healthText = null!;
    private Label _fatigueText = null!;

    internal (int Health, string HealthText, int Fatigue, string FatigueText, bool Poisoned)? ValuesForSelfTest =>
        Visible ? (_health.Filled, _healthText.Text, _fatigue.Filled, _fatigueText.Text, _health.FillColor == AreaLifeBar.PoisonedHealthColor) : null;

    public override void _Ready()
    {
        Name = "HoverWindowLifeBars";
        MouseFilter = MouseFilterEnum.Ignore;
        (_health, _healthText) = Line("Health", HealthLine);
        (_fatigue, _fatigueText) = Line("Fatigue", FatigueLine);
        Visible = false;
    }

    private (AreaLifeBar, Label) Line(string name, (int X, int Y) at)
    {
        var top = at.Y - AreaGameBarLayout.BottomTop;
        var bar = new AreaLifeBar { Name = name + "Bar", Length = BarWidth, Thickness = BarThickness, Position = new Vector2(at.X, top + (LineHeight - BarThickness) / 2) };
        var text = new Label
        {
            Name = name + "Text", MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(at.X, top), Size = new Vector2(BarWidth, LineHeight),
        };
        text.AddThemeFontSizeOverride("font_size", 12);
        AddChild(bar); AddChild(text);
        return (bar, text);
    }

    /// <summary>Shows the bars of a critter (its life and whether it is on the PC's side), or hides them (null).</summary>
    public void Show((AreaLife Life, bool PcSide)? critter)
    {
        if (critter is not { } shown) { Visible = false; return; }
        var life = shown.Life;
        _health.SetFill(LifeBarRules.HoverFill(LifeBarRules.Percent(life.Health, life.MaxHealth), BarWidth), life.Poisoned ? AreaLifeBar.PoisonedHealthColor : AreaLifeBar.HealthColor);
        _fatigue.SetFill(LifeBarRules.HoverFill(LifeBarRules.Percent(life.Fatigue, life.MaxFatigue), BarWidth), AreaLifeBar.FatigueColor);
        _healthText.Text = LifeBarRules.HoverText(life.Health, life.MaxHealth, shown.PcSide);
        _fatigueText.Text = LifeBarRules.HoverText(life.Fatigue, life.MaxFatigue, shown.PcSide);
        Visible = true;
    }
}
