using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>ARC-UI-3059: in the Steamwoven column at <paramref name="size"/> the world is drawn at the whole step the setting gives (automatic: the highest step the scale offers, then each step offered); steps above the cap read as the cap;
    /// the screen-to-world mapping (the canvas transform) has the step's scale, and the out-of-sight window the rules test is the viewport over the step (decision 2026-11-07). Checked only with a shown area
    /// and a camera; the settings change is this run's, restored after.</summary>
    private async Task VerifySteamwovenWorldZoomAsync(string tag, Vector2I size, double scale, Func<int, Task> frames, Action<bool, string> require)
    {
        if (_camera is null || _canvas is null || _readyArea is null || !IsWorld || _readyActorPlan is null)
        {
            GD.Print($"ARC-UI-3059 steamwoven zoom [{tag}] skipped: no shown area");
            return;
        }
        var saved = _gameSettings;
        var seen = new List<string>();
        try
        {
            require(SteamwovenWorldZoomActive, $"[{tag}] the Steamwoven world zoom is in force");
            var steps = SteamwovenWorldZoom.Steps(scale);
            foreach (var setting in new[] { SteamwovenWorldZoom.Automatic }.Concat(steps).Concat([SteamwovenWorldZoom.MaxStep]))
            {
                _gameSettings = _gameSettings with { WorldZoomStep = setting };
                var expected = SteamwovenWorldZoom.Resolve(setting, scale);
                for (var wait = 0; wait < 8; wait++) { SyncSteamwovenWorldZoom(); await frames(1); }
                var zoom = _camera.State.Zoom;
                var floor = _camera.ZoomFloor(new Vector2(size.X, size.Y));
                require(expected <= steps[^1], $"[{tag}] setting {setting} resolves to a step the window offers ({expected})");
                // The window floor (ARC-RENDER-032) may keep a map's zoom above the step on a small map; the step is shown otherwise.
                require(Math.Abs(zoom - Math.Max(expected, floor)) < 1e-9, $"[{tag}] setting {setting}: the camera shows step {expected} (zoom {zoom}, floor {floor})");
                if (zoom != expected) { seen.Add($"{setting}->{expected} floored {zoom}"); continue; }
                var transform = _canvas.GetGlobalTransformWithCanvas();
                require(Math.Abs(transform.X.Length() - expected) < 1e-4 && Math.Abs(transform.Y.Length() - expected) < 1e-4, $"[{tag}] the screen-to-world mapping has the step's scale {expected} ({transform.X.Length()})");
                var window = AreaGameWindow();
                require(window is { } w && Math.Abs(w.Width - size.X / (double)expected) < 1e-3 && Math.Abs(w.Height - size.Y / (double)expected) < 1e-3,
                    $"[{tag}] the out-of-sight window is the drawn view {size.X / (double)expected} x {size.Y / (double)expected} at step {expected} (got {window})");
                // A local capture of each step (ARC_AREA_BAR_CAPTURE=PREFIX: PREFIX-hud-steamwoven-TAG-zoomN.png, never in git), once per step.
                if (setting >= 1 && System.Environment.GetEnvironmentVariable("ARC_AREA_BAR_CAPTURE") is { Length: > 0 }) await CaptureHudAsync($"steamwoven-{tag}-zoom{expected}", frames);
                seen.Add($"{setting}->{expected}");
            }
        }
        finally
        {
            _gameSettings = saved;
            SyncSteamwovenWorldZoom();
            await frames(2);
        }
        GD.Print($"ARC-UI-3059 steamwoven zoom [{tag}] scale={scale:F3} steps=[{string.Join(",", SteamwovenWorldZoom.Steps(scale))}] default={SteamwovenWorldZoom.Default(scale)} checked: {string.Join("; ", seen)}");
    }
}
