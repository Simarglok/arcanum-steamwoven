using Godot;

namespace Arcanum.Experiments.DisplayUiLab;

internal static class DiagnosticOverlay
{
    internal static string Describe(Viewport viewport, DisplayState state)
    {
        var window = viewport.GetWindow();
        var screen = DisplayServer.WindowGetCurrentScreen();
        var screenUsable = DisplayServer.ScreenGetUsableRect(screen);
        var contentUsable = viewport.GetVisibleRect();
        var screenSize = DisplayServer.ScreenGetSize(screen);
        return $"Viewport visible rect: {viewport.GetVisibleRect()}\n" +
               $"Window.Size: {window.Size}; client size: {DisplayServer.WindowGetSize()}\n" +
               $"Window mode: {DisplayServer.WindowGetMode()}; usable content rect: {contentUsable}\n" +
               $"UI scale: {(int)state.UiScale}% ; camera zoom: {state.CameraZoom:0.00}\n" +
               $"Content scale mode: {window.ContentScaleMode}; size: {window.ContentScaleSize}; factor: {window.ContentScaleFactor:0.00}\n" +
               $"Screen size: {screenSize}; screen usable rect: {screenUsable}; reported scale: {DisplayServer.ScreenGetScale(screen):0.00}; reported DPI: {DisplayServer.ScreenGetDpi(screen):0.0}\n" +
               "reported by Godot; physical pixel mapping/readability not verified";
    }
}
