using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
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
/// ARC-UI-020 (ledger CH7): the PC's two gauges, health and fatigue, each 88 px high filled from the bottom
/// (<see cref="LifeBarRules.GaugeFill"/>), with the current value printed under it (<see cref="LifeBarRules.GaugeNumber"/>) and,
/// while the PC is poisoned, the health gauge in its poisoned colour and the poison level over it. While the interface bars
/// (ARC-UI-018) are shown the gauges fill the bottom strip's vials at the original's places (health 14, 472 and fatigue 754, 473 of
/// the 800 × 600 screen, the numbers at 15, 578, 755, 578 and the poison at 15, 500), at the bars' scale and over them; otherwise
/// they stand in a small panel at the bottom right.
/// </summary>
public partial class AreaHeroGauges : CanvasLayer
{
    // The original's rectangles in its 800 × 600 screen (gauges 0x5C63D8, number slots 0x5C63F8: 0 health, 1 fatigue, 5 poison).
    private static readonly Rect2 HealthGauge = new(14, 472, 28, 88), FatigueGauge = new(754, 473, 28, 88);
    private static readonly Rect2 HealthNumber = new(15, 578, 29, 12), FatigueNumber = new(755, 578, 27, 12), PoisonNumber = new(15, 500, 29, 12);
    private Control _root = null!;
    private PanelContainer _panel = null!;
    private AreaLifeBar _health = null!;
    private AreaLifeBar _fatigue = null!;
    private Label _healthNumber = null!;
    private Label _fatigueNumber = null!;
    private Label _poison = null!;
    private AreaGameBarPanel? _bars;

    internal (int Health, int Fatigue, string HealthText, string FatigueText, string? PoisonText, bool Poisoned) ValuesForSelfTest =>
        (_health.Filled, _fatigue.Filled, _healthNumber.Text, _fatigueNumber.Text, _poison.Visible ? _poison.Text : null,
         _health.FillColor == AreaLifeBar.PoisonedHealthColor);
    internal bool ShownForSelfTest => Visible;
    /// <summary>Whether the gauges sit in the interface bar's vials, and the health and fatigue gauges' window rectangles.</summary>
    internal (bool InBars, Rect2 Health, Rect2 Fatigue) PlacementForSelfTest => (_bars is not null, _health.GetGlobalRect(), _fatigue.GetGlobalRect());

    public override void _Ready()
    {
        // Nearest, as the bars' strips: the numbers are scaled with them by the whole factor.
        _root = new Control { Name = "HeroGauges", MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        AddChild(_root);
        _panel = new PanelContainer { Name = "Back", MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(110, 120) };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .85f) });
        _root.AddChild(_panel);
        _health = new AreaLifeBar { Name = "HealthGauge", Vertical = true, Length = LifeBarRules.GaugeHeight, Thickness = 28 };
        _fatigue = new AreaLifeBar { Name = "FatigueGauge", Vertical = true, Length = LifeBarRules.GaugeHeight, Thickness = 28 };
        _healthNumber = Number("HealthNumber"); _fatigueNumber = Number("FatigueNumber"); _poison = Number("Poison");
        _poison.AddThemeColorOverride("font_color", AreaLifeBar.PoisonedHealthColor);
        _poison.AddThemeColorOverride("font_outline_color", Colors.Black);
        _poison.AddThemeConstantOverride("outline_size", 3);
        _root.AddChild(_health); _root.AddChild(_fatigue);
        _root.AddChild(_healthNumber); _root.AddChild(_fatigueNumber); _root.AddChild(_poison);
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

    /// <summary>Shows the PC's values, in the vials of <paramref name="bars"/> when given (shown), else in the panel; or hides the
    /// gauges (null life).</summary>
    public void Show(AreaLife? life, AreaGameBarPanel? bars)
    {
        if (life is not { } value) { Hide(); return; }
        _health.SetFill(LifeBarRules.GaugeFill(value.Health, value.MaxHealth, LifeBarRules.GaugeHeight),
            value.Poisoned ? AreaLifeBar.PoisonedHealthColor : AreaLifeBar.HealthColor);
        _fatigue.SetFill(LifeBarRules.GaugeFill(value.Fatigue, value.MaxFatigue, LifeBarRules.GaugeHeight), AreaLifeBar.FatigueColor);
        _healthNumber.Text = LifeBarRules.GaugeNumber(value.Health);
        _fatigueNumber.Text = LifeBarRules.GaugeNumber(value.Fatigue);
        _poison.Visible = value.Poisoned;
        _poison.Text = value.Poisoned ? LifeBarRules.GaugeNumber(value.Poison) : "";
        _bars = bars;
        Show();
        Place();
    }

    private static void Put(Control control, Rect2 rect) { control.Position = rect.Position; control.Size = rect.Size; }

    private void Place()
    {
        if (_root is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var inBars = _bars is { Visible: true } && AreaGameBarPanel.BottomInset > 0;
        // Over the bars (layer 5) in their vials; under the windows (layer 4) in the panel.
        // In the panel under the hover tooltip (layer 3), which may flip into the bottom right corner.
        Layer = inBars ? 6 : 2;
        _panel.Visible = !inBars;
        _health.DrawEmpty = _fatigue.DrawEmpty = !inBars;
        foreach (var label in new[] { _healthNumber, _fatigueNumber, _poison }) label.AddThemeFontSizeOverride("font_size", inBars ? 9 : 14);
        if (inBars)
        {
            // The bars' own placement: the 800 × 600 screen centred at the whole factor, its bottom strip at the window's bottom.
            var factor = _bars!.Factor;
            var left = MathF.Round((size.X - AreaGameBarLayout.ScreenWidth * factor) / 2);
            _root.Position = new(left, size.Y - AreaGameBarLayout.ScreenHeight * factor);
            _root.Scale = new(factor, factor);
            Put(_health, HealthGauge); Put(_fatigue, FatigueGauge);
            // A label is at least its line high: each grows around the middle of the original's 12 px slot.
            foreach (var (label, slot) in new[] { (_healthNumber, HealthNumber), (_fatigueNumber, FatigueNumber), (_poison, PoisonNumber) })
            {
                var height = Math.Max(slot.Size.Y, label.GetCombinedMinimumSize().Y);
                Put(label, new Rect2(slot.Position.X, slot.Position.Y + (slot.Size.Y - height) / 2, slot.Size.X, height));
            }
            return;
        }
        _root.Scale = Vector2.One;
        _root.Position = new(Math.Max(12, size.X - _panel.Size.X - 12), Math.Max(12, size.Y - _panel.Size.Y - 12));
        Put(_health, new Rect2(10, 8, 28, 88)); Put(_fatigue, new Rect2(72, 8, 28, 88));
        Put(_healthNumber, new Rect2(4, 98, 40, 18)); Put(_fatigueNumber, new Rect2(66, 98, 40, 18)); Put(_poison, new Rect2(38, 44, 34, 18));
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
        _health.SetFill(LifeBarRules.HoverFill(life.HealthPercent, BarWidth), life.Poisoned ? AreaLifeBar.PoisonedHealthColor : AreaLifeBar.HealthColor);
        _fatigue.SetFill(LifeBarRules.HoverFill(life.FatiguePercent, BarWidth), AreaLifeBar.FatigueColor);
        _healthText.Text = LifeBarRules.HoverText(life.Health, life.MaxHealth, shown.PcSide);
        _fatigueText.Text = LifeBarRules.HoverText(life.Fatigue, life.MaxFatigue, shown.PcSide);
        Visible = true;
    }
}
