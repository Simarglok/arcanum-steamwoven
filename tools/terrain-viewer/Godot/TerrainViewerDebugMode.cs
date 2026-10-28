using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-050: the run's debug mode (ViewerDebugMode): the --debug-mode user argument or the launcher's stored setting turns it on
// before the first load, and it stays on for the run. It shows the world map's area list and teleport, marks saves and the build
// line, and changes nothing else.
public partial class TerrainViewerApp
{
    /// <summary>The debug mode is on for this run.</summary>
    internal bool DebugMode { get; private set; }

    private void EnableDebugMode(string source)
    {
        if (DebugMode) return;
        DebugMode = true;
        GD.Print($"ARC-DEV-050 debug mode on source={source}");
    }
}
