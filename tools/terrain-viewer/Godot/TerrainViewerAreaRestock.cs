using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-096: the merchant restock in the AREA viewer (ledger RK1–RK9, contract docs/world/barter-runtime.md). Every inventory window
// open schedules the shown object's restock event (the barter window its substitute container or its merchant, the loot window the looted
// holder); the event world answers the session's restock questions through AreaRestockHost: the screen test against what the viewer
// shows, the merchant search in the window-sized area around a container, the persistent flag and the inventory sources.
public partial class TerrainViewerApp
{
    private AreaRestockHost? _restockHost;

    /// <summary>The restock host of the area session now: once the barter data (inventory sources, item prototypes) loaded, over the
    /// shown session and inventory host; null before (a due restock event then waits a minute).</summary>
    private AreaRestockHost? RestockHostNow()
    {
        if (_areaSession is not { } session || _inventoryHost is not { } inventory) return null;
        AdoptBarterData();
        if (_barterData is not { Data: { } data }) return null;
        if (_restockHost is { } host && ReferenceEquals(host.Session, session) && ReferenceEquals(host.Inventory, inventory) && ReferenceEquals(host.Data, data))
            return host;
        return _restockHost = new AreaRestockHost(session, inventory, data, new RestockScreen(this));
    }

    /// <summary>The restock schedule of a window open on <paramref name="shown"/> (0x5733A9): logged; nothing for an object that is no
    /// seed holder of the shown map.</summary>
    private void ScheduleRestock(string shown, string window)
    {
        if (_areaSession is not { } session || _inventoryHost is not { } inventory) return;
        if (AreaRestockHost.Schedule(session, inventory, shown) is not { } result) return;
        var pending = session.QueryRestocks().Where(e => e.Holder == shown).Select(e => (long?)e.DeadlineMs).FirstOrDefault();
        GD.Print($"ARC-WORLD-096 restock schedule window={window} holder={shown} disposition={result.Disposition} failure={result.Failure} " +
            $"due-in-ms={(pending is { } at ? at - session.QueryClockSnapshot().GameTimestampMs : -1)}");
    }

    /// <summary>The game window in area pixels: the viewport's visible rectangle through the canvas transform (what the viewer shows,
    /// at its zoom), or null without a shown area.</summary>
    private AreaPixelRect? AreaGameWindow()
    {
        if (_readyArea is null || _canvas is null) return null;
        var view = GetViewport().GetVisibleRect();
        var inverse = _canvas.GetGlobalTransformWithCanvas().AffineInverse();
        var rect = new Rect2(inverse * view.Position, inverse * view.End - inverse * view.Position).Abs();
        return new(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y);
    }

    private sealed class RestockScreen(TerrainViewerApp app) : IAreaRestockScreen
    {
        public int? ShownMap => app._readyArea is null ? null : app.CurrentAreaMap;
        public AreaPixelRect? GameWindow => app.AreaGameWindow();
        public (double Width, double Height) WindowSize => app.AreaGameWindow() is { } window ? (window.Width, window.Height)
            : (app.GetViewport().GetVisibleRect().Size.X, app.GetViewport().GetVisibleRect().Size.Y);
        public AreaPixelRect? BodyRect(AreaObjectKey key) =>
            app._canvas?.AreaBodyRect(key) is { } rect ? new(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y) : null;
    }
}
