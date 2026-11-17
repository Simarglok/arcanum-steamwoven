using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Combat;

/// <summary>How a wall or portal on a tile edge meets a line of fire (0x43FE00 with the projectile flags 0x0C, ledger AW7, RA rows).</summary>
public enum LineOfFirePieceKind
{
    /// <summary>A wall piece that is not a window or doorway piece and has no pass-wall spell: it stops the line.</summary>
    Solid,
    /// <summary>A wall piece with the pass-wall spell (spell flag 0x100000): the cover test on its object flags.</summary>
    PassWalled,
    /// <summary>A window piece (10, 13, 14, 17, 18, 19): passes on an edge step, stops a corner step's leg.</summary>
    Window,
    /// <summary>A doorway piece (22, 25, 26, 29–32): passes.</summary>
    Doorway,
    /// <summary>A door or window that is open (frame not 0) or busted (ART bit 0x200): passes.</summary>
    OpenPortal,
    /// <summary>A closed, unbroken door or window: the cover test on its object flags.</summary>
    ClosedPortal,
}

/// <summary>A wall or portal on one edge of a tile, with its object flags (field 19), for the line of fire.</summary>
public readonly record struct LineOfFirePiece<TObject>(TObject Object, LineOfFirePieceKind Kind, uint ObjectFlags) where TObject : class;

/// <summary>An object on a tile that is not a wall or portal, with its object flags (field 19); a dead critter passes.</summary>
public readonly record struct LineOfFireObject<TObject>(TObject Object, bool DeadCritter, uint ObjectFlags) where TObject : class;

/// <summary>
/// The map a line of fire walks (the host's): the original's step to a neighbouring tile (0x4B8FF0), the map's bounds (the test of
/// the screen-to-tile conversion 0x4B8730), and per tile its walls and portals by edge and its other objects (0x4407C0), all in the
/// world cells of <see cref="CellCoord"/> and the rotation order of <see cref="NeighborDirection"/>.
/// </summary>
public interface ILineOfFireMap<TObject> where TObject : class
{
    /// <summary>The tile one step from <paramref name="from"/> in rotation 0..7, or null when it leaves the map.</summary>
    CellCoord? Step(CellCoord from, int rotation);

    /// <summary>Whether the tile is on the map.</summary>
    bool Contains(CellCoord tile);

    /// <summary>The walls and portals of the tile whose edge (their ART rotation rounded up to odd) is <paramref name="edge"/>.</summary>
    IEnumerable<LineOfFirePiece<TObject>> Edge(CellCoord tile, int edge);

    /// <summary>The tile's objects other than walls and portals that a projectile meets (critters, containers, scenery, items and
    /// the rest of the original's mask 0x3801F), less what the session destroyed or switched off.</summary>
    IEnumerable<LineOfFireObject<TObject>> Objects(CellCoord tile);
}

/// <summary>A traced line of fire: the summed cover cost, the first object that stops it (null when none), whether the line could not
/// be built (more than 1,000 pixels on its major axis: cost 100, no blocker), and the tiles it entered in order.</summary>
public sealed record LineOfFireTrace<TObject>(int Cost, TObject? Blocker, bool LineFailed, IReadOnlyList<CellCoord> Tiles) where TObject : class;

/// <summary>
/// ARC-COMBAT-012: the original's line of fire (0x4ADE00, read in the retail 1.0.7.4 executable by two readers, ledger AW6–AW8,
/// AW13–AW18 and RA rows): a pixel line between the two tiles' screen centres, walked in 10-pixel samples from the attacker's tile
/// plus its pixel offset, each tile change priced by the step cost with the projectile flags. Pure functions over an injected map.
/// </summary>
public static class LineOfFireRules
{
    /// <summary>The line's limit: more pixels than this on its major axis fail it (AW17); the cost of a failed line.</summary>
    public const int MaxLinePixels = 1000, FailedLineCost = 100;
    /// <summary>A sample is recorded every <see cref="SamplePeriod"/> pixels of the line (0x420330).</summary>
    public const int SamplePeriod = 10;
    /// <summary>The isometric tile's half extents in screen pixels (0x4B8680, the normal view).</summary>
    public const int HalfWidth = 40, HalfHeight = 20;
    /// <summary>The cover test's costs (0x43FE00): shoot-through and not see-through, and see-through providing cover.</summary>
    public const int OpaqueCover = 50, ProvidedCover = 20;
    /// <summary>Object flags (field 19) the cover test reads.</summary>
    public const uint SeeThrough = 0x10, ShootThrough = 0x20, ProvidesCover = 0x4000;
    /// <summary>The animation's clear-shot limit (AW8): a line with this much cover or more is no clear shot.</summary>
    public const int ClearShotCover = 26;

    private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];

    /// <summary>0x4B8680 in the normal (isometric) view, without the view origin (the walk's conversions cancel it): a tile's
    /// top-left screen point (registered with the positional sound's port of the same function).</summary>
    public static (long X, long Y) Screen(CellCoord tile) => (((long)tile.Y - tile.X - 1) * HalfWidth, ((long)tile.X + tile.Y) * HalfHeight);

    /// <summary>0x4B8730 in the normal view: the tile under a screen point (each axis truncated toward zero, minus 1 below zero),
    /// or null when it is off the map (the original then leaves its output unchanged).</summary>
    [OriginalFunction(0x4B8730, OriginalFunctionScope.Partial)]
    public static CellCoord? Tile(long screenX, long screenY, Func<CellCoord, bool> contains)
    {
        ArgumentNullException.ThrowIfNull(contains);
        long a = screenX >> 1, b = screenY, u = b - a, v = b + a;
        // The original's two range tests (its subtraction below and its addition above the 32-bit range); a quotient by 40 fits.
        if (u < int.MinValue || v > int.MaxValue) return null;
        var tile = new CellCoord((int)(u / HalfWidth - (u < 0 ? 1 : 0)), (int)(v / HalfWidth - (v < 0 ? 1 : 0)));
        return contains(tile) ? tile : null;
    }

    /// <summary>0x4B8D50: the rotation 0..7 from one tile toward another, diagonal when twice the minor delta reaches the major one;
    /// 5 for the same tile.</summary>
    [OriginalFunction(0x4B8D50, OriginalFunctionScope.Full)]
    public static int Rotation(CellCoord from, CellCoord to)
    {
        long dx = (long)to.X - from.X, dy = (long)to.Y - from.Y;
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        if (Math.Abs(dx) > Math.Abs(dy)) { if (2 * Math.Abs(dy) < Math.Abs(dx)) sy = 0; }
        else if (2 * Math.Abs(dx) < Math.Abs(dy)) sx = 0;
        return (sx, sy) switch
        {
            (-1, -1) => 0, (-1, 0) => 1, (-1, 1) => 2, (0, 1) => 3, (1, 1) => 4, (1, 0) => 5, (1, -1) => 6, (0, -1) => 7,
            _ => 5,
        };
    }

    /// <summary>
    /// The line's samples (0x4201C0 with the sampler 0x420330 over the line 0x4203B0): a Bresenham line between the two screen points
    /// (x-major only when |dx| &gt; |dy|; both ends drawn), every 10th point recorded as its offset from the last recorded point
    /// (the first from <paramref name="from"/>), and a last sample to <paramref name="to"/> when points are left over. Null when the
    /// line has more than <see cref="MaxLinePixels"/> points.
    /// </summary>
    [OriginalFunction(0x4201C0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x420330, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4203B0, OriginalFunctionScope.Full)]
    public static IReadOnlyList<(int Dx, int Dy)>? Samples((long X, long Y) from, (long X, long Y) to)
    {
        long dx = to.X - from.X, dy = to.Y - from.Y, ax = Math.Abs(dx), ay = Math.Abs(dy);
        if (Math.Max(ax, ay) + 1 > MaxLinePixels) return null;
        var samples = new List<(int, int)>();
        var last = from;
        var counter = 0;
        foreach (var point in Line(from, to, dx, dy, ax, ay))
        {
            if (++counter != SamplePeriod) continue;
            samples.Add(((int)(point.X - last.X), (int)(point.Y - last.Y)));
            last = point;
            counter = 0;
        }
        if (counter != 0) samples.Add(((int)(to.X - last.X), (int)(to.Y - last.Y)));
        return samples;
    }

    // 0x4203B0: the error starts at 2·minor − major; before each major step the minor axis steps when the error is not negative.
    private static IEnumerable<(long X, long Y)> Line((long X, long Y) from, (long X, long Y) to, long dx, long dy, long ax, long ay)
    {
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        var (x, y) = from;
        yield return (x, y);
        if (ax > ay)
        {
            var error = 2 * ay - ax;
            while (x != to.X)
            {
                if (error >= 0) { y += sy; error -= 2 * ax; }
                x += sx; error += 2 * ay;
                yield return (x, y);
            }
        }
        else
        {
            var error = 2 * ax - ay;
            while (y != to.Y)
            {
                if (error >= 0) { x += sx; error -= 2 * ay; }
                y += sy; error += 2 * ax;
                yield return (x, y);
            }
        }
    }

    /// <summary>
    /// The tile steps of a line walk (shared by the line of fire 0x4ADE00 and the sound cost 0x4AF640): the pixel line between the
    /// two tiles' screen centres, walked from <paramref name="from"/>'s centre plus the walker's pixel offset (object fields 3 and 4,
    /// AW18) in its samples; each time the point lands on another tile (a point off the map keeps the last tile) that step is listed,
    /// and the walk ends on reaching <paramref name="to"/> or when the samples run out. The tiles do not depend on what a step costs,
    /// so a caller that stops at a blocker stops partway through the list. Empty for the same tile; null when the line cannot be
    /// built (more than <see cref="MaxLinePixels"/> pixels on its major axis, AW17).
    /// </summary>
    public static IReadOnlyList<LineWalkStep>? Walk(CellCoord from, int offsetX, int offsetY, CellCoord to, Func<CellCoord, bool> contains)
    {
        ArgumentNullException.ThrowIfNull(contains);
        if (from == to) return [];
        var start = Screen(from);
        var end = Screen(to);
        if (Samples((start.X + HalfWidth, start.Y + HalfHeight), (end.X + HalfWidth, end.Y + HalfHeight)) is not { } samples) return null;
        var steps = new List<LineWalkStep>();
        var current = from;
        long accX = offsetX, accY = offsetY;
        foreach (var (sampleX, sampleY) in samples)
        {
            if (current == to) break;
            accX += sampleX; accY += sampleY;
            var next = Tile(start.X + accX + HalfWidth, start.Y + accY + HalfHeight, contains) ?? current;
            if (next == current) continue;
            steps.Add(new(current, Rotation(current, next), next));
            current = next;
        }
        return steps;
    }

    /// <summary>
    /// The line of fire from the attacker's tile (with its pixel offset, object fields 3 and 4) to the target tile (0x4ADE00): the same
    /// tile costs 0; a line that cannot be built costs <see cref="FailedLineCost"/> with no blocker. Otherwise each step of the
    /// <see cref="Walk"/> is priced (<see cref="StepCost{T}"/>, the target tile's own non-wall objects skipped) and the walk stops at
    /// the first blocker or when it reaches the target tile.
    /// </summary>
    [OriginalFunction(0x4ADE00, OriginalFunctionScope.Partial)]
    public static LineOfFireTrace<TObject> Trace<TObject>(CellCoord from, int offsetX, int offsetY, CellCoord to, ILineOfFireMap<TObject> map,
        CombatAnomalyProfile profile) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(profile);
        if (Walk(from, offsetX, offsetY, to, map.Contains) is not { } steps) return new(FailedLineCost, null, true, []);
        var tiles = new List<CellCoord>();
        var cost = 0;
        foreach (var step in steps)
        {
            var met = StepCost(step.From, step.Rotation, step.To == to, map, profile);
            cost += met.Cost;
            if (met.Blocker is not null) return new(cost, met.Blocker, false, tiles);
            tiles.Add(step.To);
        }
        return new(cost, null, false, tiles);
    }

    /// <summary>
    /// The animation's "clear shot from here" test on a traced line (the two attack-goal callbacks 0x425590 and 0x429960, ledger
    /// AW8; their weapon, reach and goal handling are the attack executor's): the cover is below <see cref="ClearShotCover"/>, so at
    /// most one see-through cover object, and nothing but <paramref name="target"/> (the same object: identity, as the original compares
    /// object handles) stops the line.
    /// </summary>
    public static bool ClearShot<TObject>(LineOfFireTrace<TObject> trace, TObject target) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(trace);
        return trace.Cost < ClearShotCover && (trace.Blocker is null || ReferenceEquals(trace.Blocker, target));
    }

    /// <summary>
    /// The hero's attack line (ARC-COMBAT-015; owner decision 2026-10-28): <see cref="Trace{TObject}"/>, except that a target next to the
    /// attacker is read as the Original setting reads it (<see cref="CombatAnomalyProfile.CornerStepCountsBothRoutes"/>: the side tiles'
    /// objects of a corner step into the target are skipped, AW14), so the Steamwoven fix of AW13/AW14 keeps every longer line and does
    /// not refuse or redirect an attack on an adjacent target past a companion, NPC or scenery on a side tile. Three of the four
    /// adjacent diagonals are walked as one corner step; the screen-left one (x + 1, y − 1) is walked over the side tile (x + 1, y) as two
    /// edge steps in both settings, so an object there still stops it, as in the original.
    /// </summary>
    public static LineOfFireTrace<TObject> ReachTrace<TObject>(CellCoord from, int offsetX, int offsetY, CellCoord to, ILineOfFireMap<TObject> map,
        CombatAnomalyProfile profile) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(profile);
        var adjacent = Math.Max(Math.Abs((long)to.X - from.X), Math.Abs((long)to.Y - from.Y)) == 1;
        return Trace(from, offsetX, offsetY, to, map, adjacent && !profile.CornerStepCountsBothRoutes ? profile with { CornerStepCountsBothRoutes = true } : profile);
    }

    /// <summary>
    /// The attack goal's reach test (0x429960, ledger EX8, RH1; ARC-COMBAT-015): the tile distance (0x4B96F0) at most the larger of the
    /// wielded weapon's range and 1, and a clear shot (<see cref="ClearShot{TObject}"/>) on the line traced from the attacker to the
    /// target, for every weapon (melee next to its target too). When it fails the goal approaches the target (ledger AP1–AP7). The
    /// target's destroyed and off flags and the wielded item are the caller's.
    /// </summary>
    [OriginalFunction(0x429960, OriginalFunctionScope.Partial)]
    public static bool InReach<TObject>(int distance, int range, LineOfFireTrace<TObject> trace, TObject target) where TObject : class =>
        distance >= 0 && distance <= Math.Max(range, 1) && ClearShot(trace, target);

    /// <summary>
    /// The step cost of one tile step with the projectile flags (0x43FDC0 → 0x43FE00, flags 0x0C, no mover): an edge step (odd
    /// rotation) prices the source tile's walls and portals on the crossed edge, then the neighbour (none: cost 1, the sum so far
    /// dropped), then the neighbour's walls and portals on the opposite edge and, unless <paramref name="intoTarget"/>, its other
    /// objects; the first blocker ends the step. With <paramref name="portalsBlock"/> (the step cost flag 0x1 of the projectile's own
    /// call, flags 0x0D, ARC-COMBAT-1725) a closed, unbroken portal on a crossed edge stops the step outright, before its cover test, so
    /// a portal that shoots through still stops a missed shot flying free; the line of fire's flags 0x0C leave it to the cover test. A corner step (even rotation) sums four edge steps around the corner: to the
    /// counter-clockwise side tile and on to the corner tile, then to the clockwise side tile and on to the corner tile (a side tile
    /// off the map: cost 1). <see cref="CombatAnomalyProfile.CornerStepCountsBothRoutes"/> keeps the original's double count (AW13,
    /// AW14): the corner tile's objects count on both routes and <paramref name="intoTarget"/> skips the side tiles' objects too;
    /// otherwise the second route counts only the corner tile's walls and portals, and only the target tile's objects are skipped.
    /// </summary>
    [OriginalFunction(0x43FE00, OriginalFunctionScope.Partial)]
    public static LineOfFireCost<TObject> StepCost<TObject>(CellCoord from, int rotation, bool intoTarget, ILineOfFireMap<TObject> map,
        CombatAnomalyProfile profile, bool portalsBlock = false) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(profile);
        if (rotation is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(rotation));
        if ((rotation & 1) == 1) return EdgeStep(from, rotation, false, intoTarget ? Skip.Objects : Skip.None, map, portalsBlock);
        int ccw = (rotation + 7) % 8, cw = rotation + 1;
        var original = profile.CornerStepCountsBothRoutes;
        var side = original && intoTarget ? Skip.Objects : Skip.None;
        var corner = intoTarget ? Skip.Objects : Skip.None;
        var total = 0;
        var leg = EdgeStep(from, ccw, true, side, map, portalsBlock);
        total += leg.Cost;
        if (leg.Blocker is not null) return new(total, leg.Blocker);
        if (map.Step(from, ccw) is not { } first) return new(1, null);
        leg = EdgeStep(first, cw, true, corner, map, portalsBlock);
        total += leg.Cost;
        if (leg.Blocker is not null) return new(total, leg.Blocker);
        leg = EdgeStep(from, cw, true, side, map, portalsBlock);
        total += leg.Cost;
        if (leg.Blocker is not null) return new(total, leg.Blocker);
        if (map.Step(from, cw) is not { } second) return new(1, null);
        leg = EdgeStep(second, ccw, true, original ? corner : Skip.Objects, map, portalsBlock);
        return new(total + leg.Cost, leg.Blocker);
    }

    private enum Skip { None, Objects }

    // One edge step: the source tile's pieces on the edge, the neighbour, the neighbour's pieces on the opposite edge and its objects.
    // Within the neighbour the pieces are taken before the other objects (the original takes the tile's one list in its order).
    private static LineOfFireCost<TObject> EdgeStep<TObject>(CellCoord from, int edge, bool cornerLeg, Skip skip, ILineOfFireMap<TObject> map,
        bool portalsBlock) where TObject : class
    {
        var cost = 0;
        foreach (var piece in map.Edge(from, edge))
        {
            var met = Piece(piece, cornerLeg, portalsBlock);
            cost += met.Cost;
            if (met.Blocker is not null) return new(cost, met.Blocker);
        }
        if (map.Step(from, edge) is not { } next) return new(1, null);
        foreach (var piece in map.Edge(next, (edge + 4) % 8))
        {
            var met = Piece(piece, cornerLeg, portalsBlock);
            cost += met.Cost;
            if (met.Blocker is not null) return new(cost, met.Blocker);
        }
        if (skip == Skip.Objects) return new(cost, null);
        foreach (var item in map.Objects(next))
        {
            if (item.DeadCritter) continue;
            var met = Cover(item.Object, item.ObjectFlags);
            cost += met.Cost;
            if (met.Blocker is not null) return new(cost, met.Blocker);
        }
        return new(cost, null);
    }

    private static LineOfFireCost<TObject> Piece<TObject>(LineOfFirePiece<TObject> piece, bool cornerLeg, bool portalsBlock) where TObject : class => piece.Kind switch
    {
        LineOfFirePieceKind.Solid => new(0, piece.Object),
        LineOfFirePieceKind.Window => cornerLeg ? new(0, piece.Object) : default,
        LineOfFirePieceKind.Doorway or LineOfFirePieceKind.OpenPortal => default,
        LineOfFirePieceKind.ClosedPortal when portalsBlock => new(0, piece.Object),
        LineOfFirePieceKind.PassWalled or LineOfFirePieceKind.ClosedPortal => Cover(piece.Object, piece.ObjectFlags),
        _ => throw new ArgumentOutOfRangeException(nameof(piece)),
    };

    /// <summary>The cover test (0x43FE00): not shoot-through stops the line; shoot-through and not see-through costs
    /// <see cref="OpaqueCover"/>; see-through providing cover <see cref="ProvidedCover"/>; otherwise nothing. Object flag 0x400 (no-block) changes none of it with
    /// the projectile flags (the original's jump over its type branch lands on this same cover test; ARC-COMBAT-2170).</summary>
    public static LineOfFireCost<TObject> Cover<TObject>(TObject item, uint objectFlags) where TObject : class =>
        (objectFlags & ShootThrough) == 0 ? new(0, item)
        : (objectFlags & SeeThrough) == 0 ? new(OpaqueCover, null)
        : (objectFlags & ProvidesCover) != 0 ? new(ProvidedCover, null)
        : default;
}

/// <summary>One tile step of a line walk: the tile left, the rotation 0..7 toward the next tile (<see cref="LineOfFireRules.Rotation"/>)
/// and the tile entered.</summary>
public readonly record struct LineWalkStep(CellCoord From, int Rotation, CellCoord To);

/// <summary>What a step or a part of it costs a line of fire, and the object that stops it (null when none).</summary>
public readonly record struct LineOfFireCost<TObject>(int Cost, TObject? Blocker) where TObject : class;
