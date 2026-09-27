using Godot;

namespace Arcanum.Experiments.DisplayUiLab;

public partial class DisplayUiLabApp : Node
{
    private Vector2I _savedWindowSize = new(1280, 800);
    private Vector2I _savedWindowPosition;

    internal bool IsFullscreen => DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;

    public override void _Ready()
    {
        var window = GetWindow();
        _savedWindowSize = window.Size;
        _savedWindowPosition = window.Position;
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
        GetWindow().Size = _savedWindowSize;
        GetWindow().Position = _savedWindowPosition;
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
        LeaveFullscreen();
        var screen = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        var target = new Vector2I(Mathf.Min(1280, usable.Size.X), Mathf.Min(800, usable.Size.Y));
        var window = GetWindow();
        window.Size = target;
        window.Position = usable.Position + (usable.Size - target) / 2;
        GetNode<LabSurface>("LabSurface").ResetLab();
    }
}
