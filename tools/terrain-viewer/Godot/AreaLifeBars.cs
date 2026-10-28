using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-020: one health or fatigue bar drawn as two rectangles (the viewer has no original interface art): the filled part
/// grows from the left, or from the bottom when <see cref="Vertical"/>. Its size is fixed (<see cref="Length"/> pixels along the
/// fill), so the filled pixels are the rule's for that length. The owner sets the fill and the colour.
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

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        CustomMinimumSize = Vertical ? new Vector2(Thickness, Length) : new Vector2(Length, Thickness);
    }

    public void Set(int filled, Color color)
    {
        filled = Math.Clamp(filled, 0, Length);
        if (filled == Filled && color == FillColor) return;
        Filled = filled; FillColor = color;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Vertical ? new Vector2(Thickness, Length) : new Vector2(Length, Thickness);
        DrawRect(new Rect2(Vector2.Zero, size), EmptyColor);
        if (Filled <= 0) return;
        DrawRect(Vertical ? new Rect2(0, Length - Filled, Thickness, Filled) : new Rect2(0, 0, Filled, Thickness), FillColor);
    }
}

/// <summary>
/// ARC-UI-020 (ledger CH7): the PC's two gauges, health and fatigue, each 88 px high filled from the bottom
/// (<see cref="LifeBarRules.GaugeFill"/>), with the current value printed under it (<see cref="LifeBarRules.GaugeNumber"/>) and,
/// while the PC is poisoned, the health gauge in its poisoned colour and the poison level beside it. The original draws them at the
/// two ends of its interface bar; the viewer has none, so they stand together in a small panel at the bottom right. Hidden without
/// a hero or while a window is open.
/// </summary>
public partial class AreaHeroGauges : CanvasLayer
{
    private PanelContainer _panel = null!;
    private AreaLifeBar _health = null!;
    private AreaLifeBar _fatigue = null!;
    private Label _healthNumber = null!;
    private Label _fatigueNumber = null!;
    private Label _poison = null!;

    internal (int Health, int Fatigue, string HealthText, string FatigueText, string? PoisonText, bool Poisoned) ValuesForSelfTest =>
        (_health.Filled, _fatigue.Filled, _healthNumber.Text, _fatigueNumber.Text, _poison.Visible ? _poison.Text : null,
         _health.FillColor == AreaLifeBar.PoisonedHealthColor);
    internal bool ShownForSelfTest => Visible;

    public override void _Ready()
    {
        Layer = 3;
        _panel = new PanelContainer { Name = "HeroGauges", MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .85f),
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        AddChild(_panel);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(row);
        (_health, _healthNumber) = Column(row, "Health");
        _poison = new Label { Name = "Poison", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, VerticalAlignment = VerticalAlignment.Bottom };
        _poison.AddThemeColorOverride("font_color", AreaLifeBar.PoisonedHealthColor);
        row.AddChild(_poison);
        (_fatigue, _fatigueNumber) = Column(row, "Fatigue");
        GetViewport().SizeChanged += Place;
        Hide();
    }

    private static (AreaLifeBar Bar, Label Number) Column(HBoxContainer row, string name)
    {
        var column = new VBoxContainer { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        var bar = new AreaLifeBar { Name = name + "Gauge", Vertical = true, Length = LifeBarRules.GaugeHeight, Thickness = 28 };
        var number = new Label { Name = name + "Number", MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
        column.AddChild(bar); column.AddChild(number); row.AddChild(column);
        return (bar, number);
    }

    /// <summary>Shows the PC's values, or hides the gauges (null).</summary>
    public void Show(AreaLife? life)
    {
        if (life is not { } value) { Hide(); return; }
        _health.Set(LifeBarRules.GaugeFill(value.Health, value.MaxHealth, LifeBarRules.GaugeHeight),
            value.Poisoned ? AreaLifeBar.PoisonedHealthColor : AreaLifeBar.HealthColor);
        _fatigue.Set(LifeBarRules.GaugeFill(value.Fatigue, value.MaxFatigue, LifeBarRules.GaugeHeight), AreaLifeBar.FatigueColor);
        _healthNumber.Text = LifeBarRules.GaugeNumber(value.Health);
        _fatigueNumber.Text = LifeBarRules.GaugeNumber(value.Fatigue);
        _poison.Visible = value.Poisoned;
        _poison.Text = value.Poisoned ? LifeBarRules.GaugeNumber(value.Poison) : "";
        Show();
        Place();
    }

    private void Place()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        _panel.ResetSize();
        var panel = _panel.GetCombinedMinimumSize();
        _panel.Position = new(Math.Max(12, size.X - panel.X - 12), Math.Max(12, size.Y - panel.Y - 12));
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
        _health.Set(LifeBarRules.HoverFill(life.HealthPercent, BarWidth), life.Poisoned ? AreaLifeBar.PoisonedHealthColor : AreaLifeBar.HealthColor);
        _fatigue.Set(LifeBarRules.HoverFill(life.FatiguePercent, BarWidth), AreaLifeBar.FatigueColor);
        _healthText.Text = LifeBarRules.HoverText(life.Health, life.MaxHealth, shown.PcSide);
        _fatigueText.Text = LifeBarRules.HoverText(life.Fatigue, life.MaxFatigue, shown.PcSide);
        Visible = true;
    }
}
