using Godot;

namespace Arcanum.Experiments.DisplayUiLab;

public partial class DisplayUiLabApp : Node
{
    private Vector2I _savedWindowSize = new(1280, 800);
    private Vector2I _savedWindowPosition;
    private Vector2I _pendingResetSize;
    private Vector2I _pendingResetPosition;
    private int _pendingResetFrames;

    internal bool IsFullscreen => DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
    internal Vector2I SavedWindowSize => _savedWindowSize;
    internal Vector2I SavedWindowPosition => _savedWindowPosition;

    public override void _Ready()
    {
        var window = GetWindow();
        _savedWindowSize = window.Size;
        _savedWindowPosition = window.Position;
    }

    public override void _Process(double delta)
    {
        if (_pendingResetFrames <= 0)
            return;
        var window = GetWindow();
        window.Size = _pendingResetSize;
        window.Position = _pendingResetPosition;
        _pendingResetFrames--;
    }

    public void EnterFullscreen()
    {
        if (IsFullscreen)
            return;
        var window = GetWindow();
        _savedWindowSize = window.Size;
        _savedWindowPosition = window.Position;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
    }

    public void LeaveFullscreen()
    {
        if (!IsFullscreen)
            return;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        CallDeferred(nameof(RestoreSavedWindowBounds));
    }

    public void ToggleFullscreen()
    {
        if (IsFullscreen)
            LeaveFullscreen();
        else
            EnterFullscreen();
    }

    public void ResetWindow()
    {
        if (IsFullscreen)
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        CallDeferred(nameof(ApplyResetWindow));
    }

    private void RestoreSavedWindowBounds()
    {
        var window = GetWindow();
        window.Size = _savedWindowSize;
        window.Position = _savedWindowPosition;
    }

    private void ApplyResetWindow()
    {
        var screen = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        var target = new Vector2I(Mathf.Min(1280, usable.Size.X), Mathf.Min(800, usable.Size.Y));
        var window = GetWindow();
        window.Size = target;
        window.Position = usable.Position + (usable.Size - target) / 2;
        _pendingResetSize = target;
        _pendingResetPosition = window.Position;
        // macOS applies fullscreen/window-mode transitions asynchronously; retry the requested
        // windowed bounds for the next three frames instead of accepting an intermediate state.
        _pendingResetFrames = 3;
        GetNode<LabSurface>("LabSurface").ResetLab();
    }
}
