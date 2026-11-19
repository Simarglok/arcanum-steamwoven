namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2771 (FR-UI-003): which Godot window mode "fullscreen" means on a platform. On macOS Godot's plain <c>Fullscreen</c> is the system's
/// full-screen space, where the Dock and the menu bar slide in when the pointer touches the screen edge; the exclusive mode sets the
/// application's presentation options to hide both for good. Elsewhere the plain mode is kept (exclusive fullscreen on Windows takes the
/// display over, which alt-tab and overlays do not like). Godot-free, so the choice is checked as plain values.
/// </summary>
public static class ViewerFullscreen
{
    /// <summary>The name Godot's <c>OS.GetName()</c> gives macOS.</summary>
    public const string MacOsName = "macOS";

    /// <summary>True where fullscreen must be the exclusive mode so that the Dock and the menu bar stay hidden.</summary>
    public static bool UsesExclusiveMode(string? osName) => string.Equals(osName, MacOsName, StringComparison.Ordinal);
}
