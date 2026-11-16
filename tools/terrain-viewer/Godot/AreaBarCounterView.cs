using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>What the bottom strip's counters show: counter 3's icon frames (null: no art) and text, counter 4's text (null: it does
/// not draw, outside bar mode 8) and the bar font (art 171; null: the stand-in font).</summary>
public sealed record AreaBarCounterLook(Texture2D[]? Icon, string Counter3, string? Counter4, AreaArtFontFace? Font = null);

/// <summary>
/// ARC-UI-2311: the original's counters 3 and 4 of the bottom strip (ledger BC1–BC8, slots 3 and 4 of 0x5C63F8, refreshed by 0x54B3C0):
/// counter 3 (six digits, rectangle (104, 512, 50 × 20)) with its icon at (61, 509), counter 4 (three digits, (264, 562, 50 × 20)) in
/// bar mode 8. ARC-UI-2368: each number is an <see cref="AreaBarNumber"/>: its slot filled black, the text in the bar font (art 171) in white
/// with a shadow, centred as 0x54AD00 does (ledger BC5, BC9). Draws only; <see cref="Bind"/> takes what to show.
/// </summary>
public partial class AreaBarCounterView : Control
{
    private TextureRect _icon = null!;
    private AreaBarNumber _counter3 = null!, _counter4 = null!;

    internal Texture2D? IconForSelfTest => _icon.Visible ? _icon.Texture : null;
    internal Vector2? IconPositionForSelfTest => _icon.Visible ? _icon.Position : null;
    internal string? Counter3ForSelfTest => _counter3.Visible ? _counter3.Text : null;
    internal string? Counter4ForSelfTest => _counter4.Visible ? _counter4.Text : null;
    internal AreaArtFontFace? FontForSelfTest => _counter3.Face;
    internal (int X, int Y)? Counter3PlacementForSelfTest => _counter3.Visible ? _counter3.PlacementForSelfTest : null;
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

    private AreaBarNumber Number(string name, (int X, int Y, int Width, int Height) slot)
    {
        var number = new AreaBarNumber { Name = name, MouseFilter = MouseFilterEnum.Ignore, Visible = false, Position = Local(slot.X, slot.Y), Size = new Vector2(slot.Width, slot.Height) };
        AddChild(number);
        return number;
    }

    public void Bind(AreaBarCounterLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        _icon.Texture = look.Icon is { Length: > 0 } frames ? frames[0] : null;
        _icon.Visible = _icon.Texture is not null;
        _counter3.Face = _counter4.Face = look.Font;
        _counter3.Text = look.Counter3;
        _counter3.Visible = true;
        _counter4.Text = look.Counter4 ?? "";
        _counter4.Visible = look.Counter4 is not null;
    }
}
