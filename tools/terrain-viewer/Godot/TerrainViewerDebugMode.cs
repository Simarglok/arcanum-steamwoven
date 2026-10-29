using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-050: the run's debug mode (ViewerDebugMode): the --debug-mode user argument or the launcher's stored setting turns it on
// before the first load, and it stays on for the run. It shows the world map's area list and teleport, marks saves and the build
// line, shows the diagnostic panel (ARC-DEV-052; hidden by default otherwise) and the actor's frame and anchor cross (ARC-DEV-064)
// and changes nothing else.
public partial class TerrainViewerApp
{
    /// <summary>The debug mode is on for this run.</summary>
    internal bool DebugMode { get; private set; }

    private void EnableDebugMode(string source)
    {
        if (DebugMode) return;
        DebugMode = true;
        GD.Print($"ARC-DEV-050 debug mode on source={source}");
        _hud?.ShowPanel("debug mode");
        if (_canvas is not null) _canvas.ShowActorGuides = true;
    }

    /// <summary>ARC-DEV-064: a self-test raster oracle that checks the actor's guides turns them on without the debug mode.</summary>
    internal void ShowActorGuidesForSelfTest()
    {
        if (!SelfTestBootstrap) throw new InvalidOperationException("Actor guides without the debug mode are limited to the self-test bootstrap.");
        _canvas!.ShowActorGuides = true;
    }
}
