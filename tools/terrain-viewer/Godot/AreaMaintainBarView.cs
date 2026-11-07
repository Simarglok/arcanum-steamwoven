using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <param name="Art">Each art's frames, by interface art number (the slot and plugged arts and the spell icons).</param>
public sealed record AreaMaintainBarLook(IReadOnlyList<AreaMaintainSlotLook> Slots, Func<int, Texture2D[]?> Art);

/// <summary>
/// ARC-MAGIC-026: the original's maintain bar on the top strip (<see cref="AreaMaintainBarLayout"/>, ledger MB1–MB9): five push buttons
/// drawn with their slot art or their run's spell icon (idle frame, down frame while held, GB4), a closed slot with its plugged art
/// over it and no pointer. A press raises <see cref="SlotPressed"/>, the release <see cref="SlotReleased"/> (whether over the slot);
/// the pointer entering or leaving an open slot raises <see cref="SlotEntered"/>/<see cref="SlotLeft"/>. Placed on the top strip in
/// its frame.
/// </summary>
public partial class AreaMaintainBarView : Control
{
    private readonly TextureRect[] _slots = new TextureRect[MaintainBarRules.Slots];
    private readonly TextureRect[] _plugs = new TextureRect[MaintainBarRules.Slots];
    private AreaMaintainBarLook? _look;
    private int? _held;

    public event Action<int>? SlotPressed;
    public event Action<int, bool>? SlotReleased;
    public event Action<int>? SlotEntered, SlotLeft;

    internal Vector2 SlotCentreForSelfTest(int slot) => _slots[slot].GetGlobalRect().GetCenter();
    internal Texture2D? SlotArtForSelfTest(int slot) => _slots[slot].Texture;
    internal bool PluggedForSelfTest(int slot) => _plugs[slot].Visible && _plugs[slot].Texture is not null;

    public override void _Ready()
    {
        Name = "MaintainBar";
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(AreaGameBarLayout.ScreenWidth, AreaGameBarLayout.TopHeight);
        for (var i = 0; i < MaintainBarRules.Slots; i++)
        {
            var slot = i;
            var rect = new TextureRect { Name = "Slot" + i, Position = new Vector2(AreaMaintainBarLayout.SlotX(i), AreaMaintainBarLayout.Y), Size = new Vector2(32, 32) };
            rect.GuiInput += input => SlotInput(slot, rect, input);
            rect.MouseEntered += () => SlotEntered?.Invoke(slot);
            rect.MouseExited += () => SlotLeft?.Invoke(slot);
            _slots[i] = rect;
            AddChild(rect);
            var (px, py) = AreaMaintainBarLayout.PluggedPlace(i);
            _plugs[i] = new TextureRect { Name = "Plugged" + i, Position = new Vector2(px, py), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
            AddChild(_plugs[i]);
        }
        Render();
    }

    /// <summary>Draws <paramref name="look"/>.</summary>
    public void Bind(AreaMaintainBarLook look)
    {
        _look = look ?? throw new ArgumentNullException(nameof(look));
        if (IsNodeReady()) Render();
    }

    private void SlotInput(int slot, Control rect, InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
        rect.AcceptEvent();
        if (click.Pressed) { _held = slot; SlotPressed?.Invoke(slot); }
        else
        {
            // Godot sends the release to the control that took the press; it is over the slot when it lands inside it.
            var over = _held == slot && new Rect2(Vector2.Zero, rect.Size).HasPoint(click.Position);
            _held = null;
            SlotReleased?.Invoke(slot, over);
        }
        Render();
    }

    private void Render()
    {
        for (var i = 0; i < MaintainBarRules.Slots; i++)
        {
            var look = _look?.Slots.ElementAtOrDefault(i);
            var closed = look is null || look.Kind == AreaMaintainSlotKind.Closed;
            var frames = look?.Art is { } art ? _look!.Art(art) : null;
            var texture = frames is { Length: > 0 } ? frames[AreaGameBarLayout.Frame(_held == i ? AreaBarButtonLook.Down : AreaBarButtonLook.Idle, frames.Length)] : null;
            _slots[i].Texture = texture;
            if (texture is not null) _slots[i].Size = texture.GetSize();
            // A closed slot's button is disabled (0x553670): it takes no press, release or hover.
            _slots[i].MouseFilter = closed ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
            var plug = closed && _look?.Art(AreaMaintainBarLayout.PluggedArt(i)) is { Length: > 0 } plugged ? plugged[0] : null;
            _plugs[i].Texture = plug;
            _plugs[i].Visible = plug is not null;
        }
    }
}
