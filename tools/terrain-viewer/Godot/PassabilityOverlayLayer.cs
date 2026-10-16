using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-RENDER-008 read-only passability overlay, drawn in the canvas' own chart space (a cell's projected
/// diamond: top (40*(Y-X), 20*(Y+X)), 80x40). Red fill + outline: every admitted incoming step Blocked.
/// Red boundary/arrow: a Blocked incoming step into a cell that is not wholly blocked. Gray hatch: Unknown
/// (outlined) or outside the authority's coverage. Open: no fill. Visible cells only, a fixed per-frame
/// assessment budget and a visible-cell cap; nothing is assessed or drawn while the overlay is off.
/// </summary>
public partial class PassabilityOverlayLayer : Node2D
{
    public const int VisibleCellCap = 20_000;
    public const int CellsPerFrame = 1024;
    public static readonly Color BlockedFill = new(0.92f, 0.08f, 0.08f, 0.38f);
    public static readonly Color BlockedLine = new(1f, 0.12f, 0.12f, 0.95f);
    public static readonly Color UnknownLine = new(0.72f, 0.72f, 0.72f, 0.9f);
    public static readonly Color OutsideLine = new(0.5f, 0.5f, 0.5f, 0.65f);
    private PassabilityOverlay? _overlay;
    private IReadOnlyList<CellCoord> _visible = Array.Empty<CellCoord>();
    private Rect2 _view;
    private bool _complete;

    public PassabilityOverlay? Overlay => _overlay;
    public IReadOnlyList<CellCoord> VisibleCells => _visible;
    /// <summary>True when the view holds more cells than the cap: nothing is drawn, the HUD asks to zoom in.</summary>
    public bool Capped { get; private set; }
    public long Redraws { get; private set; }
    public int DrawnCells { get; private set; }

    public PassabilityOverlayLayer() { ZIndex = 100; ZAsRelative = false; Name = "PassabilityOverlay"; }

    /// <summary>Binds a fresh overlay (or none). A different authority never inherits cached cells.</summary>
    public void Bind(PassabilityOverlay? overlay)
    {
        _overlay = overlay; _visible = Array.Empty<CellCoord>(); _view = default; Capped = false; DrawnCells = 0; _complete = false;
        Visible = overlay is not null; QueueRedraw();
    }

    /// <summary>Per frame while on: recompute the visible set when the view moved, then assess a bounded batch.</summary>
    public bool Tick(Rect2 viewportRect)
    {
        if (_overlay is null || _overlay.Failure is not null) return false;
        // The AREA pool appends draw items at run time; stay the last sibling so the overlay draws above them.
        if (GetParent() is { } parent && GetIndex() != parent.GetChildCount() - 1) parent.MoveChild(this, -1);
        var inverse = GetGlobalTransformWithCanvas().AffineInverse();
        var view = inverse * viewportRect;
        var changed = false;
        if (view != _view)
        {
            _view = view; changed = true; _complete = false;
            var cells = _overlay.Visible(view.Position.X, view.Position.Y, view.End.X, view.End.Y, VisibleCellCap + 1);
            Capped = cells.Count > VisibleCellCap;
            _visible = Capped ? Array.Empty<CellCoord>() : cells;
        }
        // A fully assessed view is not rescanned until the view or the overlay changes.
        if (!_complete)
        {
            var before = _overlay.CachedCells;
            if (_overlay.Fill(_visible, CellsPerFrame) > 0 || _overlay.CachedCells != before || _overlay.Failure is not null) changed = true;
            else _complete = true;
        }
        if (changed) QueueRedraw();
        return changed;
    }

    public static Vector2 Top(CellCoord cell) => new(40 * (cell.Y - cell.X), 20 * (cell.Y + cell.X));
    public static Vector2[] Diamond(CellCoord cell)
    {
        var t = Top(cell);
        return [t, t + new Vector2(40, 20), t + new Vector2(0, 40), t + new Vector2(-40, 20)];
    }
    public static Vector2 Center(CellCoord cell) => Top(cell) + new Vector2(0, 20);

    public override void _Draw()
    {
        Redraws++; DrawnCells = 0;
        if (_overlay is null || _overlay.Failure is not null || Capped) return;
        foreach (var cell in _visible)
        {
            if (!_overlay.TryGet(cell, out var result)) continue;
            DrawnCells++;
            var d = Diamond(cell);
            switch (result.Kind)
            {
                case OverlayCellKind.Blocked:
                    DrawColoredPolygon(d, BlockedFill);
                    DrawPolyline([d[0], d[1], d[2], d[3], d[0]], BlockedLine, 1.5f);
                    break;
                case OverlayCellKind.Unknown:
                    Hatch(d, UnknownLine); DrawPolyline([d[0], d[1], d[2], d[3], d[0]], UnknownLine, 1f);
                    break;
                case OverlayCellKind.Outside:
                    Hatch(d, OutsideLine);
                    break;
            }
            foreach (var edge in result.BlockedEdges) Edge(cell, edge, BlockedLine, 3f);
            foreach (var edge in result.UnknownEdges) Edge(cell, edge, UnknownLine, 1.5f);
        }
    }

    // Three lines parallel to the left->top edge, between the left->bottom and top->right edges.
    private void Hatch(Vector2[] d, Color color)
    {
        foreach (var t in new[] { .25f, .5f, .75f })
            DrawLine(d[3].Lerp(d[2], t), d[0].Lerp(d[1], t), color, 1f);
    }

    /// <summary>The boundary shared with the source neighbour: an edge for map-cardinal steps, a corner
    /// for map-diagonal steps; an inward arrow marks the direction (from -> to), never both directions.</summary>
    private void Edge(CellCoord cell, OverlayIncoming incoming, Color color, float width)
    {
        var to = Center(cell); var from = Center(incoming.From);
        var boundary = (to + from) / 2; var inward = (to - from).Normalized();
        if (((int)incoming.Direction & 1) == 1)
        {
            // Map-cardinal: the cell's own diamond edge whose midpoint is the shared boundary.
            var d = Diamond(cell);
            var edge = Enumerable.Range(0, 4).MinBy(i => ((d[i] + d[(i + 1) % 4]) / 2).DistanceSquaredTo(boundary));
            DrawLine(d[edge] + inward * 2, d[(edge + 1) % 4] + inward * 2, color, width);
        }
        var tip = boundary + inward * 12; var side = new Vector2(-inward.Y, inward.X) * 4;
        DrawColoredPolygon([tip, boundary + inward * 4 + side, boundary + inward * 4 - side], color);
    }
}
