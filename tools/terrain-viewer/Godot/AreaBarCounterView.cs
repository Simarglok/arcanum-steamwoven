using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>What the bottom strip's counters show: counter 3's icon frames (null: no art) and text, and counter 4's text (null: it does
/// not draw, outside bar mode 8).</summary>
public sealed record AreaBarCounterLook(Texture2D[]? Icon, string Counter3, string? Counter4);

/// <summary>
/// ARC-UI-2311: the original's counters 3 and 4 of the bottom strip (ledger BC1–BC8, slots 3 and 4 of 0x5C63F8, refreshed by 0x54B3C0):
/// counter 3 (six digits, rectangle (104, 512, 50 × 20)) with its icon at (61, 509), counter 4 (three digits, (264, 562, 50 × 20)) in
/// bar mode 8. The text is centred in its rectangle in the viewer's stand-in font, as the vials' numbers (the bar font, art 171, is
/// not drawn: ARC-UI-020). Draws only; <see cref="Bind"/> takes what to show.
/// </summary>
public partial class AreaBarCounterView : Control
{
    private TextureRect _icon = null!;
    private Label _counter3 = null!, _counter4 = null!;

    internal Texture2D? IconForSelfTest => _icon.Visible ? _icon.Texture : null;
    internal Vector2? IconPositionForSelfTest => _icon.Visible ? _icon.Position : null;
    internal string? Counter3ForSelfTest => _counter3.Visible ? _counter3.Text : null;
    internal string? Counter4ForSelfTest => _counter4.Visible ? _counter4.Text : null;
    internal Rect2? Counter3RectForSelfTest => _counter3.Visible ? new Rect2(_counter3.Position, _counter3.Size) : null;
    internal Rect2? Counter4RectForSelfTest => _counter4.Visible ? new Rect2(_counter4.Position, _counter4.Size) : null;

    public override void _Ready()
    {
        Name = "BarCounters";
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(AreaGameBarLayout.ScreenWidth, AreaGameBarLayout.BottomHeight);
        _icon = new TextureRect { Name = "Counter3Icon", Position = Local(BarCounterRules.IconX, BarCounterRules.IconY), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_icon);
        _counter3 = Number("Counter3", BarCounterRules.Counter3Rect);
        _counter4 = Number("Counter4", BarCounterRules.Counter4Rect);
    }

    private static Vector2 Local(int x, int y) => new(x, y - AreaGameBarLayout.BottomTop);

    private Label Number(string name, (int X, int Y, int Width, int Height) slot)
    {
        var label = new Label
        {
            Name = name, MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            ClipText = false, Visible = false, Position = Local(slot.X, slot.Y), Size = new Vector2(slot.Width, slot.Height),
        };
        label.AddThemeFontSizeOverride("font_size", 10);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 2);
        AddChild(label);
        return label;
    }

    public void Bind(AreaBarCounterLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        _icon.Texture = look.Icon is { Length: > 0 } frames ? frames[0] : null;
        _icon.Visible = _icon.Texture is not null;
        _counter3.Text = look.Counter3;
        _counter3.Visible = true;
        _counter4.Text = look.Counter4 ?? "";
        _counter4.Visible = look.Counter4 is not null;
    }
}
