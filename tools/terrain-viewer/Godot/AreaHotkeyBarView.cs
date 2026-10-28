using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>What one hotkey slot shows: its icon (null none), a text in its place when the icon is not ready (an item's name), the
/// count printed on it, and whether its icon is hidden (the entry being dragged).</summary>
public sealed record AreaHotkeySlotLook(ImageTexture? Icon, string? StandIn, string? Count, bool Hidden);

/// <summary>What the hotkey bar shows: the slot art, the ten slots, the two recent-action buttons' frames (null: an empty action) and
/// the light button's art.</summary>
public sealed record AreaHotkeyBarLook(ImageTexture? Slot, IReadOnlyList<AreaHotkeySlotLook> Slots, IReadOnlyList<ImageTexture[]?> Recent, ImageTexture? Light);

/// <summary>
/// ARC-UI-027: the original's hotkey bar drawn on the bottom strip of <see cref="AreaGameBarPanel"/> (in the strip's frame, so it is
/// scaled with it): the ten slots of the top row with their art, icons and counts (0x57E140), the two recent-action buttons and the light
/// button. It raises what the pointer does on them; the app decides with <see cref="HotkeyBarRules"/>. A left press and release over the
/// same slot or button is its click (the button library's release, GB3); a right press on a slot starts a drag; while the app drags, every
/// release anywhere is reported with its point on the original's screen, and the dragged icon follows the pointer.
/// </summary>
public partial class AreaHotkeyBarView : Control
{
    private readonly TextureRect[] _slots = new TextureRect[HotkeyBarRules.Entries];
    private readonly TextureRect[] _icons = new TextureRect[HotkeyBarRules.Entries];
    private readonly Label[] _standIns = new Label[HotkeyBarRules.Entries];
    private readonly Label[] _counts = new Label[HotkeyBarRules.Entries];
    private readonly TextureRect[] _recent = new TextureRect[2];
    private TextureRect _light = null!;
    private TextureRect _ghost = null!;
    private AreaHotkeyBarLook? _look;
    private int? _leftOnSlot;
    private int? _recentHeld;
    private bool _dragging;

    /// <summary>A left press and release over slot <c>entry</c>.</summary>
    public event Action<int>? SlotClicked;
    /// <summary>A right press over slot <c>entry</c>.</summary>
    public event Action<int>? SlotRightPressed;
    /// <summary>A recent-action button went down (its index, the latest 0) or came up (and whether over it).</summary>
    public event Action<int>? RecentPressed;
    public event Action<int, bool>? RecentReleased;
    /// <summary>The light button went down or came up (and whether over it).</summary>
    public event Action? LightPressed;
    public event Action<bool>? LightReleased;
    /// <summary>While dragging: a mouse button was released at a point of the original's 800 × 600 screen.</summary>
    public event Action<MouseButton, Vector2I>? DragReleased;

    internal Vector2 SlotCentreForSelfTest(int entry) => _slots[entry].GetGlobalRect().GetCenter();
    internal Vector2 RecentCentreForSelfTest(int index) => _recent[index].GetGlobalRect().GetCenter();
    internal Texture2D? IconForSelfTest(int entry) => _icons[entry].Visible ? _icons[entry].Texture : null;
    internal string CountForSelfTest(int entry) => _counts[entry].Visible ? _counts[entry].Text : "";
    internal string StandInForSelfTest(int entry) => _standIns[entry].Visible ? _standIns[entry].Text : "";
    internal Texture2D? SlotArtForSelfTest(int entry) => _slots[entry].Texture;
    internal Texture2D? RecentArtForSelfTest(int index) => _recent[index].Texture;
    internal bool GhostShownForSelfTest => _ghost.Visible;

    public override void _Ready()
    {
        Name = "HotkeyBar";
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(AreaGameBarLayout.ScreenWidth, AreaGameBarLayout.BottomHeight);
        for (var i = 0; i < HotkeyBarRules.Entries; i++)
        {
            var entry = i;
            var slot = new TextureRect
            {
                Name = "Slot" + i, Position = new Vector2(HotkeyBarRules.SlotX[i], HotkeyBarRules.SlotTop), Size = new Vector2(HotkeyBarRules.HitWidth, HotkeyBarRules.IconSize),
                MouseFilter = MouseFilterEnum.Stop,
            };
            slot.GuiInput += input => SlotInput(entry, slot, input);
            _slots[i] = slot;
            AddChild(slot);
            _icons[i] = new TextureRect { Name = "Icon" + i, MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Keep, ClipContents = true };
            AddChild(_icons[i]);
            _standIns[i] = new Label
            {
                Name = "Name" + i, Position = slot.Position, Size = slot.Size, MouseFilter = MouseFilterEnum.Ignore, ClipText = true,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.Arbitrary,
            };
            _standIns[i].AddThemeFontSizeOverride("font_size", 8);
            AddChild(_standIns[i]);
            // The count is printed at the slot's left + 2 and the strip's top (HK12).
            _counts[i] = new Label { Name = "Count" + i, Position = new Vector2(HotkeyBarRules.SlotX[i] + 2, 0), MouseFilter = MouseFilterEnum.Ignore };
            _counts[i].AddThemeFontSizeOverride("font_size", 10);
            AddChild(_counts[i]);
        }
        for (var i = 0; i < _recent.Length; i++)
        {
            var index = i;
            var (x, y) = HotkeyBarRules.RecentButtons[i];
            var rect = new TextureRect { Name = "Recent" + i, Position = new Vector2(x, y - HotkeyBarRules.StripTop), Size = new Vector2(32, 32), MouseFilter = MouseFilterEnum.Stop };
            rect.GuiInput += input => ButtonInput(rect, input, () => { _recentHeld = index; RecentPressed?.Invoke(index); }, over => { _recentHeld = null; RecentReleased?.Invoke(index, over); });
            _recent[i] = rect;
            AddChild(rect);
        }
        _light = new TextureRect { Name = "Light", Position = new Vector2(HotkeyBarRules.LightX, HotkeyBarRules.LightY - HotkeyBarRules.StripTop), Size = new Vector2(14, 14), MouseFilter = MouseFilterEnum.Stop };
        _light.GuiInput += input => ButtonInput(_light, input, () => LightPressed?.Invoke(), over => LightReleased?.Invoke(over));
        AddChild(_light);
        _ghost = new TextureRect { Name = "Dragged", MouseFilter = MouseFilterEnum.Ignore, Visible = false, ZIndex = 1 };
        AddChild(_ghost);
        Render();
    }

    /// <summary>Draws <paramref name="look"/>.</summary>
    public void Bind(AreaHotkeyBarLook look)
    {
        _look = look ?? throw new ArgumentNullException(nameof(look));
        if (IsNodeReady()) Render();
    }

    /// <summary>The app started (<paramref name="icon"/> follows the pointer; null none) or ended (<paramref name="dragging"/> false) a drag.</summary>
    public void SetDrag(bool dragging, ImageTexture? icon)
    {
        _dragging = dragging;
        _ghost.Texture = icon;
        _ghost.Visible = dragging && icon is not null;
        if (_ghost.Visible) FollowPointer();
    }

    public override void _Process(double delta)
    {
        if (_ghost.Visible) FollowPointer();
    }

    // The dragged icon is centred on the pointer.
    private void FollowPointer()
    {
        var size = _ghost.Texture?.GetSize() ?? Vector2.Zero;
        _ghost.Size = size;
        _ghost.Position = GetLocalMousePosition() - size / 2;
    }

    public override void _Input(InputEvent input)
    {
        if (!_dragging || !IsVisibleInTree() || input is not InputEventMouseButton { Pressed: false } release) return;
        if (release.ButtonIndex is not (MouseButton.Left or MouseButton.Right)) return;
        // The release's point in the strip's frame, then on the original's screen.
        var local = GetGlobalTransformWithCanvas().AffineInverse() * release.Position;
        var screen = new Vector2I((int)MathF.Floor(local.X), (int)MathF.Floor(local.Y) + HotkeyBarRules.StripTop);
        if (release.ButtonIndex == MouseButton.Right) GetViewport().SetInputAsHandled();
        DragReleased?.Invoke(release.ButtonIndex, screen);
    }

    private void SlotInput(int entry, Control slot, InputEvent input)
    {
        if (input is not InputEventMouseButton click) return;
        if (click.ButtonIndex == MouseButton.Right && click.Pressed) { slot.AcceptEvent(); SlotRightPressed?.Invoke(entry); return; }
        if (click.ButtonIndex != MouseButton.Left) return;
        slot.AcceptEvent();
        if (click.Pressed) { _leftOnSlot = entry; return; }
        // Godot sends the release to the control that took the press; it is over the slot when it lands inside it.
        var over = _leftOnSlot == entry && new Rect2(Vector2.Zero, slot.Size).HasPoint(click.Position);
        _leftOnSlot = null;
        if (over) SlotClicked?.Invoke(entry);
    }

    private void ButtonInput(Control button, InputEvent input, Action press, Action<bool> release)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
        button.AcceptEvent();
        if (click.Pressed) press();
        else release(new Rect2(Vector2.Zero, button.Size).HasPoint(click.Position));
        Render();
    }

    private void Render()
    {
        var look = _look;
        for (var i = 0; i < HotkeyBarRules.Entries; i++)
        {
            var slot = look?.Slots.ElementAtOrDefault(i);
            _slots[i].Texture = look?.Slot;
            var icon = slot is { Hidden: false } ? slot.Icon : null;
            _icons[i].Visible = icon is not null;
            if (icon is not null)
            {
                var place = HotkeyBarRules.IconPlace(i, icon.GetWidth(), icon.GetHeight());
                _icons[i].Texture = icon;
                _icons[i].Position = new Vector2(place.X, place.Y);
                _icons[i].Size = new Vector2(place.Width, place.Height);
            }
            _standIns[i].Visible = slot is { Hidden: false, StandIn: not null } && icon is null;
            _standIns[i].Text = slot?.StandIn ?? "";
            _counts[i].Visible = slot is { Hidden: false, Count: not null };
            _counts[i].Text = slot?.Count ?? "";
        }
        for (var i = 0; i < _recent.Length; i++)
        {
            var frames = look?.Recent.ElementAtOrDefault(i);
            // A button's frames (GB4): idle 0, down one step; the icons' two frames show frame 0 under the pointer.
            _recent[i].Texture = frames is { Length: > 0 } ? frames[AreaGameBarLayout.Frame(_recentHeld == i ? AreaBarButtonLook.Down : AreaBarButtonLook.Idle, frames.Length)] : null;
        }
        _light.Texture = look?.Light;
    }
}
