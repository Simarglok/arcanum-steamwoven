using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-RENDER-034: the terrain overview of a map loaded as a window, drawn behind everything else in the canvas' chart space:
/// one flat diamond per overview sector in its terrain type's colour (<see cref="AreaTerrainOverview"/>). Redrawn only when a
/// publication binds a new overview; nothing is drawn without one.
/// </summary>
public partial class AreaOverviewLayer : Node2D
{
    private AreaTerrainOverview? _overview;

    public AreaOverviewLayer() { ZIndex = (int)RenderingServer.CanvasItemZMin; ZAsRelative = false; Name = "AreaTerrainOverview"; }

    public AreaTerrainOverview? Overview => _overview;
    /// <summary>Redraws since the layer was made (one per bound overview).</summary>
    public long Redraws { get; private set; }

    public void Bind(AreaTerrainOverview? overview)
    {
        _overview = overview; Visible = overview is not null; QueueRedraw();
    }

    public override void _Draw()
    {
        if (_overview is not { } overview) return;
        Redraws++;
        foreach (var sector in overview.Sectors)
        {
            var points = sector.Corners.Select(c => new Vector2((float)c.X, (float)c.Y)).ToArray();
            DrawColoredPolygon(points, new Color(sector.Colour.R / 255f, sector.Colour.G / 255f, sector.Colour.B / 255f));
        }
    }
}
