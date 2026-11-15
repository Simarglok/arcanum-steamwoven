using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>One button of the magick weapon pane: the item's spell slot (its place), the slot's spell and its icon's frames (null when the
/// art did not read).</summary>
public sealed record AreaWeaponBarSlot(int Slot, int Spell, Texture2D[]? Frames);

/// <summary>What the magick weapon parts of the bottom strip show: the weapon button's frames and whether it shows, and, in bar mode 8,
/// the mode's background and the slots' buttons.</summary>
public sealed record AreaWeaponBarLook(Texture2D[]? Button, bool ButtonShown, Texture2D? Background, bool PaneShown, IReadOnlyList<AreaWeaponBarSlot> Slots);

/// <summary>
/// ARC-UI-2304: the original's magick weapon button and pane (bar mode 8) drawn in the bottom strip of <see cref="AreaGameBarPanel"/>, in
/// the strip's frame (ledger BM6, IP1–IP10): the button `MagickWeapon_Butt` at (161, 443) while the wielded weapon qualifies, and in mode 8
/// the background `MagickWeapon_Rot` at (196, 492) with an icon button per shown spell slot at slot i's place (353 + …, 542). The buttons
/// are the bar's push buttons (GB3, GB4): down while held over them, lit under the pointer. It raises what the pointer does on them; the app
/// decides (the weapon button toggles mode 8 on the release over it, a slot activates its spell on the release over it, a right press on a
/// slot starts a hotkey drag).
/// </summary>
public partial class AreaWeaponBarView : Control
{
    private const int ButtonPlace = -1;
    private TextureRect _button = null!, _background = null!;
    private readonly TextureRect[] _slots = new TextureRect[MagickWeaponPaneRules.SlotButtonX.Count];
    private AreaWeaponBarLook? _look;
    // The place held down with the left button and the one under the pointer (ButtonPlace the weapon button, else a slot).
    private int? _held, _hovered;

    /// <summary>A left press on the weapon button and its release (and whether over the button).</summary>
    public event Action? ButtonPressed;
    public event Action<bool>? ButtonReleased;
    /// <summary>A left press on a slot's button and its release (and whether over the button).</summary>
    public event Action<int>? SlotPressed;
    public event Action<int, bool>? SlotReleased;
    /// <summary>A right press on a slot's button: the original starts a drag onto the hotkey bar (type 4, IP9).</summary>
    public event Action<int>? SlotRightPressed;
    /// <summary>The pointer entered a slot's button (its slot).</summary>
    public event Action<int>? SlotEntered;

    internal Vector2? ButtonCentreForSelfTest => _button.Visible ? _button.GetGlobalRect().GetCenter() : null;
    internal Vector2? ButtonPositionForSelfTest => _button.Visible ? _button.Position : null;
    internal Vector2? SlotCentreForSelfTest(int slot) => slot >= 0 && slot < _slots.Length && _slots[slot].Visible ? _slots[slot].GetGlobalRect().GetCenter() : null;
    internal Vector2? SlotPositionForSelfTest(int slot) => slot >= 0 && slot < _slots.Length && _slots[slot].Visible ? _slots[slot].Position : null;
    internal Texture2D? ButtonArtForSelfTest => _button.Visible ? _button.Texture : null;
    internal Texture2D? BackgroundForSelfTest => _background.Visible ? _background.Texture : null;
    internal Texture2D? SlotArtForSelfTest(int slot) => slot >= 0 && slot < _slots.Length && _slots[slot].Visible ? _slots[slot].Texture : null;
    internal IReadOnlyList<int> ShownSlotsForSelfTest => _look is { PaneShown: true } look ? look.Slots.Select(slot => slot.Slot).ToArray() : [];

    public override void _Ready()
    {
        Name = "MagickWeaponPane";
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(AreaGameBarLayout.ScreenWidth, AreaGameBarLayout.BottomHeight);
        _background = new TextureRect { Name = "Background", Position = Local(MagickWeaponPaneRules.BackgroundX, MagickWeaponPaneRules.BackgroundY), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_background);
        _button = Place("Button", Local(MagickWeaponPaneRules.ButtonX, MagickWeaponPaneRules.ButtonY), ButtonPlace);
        for (var s = 0; s < _slots.Length; s++)
        {
            _slots[s] = Place("Slot" + s, Local(MagickWeaponPaneRules.SlotButtonX[s], MagickWeaponPaneRules.SlotButtonY), s);
            _slots[s].Size = new Vector2(MagickWeaponPaneRules.SlotButtonSize, MagickWeaponPaneRules.SlotButtonSize);
        }
        Render();
    }

    /// <summary>The strip is the bottom one (origin 0, 441): screen coordinates become the strip's own.</summary>
    private static Vector2 Local(int x, int y) => new(x, y - AreaGameBarLayout.BottomTop);

    private TextureRect Place(string name, Vector2 position, int place)
    {
        var rect = new TextureRect { Name = name, Position = position, MouseFilter = MouseFilterEnum.Stop, Visible = false };
        rect.GuiInput += input => PlaceInput(rect, input, place);
        rect.MouseEntered += () => { _hovered = place; Render(); if (place != ButtonPlace) SlotEntered?.Invoke(place); };
        rect.MouseExited += () => { if (_hovered == place) _hovered = null; Render(); };
        AddChild(rect);
        return rect;
    }

    private void PlaceInput(Control rect, InputEvent input, int place)
    {
        if (input is not InputEventMouseButton click) return;
        if (click.ButtonIndex == MouseButton.Right)
        {
            if (!click.Pressed || place == ButtonPlace) return;
            rect.AcceptEvent();
            SlotRightPressed?.Invoke(place);
            return;
        }
        if (click.ButtonIndex != MouseButton.Left) return;
        rect.AcceptEvent();
        if (click.Pressed)
        {
            _held = place;
            Render();
            if (place == ButtonPlace) ButtonPressed?.Invoke(); else SlotPressed?.Invoke(place);
            return;
        }
        // Godot sends the release to the control that took the press; it is over the button when it lands inside it.
        var over = _held == place && new Rect2(Vector2.Zero, rect.Size).HasPoint(click.Position);
        _held = null;
        Render();
        if (place == ButtonPlace) ButtonReleased?.Invoke(over); else SlotReleased?.Invoke(place, over);
    }

    /// <summary>Draws <paramref name="look"/>.</summary>
    public void Bind(AreaWeaponBarLook look)
    {
        _look = look ?? throw new ArgumentNullException(nameof(look));
        if (_held is { } held && !Shows(held)) _held = null;
        if (_hovered is { } hovered && !Shows(hovered)) _hovered = null;
        if (IsNodeReady()) Render();
    }

    private bool Shows(int place) => place == ButtonPlace ? _look is { ButtonShown: true } : _look is { PaneShown: true } look && look.Slots.Any(slot => slot.Slot == place);

    private void Render()
    {
        var look = _look;
        _background.Texture = look is { PaneShown: true } ? look.Background : null;
        _background.Visible = _background.Texture is not null;
        DrawPlace(_button, look is { ButtonShown: true } ? look.Button : null, ButtonPlace);
        for (var s = 0; s < _slots.Length; s++)
            DrawPlace(_slots[s], look is { PaneShown: true } ? look.Slots.FirstOrDefault(slot => slot.Slot == s)?.Frames : null, s);
    }

    // A button's frame (GB4): down while held with the pointer over it; lit (two steps) under the pointer; else idle. A button whose art did
    // not read is not shown (no invisible area takes the pointer).
    private void DrawPlace(TextureRect rect, Texture2D[]? frames, int place)
    {
        rect.Visible = frames is { Length: > 0 };
        if (frames is not { Length: > 0 }) return;
        var held = _held == place;
        var hovered = _hovered == place;
        var look = held && hovered ? AreaBarButtonLook.Down : hovered && !held ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle;
        rect.Texture = frames[AreaGameBarLayout.Frame(look, frames.Length)];
        if (place == ButtonPlace) rect.Size = rect.Texture.GetSize();
    }
}
