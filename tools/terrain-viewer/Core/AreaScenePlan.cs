using Arcanum.Data.Area;
using Arcanum.Data.Terrain;

namespace Arcanum.TerrainViewer.Core;

public enum AreaDisposition { TerrainTile, TerrainFacade, UprightBody, FlatBody, FlatUnderAll, OutsideScope, Suppressed }
public sealed record AreaCoverage(AreaObjectKey? Identity, AreaVisualKey? Visual, AreaDisposition Disposition, string Reason);
public sealed record AreaFrameKey(AreaEntryIdentity Entry, int Palette, int Group, int Frame,
    string Policy, string PayloadSha256, int ViewLength, string RgbaSha256)
{
    public static AreaFrameKey From(AreaVisualRecord visual) => new(visual.Entry,
        visual.EffectivePalette, visual.PhysicalGroup, visual.Frame,
        visual.Policy, visual.PayloadSha256, visual.ViewLength, visual.RgbaSha256);
}
public sealed record AreaDrawCommand(AreaVisualKey Visual, AreaFrameKey Frame, AreaObjectKey? Identity,
    TerrainRect Source, TerrainRect Destination, bool FlipX, bool FlipY, int Phase, int MapX, int MapY,
    int FirstRank, int SecondRank, int Alpha, bool Additive)
{
    /// <summary>The ART class of the drawn art (raw art id &gt;&gt; 28; 1 = wall, as <see cref="AreaScenePlan.Hotspot"/>
    /// reads it). ARC-RENDER-014 binds HD for characters and objects, not walls.</summary>
    public int ArtClass { get; init; }

    /// <summary>ARC-RENDER-018: multiply blend (blit <c>0x40</c>): each opaque source pixel multiplies the colour
    /// beneath it; transparent pixels leave it unchanged.</summary>
    public bool Multiply { get; init; }

    /// <summary>ARC-RENDER-018: drawn with the base palette, without ambient or stationary light (the original drops
    /// every colour mode of a multiply or constant-alpha body).</summary>
    public bool Unlit { get; init; }

    /// <summary>ARC-RENDER-018 wading: the bottom <see cref="WadeRows"/> destination rows are drawn at
    /// <see cref="AreaScenePlan.WadingAlpha"/>, which replaces every other alpha of those rows; 0 = not wading.
    /// Relative to <see cref="Destination"/>, so a moved command keeps its water line.</summary>
    public int WadeRows { get; init; }

    /// <summary>True when the drawn pixels are not the source pixels over the destination (any blend, constant alpha
    /// or wading); pixel observations that expect source pixels skip such commands.</summary>
    public bool Blended => Additive || Multiply || Alpha != 255 || WadeRows > 0;
}

/// <summary>ARC-RENDER-022: one roof block and the command that draws its frame (drawn after every body, when the block is
/// not filled; ledger RF1, RF2, RF7).</summary>
public sealed record AreaRoofDraw(AreaRoofRecord Roof, AreaDrawCommand Command);

/// <summary>One world chart and one stable immutable body policy. Body appearance
/// belongs to DATA009; footprint terrain cropping belongs to the established
/// diagnostic TerrainGeometry seam. No ART/resource lookup occurs here.</summary>
public sealed class AreaScenePlan
{
    private AreaScenePlan(TerrainRect bounds, IReadOnlyList<AreaDrawCommand> commands, IReadOnlyList<AreaCoverage> coverage, AreaChart chart,
        IReadOnlyList<AreaRoofDraw> roofs)
    { WorldBounds = bounds; Commands = commands; Coverage = coverage; Chart = chart; Roofs = roofs; }
    /// <summary>ARC-RENDER-022: the roof blocks of a whole map, outside <see cref="Commands"/> (they are neither terrain nor
    /// bodies); none for a pinned profile.</summary>
    public IReadOnlyList<AreaRoofDraw> Roofs { get; }
    /// <summary>The frames the plan draws: its commands' and its roofs'.</summary>
    public IEnumerable<AreaFrameKey> Frames => Commands.Select(command => command.Frame).Concat(Roofs.Select(roof => roof.Command.Frame));
    /// <summary>The chart whose origin every projection and sort key of this plan is relative to.</summary>
    public AreaChart Chart { get; }
    /// <summary>ARC-RENDER-024: the same plan with its chart's spawn moved (a window move stands the actor where it walked
    /// meanwhile); every projection keeps the chart origin.</summary>
    public AreaScenePlan WithSpawn(Arcanum.Simulation.Movement.CellCoord spawn) => new(WorldBounds, Commands, Coverage, Chart with { Spawn = spawn }, Roofs);
    public TerrainRect WorldBounds { get; }
    public IReadOnlyList<AreaDrawCommand> Commands { get; }
    public IReadOnlyList<AreaCoverage> Coverage { get; }
    public int DrawableBodies => Commands.Count(command => command.Identity is not null);
    public const int CameraOriginX = 92928;
    public const int CameraOriginY = 82560;

    public static AreaScenePlan Create(AreaSnapshot snapshot, AreaChart? chart = null)
        => Create(snapshot.Terrain, snapshot.Objects, snapshot.Visuals, chart, snapshot.Roofs);

    // A value-input seam, also used by synthetic tests. It runs the complete
    // production disposition, placement and ordering rules, not pre-made draws.
    public static AreaScenePlan Create(IEnumerable<AreaTerrainRecord> terrain, IEnumerable<AreaObjectRecord> objects,
        IEnumerable<AreaVisualRecord> visuals, AreaChart? chart = null, IEnumerable<AreaRoofRecord>? roofs = null)
    {
        chart ??= AreaChart.Exterior;
        var catalog = visuals.ToDictionary(visual => visual.Key);
        // Each body's visual by its object (ARC-RENDER-019: a lookup, not a scan per body, so a
        // 43,219-object map plans in linear time). Two body visuals for one object stay an error.
        var bodyVisuals = new Dictionary<AreaObjectKey, AreaVisualRecord?>();
        foreach (var candidate in catalog.Values)
            if (candidate.Key.Object is { } owner && candidate.Key.Layer == AreaLayerKind.Body)
                bodyVisuals[owner] = bodyVisuals.ContainsKey(owner) ? null : candidate;
        var commands = new List<AreaDrawCommand>(); var coverage = new List<AreaCoverage>();
        foreach (var tile in terrain)
        {
            if (tile.Scope != AreaScope.Core)
            { coverage.Add(new(null, tile.Key, AreaDisposition.OutsideScope, "DATA009TerrainScope")); continue; }
            var visual = catalog[tile.SelectedVisual];
            var facade = tile.RawArt >> 28 != 0;
            var kind = facade ? TerrainKind.Facade : TerrainKind.Tile;
            var position = TerrainGeometry.Project(checked((int)(tile.WorldX - chart.OriginX)), checked((int)(tile.WorldY - chart.OriginY)));
            // Never clip to an individual sector: all nine sectors share the chart.
            var footprint = new TerrainRect(position.X + 1, position.Y, facade ? 80 : 78, 40);
            var source = new TerrainRect(0, 0, Math.Min(footprint.Width, visual.Width), Math.Min(40, visual.Height));
            var destination = new TerrainRect(footprint.X, footprint.Y, source.Width, source.Height);
            commands.Add(new(tile.Key, AreaFrameKey.From(visual), null, source, destination,
                kind == TerrainKind.Tile && (tile.RawArt & 0xC1) == 0xC1, false, facade ? 1 : 0,
                checked((int)tile.WorldX), checked((int)tile.WorldY), 0, 0, 255, false));
            coverage.Add(new(null, tile.Key, facade ? AreaDisposition.TerrainFacade : AreaDisposition.TerrainTile, "CoreTerrain"));
        }
        foreach (var body in objects)
        {
            var visual = bodyVisuals.TryGetValue(body.Key, out var found)
                ? found ?? throw new InvalidOperationException("AREA_VISUAL_AMBIGUOUS") : null;
            if (body.Scope != AreaScope.Core)
            { coverage.Add(new(body.Key, visual?.Key, AreaDisposition.OutsideScope, "DATA009Scope")); continue; }
            var state = State(body);
            if (state.Suppressed)
            { coverage.Add(new(body.Key, visual?.Key, AreaDisposition.Suppressed, state.Reason)); continue; }
            if (visual is null || body.WorldX is null || body.WorldY is null)
                throw new InvalidOperationException("AREA_VISUAL_REQUIRED");
            var phase = state.Flat ? state.UnderAll ? 2 : 3 : 4;
            var projected = TerrainGeometry.Project(checked((int)(body.WorldX.Value - chart.OriginX)), checked((int)(body.WorldY.Value - chart.OriginY)));
            var width = checked(visual.Width * state.Scale / 100) / (state.Shrunk ? 2 : 1);
            var height = checked(visual.Height * state.Scale / 100) / (state.Shrunk ? 2 : 1);
            if (width <= 0 || height <= 0) throw new InvalidOperationException("AREA_BODY_SCALE");
            var (hotX, hotY) = Hotspot(visual);
            var destination = new TerrainRect(checked(projected.X + visual.ObjectOffsetX + 40 - hotX * state.Scale / 100 / (state.Shrunk ? 2 : 1)),
                checked(projected.Y + visual.ObjectOffsetY + 20 - hotY * state.Scale / 100 / (state.Shrunk ? 2 : 1)), width, height);
            var ranks = SameCellRanks(visual.ObjectOffsetX, visual.ObjectOffsetY);
            commands.Add(new(visual.Key, AreaFrameKey.From(visual), body.Key, new(0, 0, visual.Width, visual.Height),
                destination, visual.Mirrored != state.FlipX, state.FlipY, phase, checked((int)body.WorldX.Value), checked((int)body.WorldY.Value),
                ranks.First, ranks.Second, state.Alpha, state.Additive)
            {
                ArtClass = (int)(visual.RawArt >> 28), Multiply = state.Multiply, Unlit = state.Unlit,
                // A flat wading body is drawn whole at the wading alpha; an upright one only in its lower rows.
                WadeRows = state.Wading ? state.Flat ? height : Math.Min(WadingRows, height) : 0,
            });
            coverage.Add(new(body.Key, visual.Key, state.Flat ? state.UnderAll ? AreaDisposition.FlatUnderAll : AreaDisposition.FlatBody
                : AreaDisposition.UprightBody, "DATA009ResolvedBody"));
        }
        if (commands.Count == 0) throw new InvalidOperationException("AREA_EMPTY_PLAN");
        var ordered = commands.OrderBy(command => command.Phase).ThenBy(command => command.MapY).ThenBy(command => command.MapX)
            .ThenBy(command => command.SecondRank).ThenBy(command => command.FirstRank)
            .ThenBy(command => command.Identity?.Kind).ThenBy(command => command.Identity?.SourceIndex)
            .ThenBy(command => command.Identity?.Offset).ThenBy(command => command.Visual.SourceIndex).ToArray();
        var left = commands.Min(command => command.Destination.X); var top = commands.Min(command => command.Destination.Y);
        var right = commands.Max(command => checked(command.Destination.X + command.Destination.Width));
        var bottom = commands.Max(command => checked(command.Destination.Y + command.Destination.Height));
        // A roof block is drawn from its normalized tile's screen origin moved 120 left and 200 up, less the frame's hot spot
        // (a mirrored roof frame takes hot spot x 0); the original passes no object offset (ledger RF1, RF7).
        var roofDraws = new List<AreaRoofDraw>();
        foreach (var roof in roofs ?? [])
        {
            var visual = catalog[roof.Visual];
            var (x, y) = AreaRoofGrid.ScreenPosition(roof.BlockX, roof.BlockY, (tx, ty) =>
            {
                var projected = TerrainGeometry.Project(checked((int)(tx - chart.OriginX)), checked((int)(ty - chart.OriginY)));
                return (projected.X, projected.Y);
            });
            var (hotX, hotY, _) = AreaRoofGrid.HotSpot(visual);
            var (nx, ny) = AreaRoofGrid.Normalize(roof.BlockX, roof.BlockY);
            roofDraws.Add(new(roof, new(visual.Key, AreaFrameKey.From(visual), null, new(0, 0, visual.Width, visual.Height),
                new(checked((int)(x - hotX)), checked((int)(y - hotY)), visual.Width, visual.Height), visual.Mirrored, false, 5,
                checked((int)nx), checked((int)ny), 0, 0, 255, false) { ArtClass = 10 }));
        }
        return new(new(left, top, checked(right - left), checked(bottom - top)), Array.AsReadOnly(ordered), coverage.AsReadOnly(), chart,
            roofDraws.AsReadOnly());
    }

    /// <summary>
    /// ARC-COMBAT-005: <paramref name="command"/> (the body of <paramref name="body"/> drawn from <paramref name="visual"/>) with
    /// another frame of a critter art, a fallen pose: the same object, visual key, cell, order and blend, its destination placed from
    /// the new frame's size and hotspot by the rule <see cref="Create"/> places a body with (scale and shrink of the object).
    /// </summary>
    public static AreaDrawCommand Repose(AreaDrawCommand command, AreaObjectRecord body, AreaVisualRecord visual, AreaFrameKey frame,
        int width, int height, int hotX, int hotY)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(body); ArgumentNullException.ThrowIfNull(visual);
        ArgumentNullException.ThrowIfNull(frame);
        if (command.Identity != body.Key || command.Visual != visual.Key || visual.RawArt >> 28 is 1 or 3) throw new ArgumentException("AREA_REPOSE_BODY");
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var state = State(body);
        int Scaled(int value) => checked(value * state.Scale / 100) / (state.Shrunk ? 2 : 1);
        // The anchor (projection + object offset + 40, 20) the T0 command was placed from.
        var anchorX = checked(command.Destination.X + visual.HotX * state.Scale / 100 / (state.Shrunk ? 2 : 1));
        var anchorY = checked(command.Destination.Y + visual.HotY * state.Scale / 100 / (state.Shrunk ? 2 : 1));
        var w = Scaled(width); var h = Scaled(height);
        if (w <= 0 || h <= 0) throw new InvalidOperationException("AREA_BODY_SCALE");
        var destination = new TerrainRect(checked(anchorX - hotX * state.Scale / 100 / (state.Shrunk ? 2 : 1)),
            checked(anchorY - hotY * state.Scale / 100 / (state.Shrunk ? 2 : 1)), w, h);
        return command with
        {
            Frame = frame, Source = new(0, 0, width, height), Destination = destination,
            WadeRows = command.WadeRows == 0 ? 0 : command.Phase == 4 ? Math.Min(WadingRows, h) : h,
        };
    }

    /// <summary>The drawn hotspot. Walls (art class 1, ARC-WORLD-009) and portals (class 3, doors and windows of
    /// whole maps, ARC-RENDER-017): rotations 0, 1, 6 and 7 move it 40 left and 20 down, then the flip bit mirrors
    /// it as width − x − 2 (research T7 and the pinned tig art.c 1235–1253). The pinned Crash Site profiles draw no
    /// portal; every other body uses its frame hotspot unchanged.</summary>
    public static (int X, int Y) Hotspot(AreaVisualRecord visual)
    {
        if (visual.RawArt >> 28 is not (1 or 3)) return (visual.HotX, visual.HotY);
        var rotation = (int)((visual.RawArt >> 11) & 7);
        var (x, y) = rotation is < 2 or > 5 ? (checked(visual.HotX - 40), checked(visual.HotY + 20)) : (visual.HotX, visual.HotY);
        return (visual.RawArt & 1) != 0 ? (checked(visual.Width - x - 2), y) : (x, y);
    }

    /// <summary>The integral local ordering coordinates of the original object
    /// footprint; C# integer division deliberately truncates toward zero.</summary>
    public static (int First, int Second) SameCellRanks(int offsetX, int offsetY)
    {
        // Pinned location.c:490-499 specifies a center-shifted inverse
        // offset rank, with TWO truncation points. This is not the ordinary
        // inverse isometric projection; odd and negative offsets differ.
        var horizontal = checked(offsetX - 40) / 2;
        var vertical = checked(2 * (offsetY / 2));
        return (checked(vertical - horizontal), checked(horizontal + vertical));
    }

    public static IReadOnlyList<AreaDrawCommand> UprightOrder(IEnumerable<AreaDrawCommand> commands,
        ActorDiagnosticPlacementInput placement, ActorDiagnosticDrawCommand actor, AreaObjectKey actorIdentity, AreaChart? chart = null)
    {
        var marker = ActorMarker(placement, actor, actorIdentity, chart ?? AreaChart.Exterior);
        return Array.AsReadOnly(commands.Where(command => command.Phase == 4).Append(marker).OrderBy(command => command.MapY)
            .ThenBy(command => command.MapX).ThenBy(command => command.SecondRank).ThenBy(command => command.FirstRank)
            .ThenBy(command => command.Identity?.Kind).ThenBy(command => command.Identity?.SourceIndex).ThenBy(command => command.Identity?.Offset).ToArray());
    }

    internal static AreaDrawCommand ActorMarker(ActorDiagnosticPlacementInput placement, ActorDiagnosticDrawCommand actor, AreaObjectKey actorIdentity, AreaChart chart)
    {
        var ranks = SameCellRanks(placement.InstanceOffsetX!.Value, placement.InstanceOffsetY!.Value);
        // ActorIdentity is a reserved session sentinel, never a DATA009 identity.
        return new AreaDrawCommand(default, default!, actorIdentity, default, actor.Destination, actor.FlipX, false, 4,
            checked((int)(chart.OriginX + placement.MapX!.Value)), checked((int)(chart.OriginY + placement.MapY!.Value)), ranks.First, ranks.Second, 255, false);
    }

    /// <summary>The seven <see cref="UprightOrder"/> keys, in order.</summary>
    internal static int CompareUpright(AreaDrawCommand a, AreaDrawCommand b)
    {
        var c = a.MapY.CompareTo(b.MapY); if (c != 0) return c;
        c = a.MapX.CompareTo(b.MapX); if (c != 0) return c;
        c = a.SecondRank.CompareTo(b.SecondRank); if (c != 0) return c;
        c = a.FirstRank.CompareTo(b.FirstRank); if (c != 0) return c;
        c = Comparer<AreaSourceKind?>.Default.Compare(a.Identity?.Kind, b.Identity?.Kind); if (c != 0) return c;
        c = Comparer<int?>.Default.Compare(a.Identity?.SourceIndex, b.Identity?.SourceIndex); if (c != 0) return c;
        return Comparer<long?>.Default.Compare(a.Identity?.Offset, b.Identity?.Offset);
    }

    /// <summary>The destination rows a wading upright body draws at <see cref="WadingAlpha"/> (pinned object.c 765–825).</summary>
    public const int WadingRows = 15;
    /// <summary>The constant alpha of the wading rows; it replaces every other alpha mode of those rows.</summary>
    public const int WadingAlpha = 92;

    private sealed record BodyState(bool Suppressed, string Reason, bool Flat, bool UnderAll, int Scale, bool Shrunk, int Alpha, bool Additive,
        bool FlipX, bool FlipY, bool Multiply, bool Unlit, bool Wading);
    private static BodyState State(AreaObjectRecord body)
    {
        long Field(int slot) => body.EffectiveFields.SingleOrDefault(field => field.Slot == slot && field.Presence == AreaPresence.Value)?.Number
            ?? throw new InvalidOperationException("AREA_MANDATORY_FIELD");
        var flags = unchecked((uint)Field(18)); var blit = unchecked((uint)Field(8));
        var reason = (flags & 0x1000) != 0 ? "ExteriorSectorInsertionRefusedByInventory" : (flags & 0x103) != 0
            ? "BodySuppressedBySerializedState" : (flags & 0x200) != 0 ? "InvisibleNoDetectionObserver" : "Eligible";
        var suppressed = reason != "Eligible";
        var scale = Field(11); var blitAlpha = Field(10);
        // ARC-RENDER-018, the original's blend choice (object_setup_blit, checked in the retail executable): additive
        // (0x10) wins over multiply (0x40), which wins over constant alpha (0x100); the serialized blit alpha is read
        // only by the constant-alpha branch. Any other blit bit (subtract, alpha modes, palettes) stays refused.
        var additive = (blit & 0x10) != 0; var multiply = !additive && (blit & 0x40) != 0;
        var constant = !additive && !multiply && (blit & 0x100) != 0;
        if (scale is <= 0 or > 200 || (blit & ~0x153u) != 0 || Field(9) != 0xFFFFFF
            || (constant ? blitAlpha is < 0 or > 255 : blitAlpha != 255))
            throw new InvalidOperationException("AREA_UNSUPPORTED_BODY_STATE");
        // Never silently approximate an unsupported source blend; a frozen body (OF_FROZEN) is drawn additive with a
        // constant colour before any blit blend is considered.
        if ((flags & 0x40) != 0) throw new InvalidOperationException("AREA_UNSUPPORTED_TRANSLUCENCY");
        if ((flags & 0x10000000) != 0) throw new InvalidOperationException("AREA_UNSUPPORTED_BODY_STATE");
        // Wading (OF_WADING without OF_WATER_WALKING) as serialized at T0. The wading rows replace only the alpha
        // modes, so an additive or multiply body keeps its blend over the whole frame and does not wade.
        var wading = (flags & 0x20000) != 0 && (flags & 0x40000) == 0 && !additive && !multiply;
        return new(suppressed, reason, (flags & 0x4) != 0, body.Type == 3 && (Field(36) & 0x200) != 0,
            (int)scale, (flags & 0x80) != 0, constant ? (int)blitAlpha : 255, additive, (blit & 1) != 0, (blit & 2) != 0,
            // ARC-RENDER-026: an additive, multiply or constant-alpha body is drawn with the base palette, unlit (ledger BL1, BL2).
            multiply, additive || multiply || constant, wading);
    }
}

/// <summary><see cref="AreaScenePlan.UprightOrder"/> for one fixed command list: the
/// bodies are stably sorted once, and each call only inserts the actor marker after
/// every equal-key body, exactly where the stable sort of bodies + appended marker
/// places it. The result is identical to <c>UprightOrder</c>; it is not a new order.</summary>
public sealed class AreaUprightOrder
{
    private readonly AreaDrawCommand[] _bodies;
    private readonly AreaChart _chart;
    public AreaUprightOrder(IEnumerable<AreaDrawCommand> commands, AreaChart? chart = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _chart = chart ?? AreaChart.Exterior;
        _bodies = commands.Where(command => command.Phase == 4).OrderBy(command => command.MapY)
            .ThenBy(command => command.MapX).ThenBy(command => command.SecondRank).ThenBy(command => command.FirstRank)
            .ThenBy(command => command.Identity?.Kind).ThenBy(command => command.Identity?.SourceIndex).ThenBy(command => command.Identity?.Offset).ToArray();
    }

    public IReadOnlyList<AreaDrawCommand> With(ActorDiagnosticPlacementInput placement, ActorDiagnosticDrawCommand actor, AreaObjectKey actorIdentity)
    {
        var marker = AreaScenePlan.ActorMarker(placement, actor, actorIdentity, _chart);
        var low = UpperBound(_bodies, _bodies.Length, marker);
        var ordered = new AreaDrawCommand[_bodies.Length + 1];
        Array.Copy(_bodies, ordered, low); ordered[low] = marker;
        Array.Copy(_bodies, low, ordered, low + 1, _bodies.Length - low);
        return Array.AsReadOnly(ordered);
    }

    /// <summary>ARC-WORLD-016: the same order with the bodies of <paramref name="moving"/>'s identities taken out of their
    /// T0 place and each moved command inserted after every body with an equal or smaller key, then the actor marker,
    /// exactly as a stable sort of the remaining bodies with the moved commands and the marker appended places them.</summary>
    public IReadOnlyList<AreaDrawCommand> With(ActorDiagnosticPlacementInput placement, ActorDiagnosticDrawCommand actor, AreaObjectKey actorIdentity,
        IReadOnlyList<AreaDrawCommand>? moving)
    {
        if (moving is null || moving.Count == 0) return With(placement, actor, actorIdentity);
        var keys = moving.Select(command => command.Identity).ToHashSet();
        var ordered = new AreaDrawCommand[_bodies.Length + moving.Count + 1];
        var count = 0;
        foreach (var body in _bodies) if (!keys.Contains(body.Identity)) ordered[count++] = body;
        foreach (var command in moving.Where(command => command.Phase == 4)) Insert(ordered, ref count, command);
        Insert(ordered, ref count, AreaScenePlan.ActorMarker(placement, actor, actorIdentity, _chart));
        return Array.AsReadOnly(ordered[..count]);
    }

    private static void Insert(AreaDrawCommand[] ordered, ref int count, AreaDrawCommand command)
    {
        var at = UpperBound(ordered, count, command);
        Array.Copy(ordered, at, ordered, at + 1, count - at);
        ordered[at] = command; count++;
    }

    // Upper bound: the first entry that orders strictly after the command.
    private static int UpperBound(AreaDrawCommand[] ordered, int count, AreaDrawCommand command)
    {
        int low = 0, high = count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (AreaScenePlan.CompareUpright(ordered[middle], command) <= 0) low = middle + 1; else high = middle;
        }
        return low;
    }
}
