using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-1200, ARC-UI-3143: the opt-in window-art part (ARC_AREA_WINDOW_ART=legacy|hd, see WindowArtSmokeAsync) for a pane of the bottom strip: the message window (354), the spell
    // pane (8) and the skill pane (274): the Legacy art, or the pack's redraw when the HD interface is on.
    private Task PaneArtSmokeAsync(string window, Func<Texture2D?> background, int art) =>
        WindowArtSmokeAsync(window, () => background() is HdInterfaceTexture texture ? [texture] : [], [art], () => 1f, () => background() is not null, new Vector2I(1600, 1200));

    // ARC-UI-3143: the window-art smoke no longer checks a painted scale (the generated chrome was painted at the bars' whole device scale); the callers' scale argument is kept and unused.
    private static float BarDeviceScale() => 1f;
}
