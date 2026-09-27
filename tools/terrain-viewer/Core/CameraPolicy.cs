namespace Arcanum.TerrainViewer.Core;

public readonly record struct CameraState(double X, double Y, double Zoom, bool FitMode);

public static class CameraPolicy
{
    public const double MinimumZoom = 0.125;
    public const double MaximumZoom = 8.0;
    public const double Margin = 32;
    public const double PanScreenPixelsPerSecond = 600;

    public static CameraState Fit(double worldX, double worldY, double worldWidth, double worldHeight, double viewportWidth, double viewportHeight)
    {
        if (worldWidth <= 0 || worldHeight <= 0 || viewportWidth <= 64 || viewportHeight <= 64) throw new ArgumentOutOfRangeException(nameof(worldWidth));
        var zoom = Math.Clamp(Math.Min((viewportWidth - 64) / worldWidth, (viewportHeight - 64) / worldHeight), MinimumZoom, MaximumZoom);
        zoom = Math.Floor(zoom * 1024d) / 1024d;
        return new CameraState(Math.Round(worldX + (worldWidth / 2d)), Math.Round(worldY + (worldHeight / 2d)), zoom, true);
    }

    public static CameraState Pan(CameraState state, double screenDeltaX, double screenDeltaY) =>
        state with { X = Math.Round(state.X + (screenDeltaX / state.Zoom)), Y = Math.Round(state.Y + (screenDeltaY / state.Zoom)), FitMode = false };

    public static CameraState Zoom(CameraState state, double factor) => state with
    {
        Zoom = Math.Clamp(state.Zoom * factor, MinimumZoom, MaximumZoom),
        FitMode = false
    };
}
