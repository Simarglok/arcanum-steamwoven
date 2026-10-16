using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-RENDER-008 read-only passability overlay, drawn in the canvas' own chart space (a cell's projected
/// diamond: top (originX + 40*(Y-X), 20*(Y+X)), 80x40; originX 40 on the AREA chart, ARC-RENDER-011). Red fill + outline: every admitted incoming step Blocked.
/// Red boundary/arrow: a Blocked incoming step into a cell that is not wholly blocked. Gray hatch: Unknown
/// (outlined) or outside the authority's coverage. Open: no fill. Visible cells only, a fixed per-frame
/// assessment budget and a visible-cell cap; nothing is assessed or drawn while the overlay is off.
/// The layer moves with the canvas, so it is redrawn only when its drawn cells change: the view leaves the
/// drawn region (the view grown by <see cref="DrawMargin"/>) or shrinks under 1/16 of it, new cells are
/// assessed, the authority fails, or the cap flips.
/// Each redraw submits a few batched commands (fills, one multiline per colour/width, then arrows), not per-cell ones.
/// </summary>
public partial class PassabilityOverlayLayer : Node2D
{
    public const int VisibleCellCap = 20_000;
    public const int CellsPerFrame = 1024;
    /// <summary>Drawn region margin on each side, as a fraction of the larger view side.</summary>
    public const float DrawMargin = 0.25f;
    /// <summary>Most cells drawn from the grown region; beyond it only the view's own cells are drawn.</summary>
    public const int DrawnCellCap = 3 * VisibleCellCap;
    public static readonly Color BlockedFill = new(0.92f, 0.08f, 0.08f, 0.38f);
    public static readonly Color BlockedLine = new(1f, 0.12f, 0.12f, 0.95f);
    public static readonly Color UnknownLine = new(0.72f, 0.72f, 0.72f, 0.9f);
    public static readonly Color OutsideLine = new(0.5f, 0.5f, 0.5f, 0.65f);
    private PassabilityOverlay? _overlay;
    private IReadOnlyList<CellCoord> _visible = Array.Empty<CellCoord>();
    private Rect2 _view;
    private bool _complete;
    private Rect2 _drawRect;
    private IReadOnlyList<CellCoord> _drawn = Array.Empty<CellCoord>();

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
        _drawRect = default; _drawn = Array.Empty<CellCoord>();
        Visible = overlay is not null; QueueRedraw();
    }

    /// <summary>Per frame while on: recompute the visible set when the view moved, then assess a bounded batch.
    /// Returns whether the visible set or its assessment changed; a redraw is queued only when the drawn cells do.</summary>
    public bool Tick(Rect2 viewportRect)
    {
        if (_overlay is null || _overlay.Failure is not null) return false;
        // The AREA pool appends draw items at run time; stay the last sibling so the overlay draws above them.
        if (GetParent() is { } parent && GetIndex() != parent.GetChildCount() - 1) parent.MoveChild(this, -1);
        var inverse = GetGlobalTransformWithCanvas().AffineInverse();
        var view = inverse * viewportRect;
        var changed = false; var redraw = false;
        if (view != _view)
        {
            _view = view; changed = true; _complete = false;
            var cells = _overlay.Visible(view.Position.X, view.Position.Y, view.End.X, view.End.Y, VisibleCellCap + 1);
            var capped = cells.Count > VisibleCellCap;
            if (capped != Capped) redraw = true;
            Capped = capped;
            _visible = Capped ? Array.Empty<CellCoord>() : cells;
            if (Capped) { _drawRect = default; _drawn = Array.Empty<CellCoord>(); }
            // Recomputed when the view leaves the drawn region or shrinks far inside it (zoom in).
            else if (!_drawRect.Encloses(view) || view.Area * 16 < _drawRect.Area)
            {
                // Near the cap a quarter margin could exceed DrawnCellCap; a tenth stays well under it.
                var margin = _visible.Count * 4 <= DrawnCellCap ? DrawMargin : DrawMargin / 2.5f;
                var grown = view.Grow(margin * Math.Max(view.Size.X, view.Size.Y));
                var drawn = _overlay.Visible(grown.Position.X, grown.Position.Y, grown.End.X, grown.End.Y, DrawnCellCap + 1);
                if (drawn.Count > DrawnCellCap) { grown = view; drawn = _visible; }
                _drawRect = grown; _drawn = drawn; redraw = true;
            }
        }
        // A fully assessed view is not rescanned until the view or the overlay changes.
        if (!_complete)
        {
            var before = _overlay.CachedCells;
            if (_overlay.Fill(_visible, CellsPerFrame) > 0 || _overlay.CachedCells != before || _overlay.Failure is not null) changed = redraw = true;
            else _complete = true;
        }
        if (redraw) QueueRedraw();
        return changed;
    }

    public static Vector2 Top(CellCoord cell, int originX = 0) => new(originX + 40 * (cell.Y - cell.X), 20 * (cell.Y + cell.X));
    public static Vector2[] Diamond(CellCoord cell, int originX = 0)
    {
        var t = Top(cell, originX);
        return [t, t + new Vector2(40, 20), t + new Vector2(0, 40), t + new Vector2(-40, 20)];
    }
    public static Vector2 Center(CellCoord cell, int originX = 0) => Top(cell, originX) + new Vector2(0, 20);

    public override void _Draw()
    {
        Redraws++; DrawnCells = 0;
        if (_overlay is null || _overlay.Failure is not null || Capped) return;
        var fills = new List<Vector2>(); var fillColors = new List<Color>();
        var arrows = new List<Vector2>(); var arrowColors = new List<Color>();
        // Fixed line order: gray hatches and outlines below red outlines and edges.
        var lines = new (Color Color, float Width, List<Vector2> Points)[]
        {
            (OutsideLine, 1f, new()), (UnknownLine, 1f, new()), (UnknownLine, 1.5f, new()), (BlockedLine, 1.5f, new()), (BlockedLine, 3f, new()),
        };
        void Outline(Vector2[] d, List<Vector2> into) { for (var i = 0; i < 4; i++) { into.Add(d[i]); into.Add(d[(i + 1) % 4]); } }
        foreach (var cell in _drawn)
        {
            if (!_overlay.TryGet(cell, out var result)) continue;
            DrawnCells++;
            var d = Diamond(cell, _overlay.OriginX);
            switch (result.Kind)
            {
                case OverlayCellKind.Blocked:
                    fills.AddRange([d[0], d[1], d[2], d[0], d[2], d[3]]);
                    for (var i = 0; i < 6; i++) fillColors.Add(BlockedFill);
                    Outline(d, lines[3].Points);
                    break;
                case OverlayCellKind.Unknown:
                    Hatch(d, lines[1].Points); Outline(d, lines[1].Points);
                    break;
                case OverlayCellKind.Outside:
                    Hatch(d, lines[0].Points);
                    break;
            }
            foreach (var edge in result.UnknownEdges) Edge(cell, edge, UnknownLine, lines[2].Points, arrows, arrowColors);
            foreach (var edge in result.BlockedEdges) Edge(cell, edge, BlockedLine, lines[4].Points, arrows, arrowColors);
        }
        var item = GetCanvasItem();
        if (fills.Count > 0) RenderingServer.CanvasItemAddTriangleArray(item, Enumerable.Range(0, fills.Count).ToArray(), fills.ToArray(), fillColors.ToArray());
        foreach (var (color, width, points) in lines) if (points.Count > 0) DrawMultiline(points.ToArray(), color, width);
        if (arrows.Count > 0) RenderingServer.CanvasItemAddTriangleArray(item, Enumerable.Range(0, arrows.Count).ToArray(), arrows.ToArray(), arrowColors.ToArray());
    }

    // Three lines parallel to the left->top edge, between the left->bottom and top->right edges.
    private static void Hatch(Vector2[] d, List<Vector2> into)
    {
        foreach (var t in new[] { .25f, .5f, .75f }) { into.Add(d[3].Lerp(d[2], t)); into.Add(d[0].Lerp(d[1], t)); }
    }

    /// <summary>The boundary shared with the source neighbour: an edge for map-cardinal steps, a corner
    /// for map-diagonal steps; an inward arrow marks the direction (from -> to), never both directions.</summary>
    private static void Edge(CellCoord cell, OverlayIncoming incoming, Color color, List<Vector2> line, List<Vector2> arrows, List<Color> arrowColors)
    {
        var origin = _overlay!.OriginX;
        var to = Center(cell, origin); var from = Center(incoming.From, origin);
        var boundary = (to + from) / 2; var inward = (to - from).Normalized();
        if (((int)incoming.Direction & 1) == 1)
        {
            // Map-cardinal: the cell's own diamond edge whose midpoint is the shared boundary.
            var d = Diamond(cell, origin);
            var edge = Enumerable.Range(0, 4).MinBy(i => ((d[i] + d[(i + 1) % 4]) / 2).DistanceSquaredTo(boundary));
            line.Add(d[edge] + inward * 2); line.Add(d[(edge + 1) % 4] + inward * 2);
        }
        var tip = boundary + inward * 12; var side = new Vector2(-inward.Y, inward.X) * 4;
        arrows.AddRange([tip, boundary + inward * 4 + side, boundary + inward * 4 - side]);
        arrowColors.AddRange([color, color, color]);
    }
}
