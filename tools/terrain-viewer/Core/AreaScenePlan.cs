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
    int FirstRank, int SecondRank, int Alpha, bool Additive);

/// <summary>One world chart and one stable immutable body policy. Body appearance
/// belongs to DATA009; footprint terrain cropping belongs to the established
/// diagnostic TerrainGeometry seam. No ART/resource lookup occurs here.</summary>
public sealed class AreaScenePlan
{
    private AreaScenePlan(TerrainRect bounds, IReadOnlyList<AreaDrawCommand> commands, IReadOnlyList<AreaCoverage> coverage)
    { WorldBounds = bounds; Commands = commands; Coverage = coverage; }
    public TerrainRect WorldBounds { get; }
    public IReadOnlyList<AreaDrawCommand> Commands { get; }
    public IReadOnlyList<AreaCoverage> Coverage { get; }
    public int DrawableBodies => Commands.Count(command => command.Identity is not null);
    public const int CameraOriginX = 92928;
    public const int CameraOriginY = 82560;

    public static AreaScenePlan Create(AreaSnapshot snapshot)
        => Create(snapshot.Terrain, snapshot.Objects, snapshot.Visuals);

    // A value-input seam, also used by synthetic tests. It runs the complete
    // production disposition, placement and ordering rules, not pre-made draws.
    public static AreaScenePlan Create(IEnumerable<AreaTerrainRecord> terrain, IEnumerable<AreaObjectRecord> objects,
        IEnumerable<AreaVisualRecord> visuals)
    {
        var catalog = visuals.ToDictionary(visual => visual.Key);
        var commands = new List<AreaDrawCommand>(); var coverage = new List<AreaCoverage>();
        foreach (var tile in terrain)
        {
            if (tile.Scope != AreaScope.Core)
            { coverage.Add(new(null, tile.Key, AreaDisposition.OutsideScope, "DATA009TerrainScope")); continue; }
            var visual = catalog[tile.SelectedVisual];
            var facade = tile.RawArt >> 28 != 0;
            var kind = facade ? TerrainKind.Facade : TerrainKind.Tile;
            var position = TerrainGeometry.Project(checked((int)(tile.WorldX - CameraOriginX)), checked((int)(tile.WorldY - CameraOriginY)));
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
            var visual = catalog.Values.SingleOrDefault(candidate => candidate.Key.Object == body.Key && candidate.Key.Layer == AreaLayerKind.Body);
            if (body.Scope != AreaScope.Core)
            { coverage.Add(new(body.Key, visual?.Key, AreaDisposition.OutsideScope, "DATA009Scope")); continue; }
            var state = State(body);
            if (state.Suppressed)
            { coverage.Add(new(body.Key, visual?.Key, AreaDisposition.Suppressed, state.Reason)); continue; }
            if (visual is null || body.WorldX is null || body.WorldY is null)
                throw new InvalidOperationException("AREA_VISUAL_REQUIRED");
            var phase = state.Flat ? state.UnderAll ? 2 : 3 : 4;
            var projected = TerrainGeometry.Project(checked((int)(body.WorldX.Value - CameraOriginX)), checked((int)(body.WorldY.Value - CameraOriginY)));
            var width = checked(visual.Width * state.Scale / 100) / (state.Shrunk ? 2 : 1);
            var height = checked(visual.Height * state.Scale / 100) / (state.Shrunk ? 2 : 1);
            if (width <= 0 || height <= 0) throw new InvalidOperationException("AREA_BODY_SCALE");
            var (hotX, hotY) = Hotspot(visual);
            var destination = new TerrainRect(checked(projected.X + visual.ObjectOffsetX + 40 - hotX * state.Scale / 100 / (state.Shrunk ? 2 : 1)),
                checked(projected.Y + visual.ObjectOffsetY + 20 - hotY * state.Scale / 100 / (state.Shrunk ? 2 : 1)), width, height);
            var ranks = SameCellRanks(visual.ObjectOffsetX, visual.ObjectOffsetY);
            commands.Add(new(visual.Key, AreaFrameKey.From(visual), body.Key, new(0, 0, visual.Width, visual.Height),
                destination, visual.Mirrored != state.FlipX, state.FlipY, phase, checked((int)body.WorldX.Value), checked((int)body.WorldY.Value),
                ranks.First, ranks.Second, state.Alpha, state.Additive));
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
        return new(new(left, top, checked(right - left), checked(bottom - top)), Array.AsReadOnly(ordered), coverage.AsReadOnly());
    }

    /// <summary>The drawn hotspot. Walls (art class 1, ARC-WORLD-009): rotations 0, 1, 6 and 7 move it
    /// 40 left and 20 down, then the flip bit mirrors it as width − x − 2 (research T7 and the pinned
    /// tig art.c 1235–1253). The source applies the same shift to portal art, which no admitted core
    /// draws; every other body uses its frame hotspot unchanged.</summary>
    public static (int X, int Y) Hotspot(AreaVisualRecord visual)
    {
        if (visual.RawArt >> 28 != 1) return (visual.HotX, visual.HotY);
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
        ActorDiagnosticPlacementInput placement, ActorDiagnosticDrawCommand actor, AreaObjectKey actorIdentity)
    {
        var marker = ActorMarker(placement, actor, actorIdentity);
        return Array.AsReadOnly(commands.Where(command => command.Phase == 4).Append(marker).OrderBy(command => command.MapY)
            .ThenBy(command => command.MapX).ThenBy(command => command.SecondRank).ThenBy(command => command.FirstRank)
            .ThenBy(command => command.Identity?.Kind).ThenBy(command => command.Identity?.SourceIndex).ThenBy(command => command.Identity?.Offset).ToArray());
    }

    internal static AreaDrawCommand ActorMarker(ActorDiagnosticPlacementInput placement, ActorDiagnosticDrawCommand actor, AreaObjectKey actorIdentity)
    {
        var ranks = SameCellRanks(placement.InstanceOffsetX!.Value, placement.InstanceOffsetY!.Value);
        // ActorIdentity is a reserved session sentinel, never a DATA009 identity.
        return new AreaDrawCommand(default, default!, actorIdentity, default, actor.Destination, actor.FlipX, false, 4,
            checked(CameraOriginX + (int)placement.MapX!.Value), checked(CameraOriginY + (int)placement.MapY!.Value), ranks.First, ranks.Second, 255, false);
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

    private sealed record BodyState(bool Suppressed, string Reason, bool Flat, bool UnderAll, int Scale, bool Shrunk, int Alpha, bool Additive, bool FlipX, bool FlipY);
    private static BodyState State(AreaObjectRecord body)
    {
        long Field(int slot) => body.EffectiveFields.SingleOrDefault(field => field.Slot == slot && field.Presence == AreaPresence.Value)?.Number
            ?? throw new InvalidOperationException("AREA_MANDATORY_FIELD");
        var flags = unchecked((uint)Field(18)); var blit = unchecked((uint)Field(8));
        var reason = (flags & 0x1000) != 0 ? "ExteriorSectorInsertionRefusedByInventory" : (flags & 0x103) != 0
            ? "BodySuppressedBySerializedState" : (flags & 0x200) != 0 ? "InvisibleNoDetectionObserver" : "Eligible";
        var suppressed = reason != "Eligible";
        var scale = Field(11);
        if (scale is <= 0 or > 200 || (blit & ~0x13u) != 0 || Field(9) != 0xFFFFFF || Field(10) != 255 || (flags & 0x20000) != 0)
            throw new InvalidOperationException("AREA_UNSUPPORTED_BODY_STATE");
        // The authenticated corpus has opaque and additive (0x10) bodies only.
        // Never silently approximate an unsupported source blend.
        if ((flags & 0x40) != 0) throw new InvalidOperationException("AREA_UNSUPPORTED_TRANSLUCENCY");
        return new(suppressed, reason, (flags & 0x4) != 0, body.Type == 3 && (Field(36) & 0x200) != 0,
            (int)scale, (flags & 0x80) != 0, 255, (blit & 0x10) != 0, (blit & 1) != 0, (blit & 2) != 0);
    }
}

/// <summary><see cref="AreaScenePlan.UprightOrder"/> for one fixed command list: the
/// bodies are stably sorted once, and each call only inserts the actor marker after
/// every equal-key body, exactly where the stable sort of bodies + appended marker
/// places it. The result is identical to <c>UprightOrder</c>; it is not a new order.</summary>
public sealed class AreaUprightOrder
{
    private readonly AreaDrawCommand[] _bodies;
    public AreaUprightOrder(IEnumerable<AreaDrawCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _bodies = commands.Where(command => command.Phase == 4).OrderBy(command => command.MapY)
            .ThenBy(command => command.MapX).ThenBy(command => command.SecondRank).ThenBy(command => command.FirstRank)
            .ThenBy(command => command.Identity?.Kind).ThenBy(command => command.Identity?.SourceIndex).ThenBy(command => command.Identity?.Offset).ToArray();
    }

    public IReadOnlyList<AreaDrawCommand> With(ActorDiagnosticPlacementInput placement, ActorDiagnosticDrawCommand actor, AreaObjectKey actorIdentity)
    {
        var marker = AreaScenePlan.ActorMarker(placement, actor, actorIdentity);
        // Upper bound: the first body that orders strictly after the marker.
        int low = 0, high = _bodies.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (AreaScenePlan.CompareUpright(_bodies[middle], marker) <= 0) low = middle + 1; else high = middle;
        }
        var ordered = new AreaDrawCommand[_bodies.Length + 1];
        Array.Copy(_bodies, ordered, low); ordered[low] = marker;
        Array.Copy(_bodies, low, ordered, low + 1, _bodies.Length - low);
        return Array.AsReadOnly(ordered);
    }
}
