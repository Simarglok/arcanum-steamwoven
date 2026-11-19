using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-UI-2771: the viewer's fullscreen switch for its window. Fullscreen is the platform's kind (<see cref="ViewerFullscreen"/>: the
/// exclusive mode on macOS, which keeps the Dock and the menu bar hidden); either kind counts as fullscreen when asked.</summary>
public static class ViewerWindowMode
{
    private static Window.ModeEnum FullscreenMode => ViewerFullscreen.UsesExclusiveMode(OS.GetName()) ? Window.ModeEnum.ExclusiveFullscreen : Window.ModeEnum.Fullscreen;

    public static bool IsFullscreen(Window window) => window.Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen;

    /// <summary>Fullscreen on or off; a window already in the wanted state is left alone.</summary>
    public static void Set(Window window, bool fullscreen)
    {
        if (IsFullscreen(window) == fullscreen) return;
        window.Mode = fullscreen ? FullscreenMode : Window.ModeEnum.Windowed;
    }

    /// <summary>F11: the other state; returns whether the window is fullscreen now.</summary>
    public static bool Toggle(Window window)
    {
        var fullscreen = !IsFullscreen(window);
        Set(window, fullscreen);
        return fullscreen;
    }
}
