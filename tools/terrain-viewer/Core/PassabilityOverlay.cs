using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One directed step verdict as the movement authority returns it.</summary>
public enum OverlayStepVerdict { Open, Blocked, Unknown, Unsupported }

/// <summary>A directed production assessment, reduced to verdict and the assessor's own reason code.</summary>
public readonly record struct OverlayStep(OverlayStepVerdict Verdict, string Reason);

/// <summary>
/// The read-only step authority the overlay displays: exactly the production assessor the WORLD/AREA
/// movement session uses, with its snapshot, actor and context. It is not a permit and never advances state.
/// </summary>
public interface IPassabilityAuthority
{
    /// <summary>Cache identity: profile, evidence digest, epoch, actor/context fingerprint.</summary>
    string Identity { get; }
    string ProfileId { get; }
    string ActorId { get; }
    string CoverageDescription { get; }
    /// <summary>The movement session whose assessor this is; a replaced session gets a new overlay.</summary>
    object Owner { get; }
    /// <summary>Inclusive chart cell range the authority can address; cells and neighbours outside it are not requested.</summary>
    (int MinX, int MinY, int MaxX, int MaxY) Chart { get; }
    /// <summary>Inclusive cell range the scene draws; cells in it but outside <see cref="Chart"/> are Outside without a request.</summary>
    (int MinX, int MinY, int MaxX, int MaxY) Envelope { get; }
    OverlayStep Assess(CellCoord from, CellCoord to, NeighborDirection direction);
}

/// <summary>Whole-map T0 authority of <see cref="AreaGridSession"/> (crash-site-area exterior or the cave): the
/// assessable chart is the bounding rectangle of the snapshot's admitted sectors in this session's chart.</summary>
public sealed class AreaPassabilityAuthority : IPassabilityAuthority
{
    private readonly AreaGridSession _session;
    public AreaPassabilityAuthority(AreaGridSession session)
    {
        _session = session;
        Identity = $"{session.Snapshot.ProfileId}|{session.Snapshot.EvidenceSha256}|{session.EvidenceEpoch}|{session.State.Binding.ContextId}|{session.Chart.BindingName}";
        var sectors = session.Snapshot.AdmittedSectors;
        int Min(Func<(long X, long Y), long> axis, long origin) => checked((int)(sectors.Min(axis) * 64 - origin));
        int Max(Func<(long X, long Y), long> axis, long origin) => checked((int)(sectors.Max(axis) * 64 + 63 - origin));
        Chart = (Min(s => s.X, session.Chart.OriginX), Min(s => s.Y, session.Chart.OriginY), Max(s => s.X, session.Chart.OriginX), Max(s => s.Y, session.Chart.OriginY));
        // The drawn context ring is one sector around the admitted sectors.
        Envelope = (Chart.MinX - 64, Chart.MinY - 64, Chart.MaxX + 64, Chart.MaxY + 64);
        CoverageDescription = $"{sectors.Count} admitted sectors of this map at new-game T0 ({session.Chart.BindingName}); outside them every step is Unsupported";
    }
    public string Identity { get; }
    public string ProfileId => _session.Snapshot.ProfileId;
    public string ActorId => _session.State.Binding.ActorId;
    public string CoverageDescription { get; }
    public object Owner => _session;
    public (int MinX, int MinY, int MaxX, int MaxY) Chart { get; }
    public (int MinX, int MinY, int MaxX, int MaxY) Envelope { get; }
    public OverlayStep Assess(CellCoord from, CellCoord to, NeighborDirection direction)
    {
        var assessment = _session.Assess(from, to, direction);
        return new(PassabilityOverlay.Verdict(assessment.Coverage, assessment.Disposition), assessment.ReasonCode);
    }
}

/// <summary>Bounded WORLD003 authority of <see cref="DiagnosticGridSession"/> (world-diagnostic).</summary>
public sealed class CardinalPassabilityAuthority(DiagnosticGridSession session) : IPassabilityAuthority
{
    public string Identity { get; } = $"{session.Snapshot.ProfileId}|{session.Snapshot.InputIdentity.SelectedEvidenceSha256}|{session.EvidenceEpoch}|{session.State.Binding.ContextId}";
    public string ProfileId => session.Snapshot.ProfileId;
    public string ActorId => session.State.Binding.ActorId;
    public string CoverageDescription => "bounded zone x4..8 y0..3 of sector 1452,1290; outside it every step is Unsupported";
    public object Owner => session;
    public (int MinX, int MinY, int MaxX, int MaxY) Chart => (0, 0, 63, 63);
    public (int MinX, int MinY, int MaxX, int MaxY) Envelope => (0, 0, 63, 63);
    public OverlayStep Assess(CellCoord from, CellCoord to, NeighborDirection direction)
    {
        var assessment = session.Assess(from, to, direction);
        return new(PassabilityOverlay.Verdict(assessment.Coverage, assessment.Disposition), assessment.ReasonCode);
    }
}

public enum OverlayCellKind
{
    /// <summary>At least one admitted incoming step is Open: no fill (edges may still be marked).</summary>
    Open,
    /// <summary>Every admitted incoming step is Blocked and at least one of them is map-cardinal: a proven
    /// blocked-cell fill. (In both movement assessors a map-cardinal step is Blocked only by the destination
    /// cell itself; diagonal-only blocks can come from shoulders and stay directed edges.)</summary>
    Blocked,
    /// <summary>No admitted incoming step is Open and either one is Unknown, or every admitted one is a
    /// Blocked map diagonal (no cardinal proof of a destination blocker).</summary>
    Unknown,
    /// <summary>No admitted incoming step: outside the authority's coverage (Unsupported).</summary>
    Outside,
}

/// <summary>One of the eight incoming directed steps into a cell: from the neighbour, in that direction.</summary>
public readonly record struct OverlayIncoming(NeighborDirection Direction, CellCoord From, OverlayStep Step);

/// <summary>A cell's classification from all eight incoming production verdicts. Directed marks
/// keep edge-only facts: a Blocked step into a cell that is not wholly blocked, or an Unknown step into an Open cell.</summary>
public sealed record OverlayCell(CellCoord Cell, OverlayCellKind Kind, IReadOnlyList<OverlayIncoming> Incoming)
{
    public IEnumerable<OverlayIncoming> BlockedEdges => Kind == OverlayCellKind.Blocked ? [] : Incoming.Where(i => i.Step.Verdict == OverlayStepVerdict.Blocked);
    public IEnumerable<OverlayIncoming> UnknownEdges => Kind == OverlayCellKind.Open ? Incoming.Where(i => i.Step.Verdict == OverlayStepVerdict.Unknown) : [];
}

public readonly record struct OverlayCounts(int Open, int Blocked, int Unknown, int Outside, int BlockedEdges, int UnknownEdges);

/// <summary>
/// Read-only passability overlay over one authority. Cells are assessed lazily (visible cells only, a fixed
/// per-call budget, deterministic order) and cached for exactly this authority identity; a new snapshot,
/// epoch or context needs a new instance. It never mutates movement, camera or input state.
/// </summary>
public sealed class PassabilityOverlay
{
    // Same order as NeighborDirection: the delta FROM the source TO the destination.
    private static readonly (int Dx, int Dy)[] Deltas = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
    private static readonly IReadOnlyList<OverlayIncoming> NoRequest = Array.AsReadOnly(Array.Empty<OverlayIncoming>());
    private readonly Dictionary<CellCoord, OverlayCell> _cells = new();
    public PassabilityOverlay(IPassabilityAuthority authority, int maxCells = 120_000, int originX = 0)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (maxCells <= 0) throw new ArgumentOutOfRangeException(nameof(maxCells));
        Authority = authority; MaxCells = maxCells; Identity = authority.Identity; OriginX = originX;
    }
    /// <summary>Horizontal origin of the drawn diamonds: 0 keeps the retained WORLD projection; the AREA chart passes
    /// <see cref="AreaGridSession.LocationOriginX"/> (40) so each diamond is the terrain tile the actor stands on
    /// (ARC-RENDER-011).</summary>
    public int OriginX { get; }
    public IPassabilityAuthority Authority { get; }
    public string Identity { get; }
    public int MaxCells { get; }
    public int CachedCells => _cells.Count;
    /// <summary>Production assessor invocations made by the overlay (zero while it is off).</summary>
    public long AssessCalls { get; private set; }
    /// <summary>Set when the authority failed: the overlay stops and never presents stale or partial cells as Unknown.</summary>
    public string? Failure { get; private set; }

    public static OverlayStepVerdict Verdict(CardinalTraversalCoverage coverage, CardinalTraversalDisposition? disposition) => coverage switch
    {
        CardinalTraversalCoverage.Supported when disposition == CardinalTraversalDisposition.Open => OverlayStepVerdict.Open,
        CardinalTraversalCoverage.Supported when disposition == CardinalTraversalDisposition.Blocked => OverlayStepVerdict.Blocked,
        CardinalTraversalCoverage.Unknown => OverlayStepVerdict.Unknown,
        CardinalTraversalCoverage.Unsupported => OverlayStepVerdict.Unsupported,
        _ => throw new InvalidOperationException("OVERLAY_VERDICT_INVALID"),
    };

    public static CellCoord Source(CellCoord destination, NeighborDirection direction)
        => new(destination.X - Deltas[(int)direction].Dx, destination.Y - Deltas[(int)direction].Dy);

    public static OverlayCellKind Classify(IReadOnlyList<OverlayIncoming> incoming)
    {
        var admitted = incoming.Where(i => i.Step.Verdict != OverlayStepVerdict.Unsupported).ToArray();
        if (admitted.Length == 0) return OverlayCellKind.Outside;
        if (admitted.All(i => i.Step.Verdict == OverlayStepVerdict.Blocked) && admitted.Any(i => ((int)i.Direction & 1) == 1))
            return OverlayCellKind.Blocked;
        if (admitted.Any(i => i.Step.Verdict == OverlayStepVerdict.Open)) return OverlayCellKind.Open;
        return OverlayCellKind.Unknown;
    }

    public bool TryGet(CellCoord cell, out OverlayCell result) => _cells.TryGetValue(cell, out result!);

    /// <summary>Assesses up to <paramref name="budget"/> uncached cells of <paramref name="cells"/> in the given
    /// order and returns how many were computed. Cells outside the chart are recorded Outside without a request.</summary>
    public int Fill(IEnumerable<CellCoord> cells, int budget)
    {
        if (Failure is not null || budget <= 0) return 0;
        var computed = 0;
        foreach (var cell in cells)
        {
            if (computed >= budget || _cells.Count >= MaxCells) break;
            if (_cells.ContainsKey(cell)) continue;
            if (!InChart(cell)) { _cells[cell] = new(cell, OverlayCellKind.Outside, NoRequest); continue; }
            var incoming = new OverlayIncoming[8];
            try
            {
                for (var d = 0; d < 8; d++)
                {
                    var direction = (NeighborDirection)d; var from = Source(cell, direction);
                    var step = InChart(from)
                        ? Count(Authority.Assess(from, cell, direction))
                        : new OverlayStep(OverlayStepVerdict.Unsupported, "OVERLAY_OUTSIDE_CHART");
                    incoming[d] = new(direction, from, step);
                }
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or KeyNotFoundException or IndexOutOfRangeException)
            {
                // An authority failure is not successful Unknown evidence.
                Failure = "OVERLAY_AUTHORITY_FAILED"; _cells.Clear(); return computed;
            }
            _cells[cell] = new(cell, Classify(incoming), Array.AsReadOnly(incoming));
            computed++;
        }
        return computed;
    }

    public OverlayCounts Count(IEnumerable<CellCoord> cells)
    {
        int open = 0, blocked = 0, unknown = 0, outside = 0, blockedEdges = 0, unknownEdges = 0;
        foreach (var cell in cells)
        {
            if (!_cells.TryGetValue(cell, out var result)) continue;
            switch (result.Kind)
            {
                case OverlayCellKind.Open: open++; break;
                case OverlayCellKind.Blocked: blocked++; break;
                case OverlayCellKind.Unknown: unknown++; break;
                default: outside++; break;
            }
            blockedEdges += result.BlockedEdges.Count(); unknownEdges += result.UnknownEdges.Count();
        }
        return new(open, blocked, unknown, outside, blockedEdges, unknownEdges);
    }

    /// <summary>Chart cells whose projected diamond can intersect the canvas rectangle, row-major, at most <paramref name="cap"/>.
    /// Projection is the chart's own: a cell's top vertex is (OriginX + 40*(Y-X), 20*(Y+X)), its diamond 80x40.</summary>
    public IReadOnlyList<CellCoord> Visible(double left, double top, double right, double bottom, int cap)
    {
        var (minX, minY, maxX, maxY) = Authority.Envelope;
        var originX = OriginX;
        (double X, double Y) Cell(double px, double py) => ((py / 20 - (px - originX) / 40) / 2, (py / 20 + (px - originX) / 40) / 2);
        var corners = new[] { Cell(left, top), Cell(right, top), Cell(left, bottom), Cell(right, bottom) };
        var x0 = Math.Max(minX, (int)Math.Floor(corners.Min(c => c.X)) - 1); var x1 = Math.Min(maxX, (int)Math.Ceiling(corners.Max(c => c.X)) + 1);
        var y0 = Math.Max(minY, (int)Math.Floor(corners.Min(c => c.Y)) - 1); var y1 = Math.Min(maxY, (int)Math.Ceiling(corners.Max(c => c.Y)) + 1);
        var result = new List<CellCoord>();
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var cx = OriginX + 40.0 * (y - x); var cy = 20.0 * (y + x) + 20;
            if (cx + 40 < left || cx - 40 > right || cy + 20 < top || cy - 20 > bottom) continue;
            if (result.Count >= cap) return result;
            result.Add(new(x, y));
        }
        return result;
    }

    private bool InChart(CellCoord cell)
    {
        var (minX, minY, maxX, maxY) = Authority.Chart;
        return cell.X >= minX && cell.X <= maxX && cell.Y >= minY && cell.Y <= maxY;
    }
    private OverlayStep Count(OverlayStep step) { AssessCalls++; return step; }
}
