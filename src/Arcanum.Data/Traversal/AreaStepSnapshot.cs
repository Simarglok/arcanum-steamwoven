using Arcanum.Data.Area;

namespace Arcanum.Data.Traversal;

/// <summary>
/// Owned whole-core step evidence at the new-game initial state T0, with no source retention or query-time I/O.
/// Admission is the declared sector set only (owner decision: the nine Crash Site core sectors; the cave
/// profile admits its single sector); a step whose
/// destination or diagonal shoulder leaves it is Unsupported, like a map edge. State after T0 (AI, scripts,
/// saves) is outside this profile.
/// </summary>
public sealed class AreaStepSnapshot
{
    private static readonly (int Dx, int Dy)[] Deltas = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
    private readonly HashSet<(long X, long Y)> _sectors;
    private readonly IReadOnlyDictionary<AreaStepCell, AreaStepTerrainEvidence> _terrain;
    private readonly IReadOnlyDictionary<AreaStepCell, AreaStepObjectEvaluation[]> _objects;
    private readonly IReadOnlyDictionary<AreaStepCell, AreaStepWallEdge[]> _walls;

    internal AreaStepSnapshot(AreaStepInputIdentity identity, long epoch, IEnumerable<(long X, long Y)> admittedSectors,
        IReadOnlyDictionary<AreaStepCell, AreaStepTerrainEvidence> terrain, IReadOnlyDictionary<AreaStepCell, AreaStepObjectEvaluation[]> objects,
        IReadOnlyDictionary<AreaStepCell, AreaStepWallEdge[]>? walls = null)
    {
        InputIdentity = identity; EvidenceEpoch = epoch; DeclaredContext = CardinalTraversalRules.DeclaredContext();
        _sectors = admittedSectors.ToHashSet(); _terrain = terrain; _objects = objects; _walls = walls ?? new Dictionary<AreaStepCell, AreaStepWallEdge[]>();
        WallEdges = _walls.Values.Sum(w => w.Length);
        AdmittedSectors = Array.AsReadOnly(_sectors.OrderBy(s => s.Y).ThenBy(s => s.X).ToArray());
        TerrainBlockedCells = terrain.Values.Count(t => t.Disposition == CardinalTraversalDisposition.Blocked);
        TerrainUnknownCells = terrain.Values.Count(t => t.Coverage != CardinalTraversalCoverage.Supported);
        ObjectBlockerCells = objects.Count(o => o.Value.Any(e => e.Disposition == CardinalTraversalDisposition.Blocked));
        UnknownObjects = objects.Values.Sum(o => o.Count(e => e.Coverage != CardinalTraversalCoverage.Supported));
    }

    public string ProfileId => AreaStepRules.ProfileId;
    public AreaStepInputIdentity InputIdentity { get; }
    public string EvidenceSha256 => InputIdentity.EvidenceSha256;
    public long EvidenceEpoch { get; }
    public CardinalTraversalContext DeclaredContext { get; }
    public IReadOnlyList<(long X, long Y)> AdmittedSectors { get; }
    /// <summary>Every object evaluation, for checks over a whole snapshot.</summary>
    internal IEnumerable<AreaStepObjectEvaluation> ObjectEvaluations => _objects.Values.SelectMany(list => list);
    public int AdmittedCells => _terrain.Count;
    public int TerrainBlockedCells { get; }
    public int TerrainUnknownCells { get; }
    public int ObjectBlockerCells { get; }
    public int UnknownObjects { get; }
    /// <summary>Walls registered as tile-edge components (each blocks one edge, never its tile).</summary>
    public int WallEdges { get; }
    public bool IsAdmitted(AreaStepCell cell) => _sectors.Contains((cell.X >> 6, cell.Y >> 6));
    /// <summary>The owned terrain verdict of one admitted cell, or null outside the admitted area.</summary>
    public AreaStepTerrainEvidence? Terrain(AreaStepCell cell) => IsAdmitted(cell) && _terrain.TryGetValue(cell, out var evidence) ? evidence : null;

    public AreaStepRequest Request(AreaStepCell from, AreaStepCell to, CardinalTraversalDirection direction) =>
        new(ProfileId, EvidenceSha256, EvidenceEpoch, from, to, direction, DeclaredContext);

    public AreaStepAssessment Assess(AreaStepRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var denial = Validate(request);
        if (denial is not null) return Result(request, CardinalTraversalCoverage.Unsupported, null, denial, null, [], []);
        if (request.Context.PathFlags == 0x1000)
            return Result(request, CardinalTraversalCoverage.Unknown, null, "PATH_BRANCH_UNAVAILABLE", null, [], [new("PATH_BRANCH_UNAVAILABLE")]);

        var gaps = new List<AreaStepGap>(); var objects = new List<AreaStepObjectEvidence>();
        var terrain = _terrain[request.To];
        if (terrain.Coverage != CardinalTraversalCoverage.Supported) gaps.Add(new(terrain.ReasonCode, request.To));
        void Collect(AreaStepCell cell, AreaStepObjectRole role)
        {
            foreach (var evaluation in _objects.GetValueOrDefault(cell, []))
            {
                objects.Add(new(evaluation, role));
                if (evaluation.Coverage != CardinalTraversalCoverage.Supported) gaps.Add(new(evaluation.Reason, cell, evaluation.Key, role));
            }
        }
        // Object-only legs: both diagonal shoulders and the final cell. Shoulder terrain is never consulted.
        var direction = (int)request.Direction;
        if ((direction & 1) == 0)
        {
            Collect(Neighbor(request.From, (direction + 7) % 8), AreaStepObjectRole.CounterClockwiseShoulder);
            Collect(Neighbor(request.From, direction + 1), AreaStepObjectRole.ClockwiseShoulder);
        }
        Collect(request.To, AreaStepObjectRole.Destination);

        // Wall legs: a cardinal step crosses one edge; a diagonal step checks both L-shaped paths
        // around it (counter-clockwise then clockwise, and clockwise then counter-clockwise), four legs.
        AreaObjectKey? wallBlocker = null, windowJump = null;
        // The source tile's walls and portals take part in leaving it: an Unknown one there is a gap.
        foreach (var evaluation in _objects.GetValueOrDefault(request.From, []))
            if (evaluation.ObjectType is 0 or 1 && evaluation.Coverage != CardinalTraversalCoverage.Supported)
                gaps.Add(new(evaluation.Reason, request.From, evaluation.Key));
        void Leg(AreaStepCell cell, int edge, bool diagonal)
        {
            var next = Neighbor(cell, edge);
            foreach (var wall in _walls.GetValueOrDefault(cell, []).Where(w => w.Edge == edge).Concat(_walls.GetValueOrDefault(next, []).Where(w => w.Edge == (edge + 4) % 8)))
            {
                switch (wall.Rule)
                {
                    case AreaStepEdgeRule.Legacy when wall.Opening: gaps.Add(new("WALL_OPENING_PIECE_UNSUPPORTED", wall.Cell, wall.Key)); break;
                    case AreaStepEdgeRule.Legacy or AreaStepEdgeRule.Block: wallBlocker ??= wall.Key; break;
                    case AreaStepEdgeRule.Unknown: gaps.Add(new(wall.Reason ?? "EDGE_RULE_UNKNOWN", wall.Cell, wall.Key)); break;
                    case AreaStepEdgeRule.WindowJump when diagonal: wallBlocker ??= wall.Key; break;
                    // ARC-WORLD-021: a critter walker jumps a window on a cardinal step (research ARC-RES-047 W1–W3);
                    // a window portal on the same edge still decides through its own rule.
                    case AreaStepEdgeRule.WindowJump: windowJump ??= wall.Key; break;
                    // ARC-RES-049: a locked portal that only a step leaving a building opens.
                    case AreaStepEdgeRule.Leaving:
                        switch (LeavingStepOpens(wall, wall.Cell == cell, request.From))
                        {
                            case true: break;
                            case false when wall.Reason == AreaStepInspector.LockedLeavingOnlyReason: wallBlocker ??= wall.Key; break;
                            case false: gaps.Add(new(wall.Reason ?? "EDGE_RULE_UNKNOWN", wall.Cell, wall.Key)); break;
                            case null: gaps.Add(new("PORTAL_LEAVING_TILE_UNRESOLVED", wall.Cell, wall.Key)); break;
                        }
                        break;
                }
            }
        }
        if ((direction & 1) == 1) Leg(request.From, direction, false);
        else
        {
            int ccw = (direction + 7) % 8, cw = direction + 1;
            Leg(request.From, ccw, true); Leg(Neighbor(request.From, ccw), cw, true);
            Leg(request.From, cw, true); Leg(Neighbor(request.From, cw), ccw, true);
        }

        if (terrain.Disposition == CardinalTraversalDisposition.Blocked)
            return Result(request, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "DESTINATION_TERRAIN_BLOCKED", terrain, objects, gaps, windowJump: windowJump);
        if (wallBlocker is not null)
            return Result(request, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, "WALL_EDGE_BLOCKED", terrain, objects, gaps, wallBlocker);
        if (objects.Any(o => o.Disposition == CardinalTraversalDisposition.Blocked))
            return Result(request, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked,
                objects.First(o => o.Disposition == CardinalTraversalDisposition.Blocked).Role == AreaStepObjectRole.Destination
                    ? "DESTINATION_OBJECT_BLOCKER" : "SHOULDER_OBJECT_BLOCKER", terrain, objects, gaps, windowJump: windowJump);
        // The window stays named on a step a host overlay may still open (ARC-WORLD-021).
        if (gaps.Count != 0) return Result(request, CardinalTraversalCoverage.Unknown, null, "REQUIRED_EVIDENCE_UNAVAILABLE", terrain, objects, gaps, windowJump: windowJump);
        return windowJump is not null
            ? Result(request, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, "WINDOW_JUMP", terrain, objects, gaps, windowJump: windowJump)
            : Result(request, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, "COMPLETE_NONBLOCKING_EVIDENCE", terrain, objects, gaps);
    }

    private AreaStepAssessment Result(AreaStepRequest request, CardinalTraversalCoverage coverage, CardinalTraversalDisposition? disposition,
        string reason, AreaStepTerrainEvidence? terrain, IEnumerable<AreaStepObjectEvidence> objects, IEnumerable<AreaStepGap> gaps, AreaObjectKey? wall = null,
        AreaObjectKey? windowJump = null) =>
        new(request, InputIdentity, EvidenceEpoch, coverage, disposition, reason, terrain, objects, gaps, wall, windowJump);

    private static AreaStepCell Neighbor(AreaStepCell cell, int direction) => new(cell.X + Deltas[direction].Dx, cell.Y + Deltas[direction].Dy);

    /// <summary>ARC-RES-049, read in the original 1.0.7.4 executable (research verification ledger): a closed locked
    /// portal opens for the walking player only when both of its open attempts on the step call it leaving a building.
    /// Each attempt compares the portal's tile P with Q, the tile one step along the portal's raw ART rotation (not the
    /// edge), and leaves when the first of the pair is indoor and the second is not; it exchanges P and Q when its
    /// direction is opposite the rotation or next to opposite. The traversal attempt passes the leg's edge direction, so
    /// it exchanges them exactly when the leg enters P from across; the door or window goal passes the direction from
    /// the walker's tile to P, which is 5 on P itself, so a portal of raw rotation 0..2 is tested in reverse there and
    /// the two attempts never agree. Null when a tile is outside the evidence or not tile or facade art.</summary>
    private bool? LeavingStepOpens(AreaStepWallEdge portal, bool legStartsOnPortal, AreaStepCell walker)
    {
        var rotation = portal.RawRotation;
        if (rotation is < 0 or > 7 || (rotation | 1) != portal.Edge) return null;
        if (Indoor(Terrain(portal.Cell)) is not { } portalIndoor || Indoor(Terrain(Neighbor(portal.Cell, rotation))) is not { } otherIndoor) return null;
        bool Leaves(int direction) =>
            (direction + 4) % 8 == rotation || (direction + 5) % 8 == rotation || (direction + 3) % 8 == rotation
                ? otherIndoor && !portalIndoor
                : portalIndoor && !otherIndoor;
        var traversal = Leaves(legStartsOnPortal ? portal.Edge : (portal.Edge + 4) % 8);
        // The walker stands on the step's source, the portal's tile or one of its neighbours (every leg touches it).
        var goal = Leaves(walker == portal.Cell ? 5 : Array.IndexOf(Deltas, (Math.Sign(portal.Cell.X - walker.X), Math.Sign(portal.Cell.Y - walker.Y))));
        return traversal && goal;
    }

    /// <summary>The original's tile type: tile art is indoor when bit 8 is clear, facade art when bit 25 is clear.</summary>
    private static bool? Indoor(AreaStepTerrainEvidence? terrain) => terrain?.RawArt is not { } raw ? null : (raw >> 28) switch
    {
        0 => ((raw >> 8) & 1) == 0,
        11 => ((raw >> 25) & 1) == 0,
        _ => null,
    };

    private string? Validate(AreaStepRequest r)
    {
        if (r.ProfileId != ProfileId) return "PROFILE_UNSUPPORTED";
        if (r.EvidenceSha256 != EvidenceSha256) return "EVIDENCE_IDENTITY_MISMATCH";
        if (r.EvidenceEpoch != EvidenceEpoch) return "EPOCH_MISMATCH";
        if ((uint)r.Direction >= 8) return "DIRECTION_UNSUPPORTED";
        // Admission first: admitted cells are bounded, so the deltas below cannot overflow.
        if (!IsAdmitted(r.From)) return "SOURCE_OUTSIDE_ADMITTED_AREA";
        if (!IsAdmitted(r.To)) return "DESTINATION_OUTSIDE_ADMITTED_AREA";
        var dx = r.To.X - r.From.X; var dy = r.To.Y - r.From.Y;
        if (dx == 0 && dy == 0) return "SAME_CELL_UNSUPPORTED";
        if (dx is < -1 or > 1 || dy is < -1 or > 1) return "NONADJACENT_UNSUPPORTED";
        if (dx != Deltas[(int)r.Direction].Dx || dy != Deltas[(int)r.Direction].Dy) return "DIRECTION_MISMATCH";
        var c = r.Context;
        if (c.ContextId != DeclaredContext.ContextId || c.ActorKind != 15 || c.CurrentArtClass != 2 || c.SpellFlags != 0 ||
            (c.PathFlags != 0 && c.PathFlags != 0x1000) || c.Polymorph || c.Editor ||
            c.ActorState != CardinalTraversalActorState.OrdinaryGround || c.SupplementalRegistryCount != 0) return "CONTEXT_UNSUPPORTED";
        // Shoulders of an admitted diagonal lie in the same sector rectangle hull; checked anyway, never assumed.
        if (((int)r.Direction & 1) == 0 && (!IsAdmitted(Neighbor(r.From, ((int)r.Direction + 7) % 8)) || !IsAdmitted(Neighbor(r.From, (int)r.Direction + 1))))
            return "SHOULDER_OUTSIDE_ADMITTED_AREA";
        return null;
    }
}
