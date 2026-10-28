using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-034: the terrain overview beyond a window's core, bound at each publication of a map loaded as a window.
public partial class TerrainViewerApp
{
    private AreaOverviewLayer? _overviewLayer;
    private AreaTerrainOverview? _mapOverview;
    private string? _mapOverviewFailure;

    // A cleared scene (a load starting, a failure) shows no overview of the map it left.
    private void RetireAreaOverview()
    {
        _mapOverview = null; _mapOverviewFailure = null;
        if (_overviewLayer is not null && GodotObject.IsInstanceValid(_overviewLayer)) _overviewLayer.Bind(null);
    }

    private void BindAreaOverview(TerrainLoadResult result)
    {
        _mapOverview = _mapWindowCentre is null ? null : result.AreaOverview;
        _mapOverviewFailure = _mapWindowCentre is null ? null : result.AreaOverviewFailure;
        if (_canvas is null) return;
        if (_overviewLayer is null || !GodotObject.IsInstanceValid(_overviewLayer))
        {
            _overviewLayer = new AreaOverviewLayer(); _canvas.AddChild(_overviewLayer); _canvas.MoveChild(_overviewLayer, 0);
        }
        _overviewLayer.Bind(_mapOverview);
        if (_mapOverviewFailure is not null) GD.Print("ARC-RENDER-034 overview unavailable " + _mapOverviewFailure);
    }
}
