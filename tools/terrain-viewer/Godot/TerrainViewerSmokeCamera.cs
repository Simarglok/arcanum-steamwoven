namespace Arcanum.TerrainViewer;

// ARC-RENDER-023: a native sampler that sets the camera and then reads frames holds the window focus simulated off for
// its run. The window's real focus opens the world input gate, and an open gate lets the camera follow return the view
// to the actor a frame or two after a camera reset, so a sample could read a view other than the one it chose (K'na Tha's
// blend and sweep checks failed this way whenever the window happened to be focused).
public partial class TerrainViewerApp
{
    private async Task<T> WithAreaSmokeCameraHeld<T>(Func<Task<T>> run)
    {
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = false;
        try { return await run(); }
        finally { _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused; }
    }
}
