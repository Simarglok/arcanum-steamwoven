using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-1200: the opt-in generated-chrome part (ARC_AREA_WINDOW_ART=legacy|hd|generated, see WindowArtSmokeAsync) for a pane of the bottom strip: the message window (354), the spell
    // pane (8) and the skill pane (274) draw their one background texture at the strip's own device scale (the bars' whole factor times the viewport's stretch; no pane claims a scale of its own).
    private Task PaneArtSmokeAsync(string window, Func<Texture2D?> background, int art) =>
        WindowArtSmokeAsync(window, () => background() is HdInterfaceTexture texture ? [texture] : [], [art], BarDeviceScale, () => background() is not null, new Vector2I(1600, 1200));

    // The strip's textures claim no scale source, so they paint at the whole scale the bars set (InterfaceHd.SetGeneratedScale rounds and clamps the bars' device scale).
    private static float BarDeviceScale() => InterfaceHd.GeneratedScale;
}
