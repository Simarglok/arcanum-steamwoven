using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.Simulation;

namespace Arcanum.World;

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

    /// <summary>ARC-MAGIC-045: the frame's hotspot as an offset inside <see cref="Destination"/> at the drawn size (set where a body is placed or re-placed from its frame, so a later shrink
    /// can scale the rectangle about it, <see cref="AreaBodyStyleRules"/>); null for a command that has none (the actor marker, a terrain tile, a body some seam moved without one).</summary>
    public (int X, int Y)? Hot { get; init; }

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
        IReadOnlyList<AreaRoofDraw> roofs, IReadOnlyDictionary<AreaObjectKey, IReadOnlyList<AreaVisualRecord>> portalFrames,
        IReadOnlyDictionary<AreaObjectKey, IReadOnlyList<AreaVisualRecord>> sceneryFrames)
    {
        WorldBounds = bounds; Commands = commands; Coverage = coverage; Chart = chart; Roofs = roofs; PortalFrames = portalFrames;
        SceneryFrames = sceneryFrames;
    }
    /// <summary>ARC-WORLD-051: every frame of each drawn door or window whose ART has more than one, in frame order (its T0
    /// body visual among them), for the open and close steps at runtime.</summary>
    public IReadOnlyDictionary<AreaObjectKey, IReadOnlyList<AreaVisualRecord>> PortalFrames { get; }
    /// <summary>ARC-RENDER-035: every frame of each drawn scenery whose ART has more than one and that the area owner decoded
    /// for its animation loop, in frame order (its T0 body visual among them).</summary>
    public IReadOnlyDictionary<AreaObjectKey, IReadOnlyList<AreaVisualRecord>> SceneryFrames { get; }
    /// <summary>ARC-RENDER-022: the roof blocks of a whole map, outside <see cref="Commands"/> (they are neither terrain nor
    /// bodies); none for a pinned profile.</summary>
    public IReadOnlyList<AreaRoofDraw> Roofs { get; }
    /// <summary>The frames the plan draws: its commands' and its roofs'.</summary>
    public IEnumerable<AreaFrameKey> Frames => Commands.Select(command => command.Frame).Concat(Roofs.Select(roof => roof.Command.Frame))
        .Concat(PortalFrames.Values.SelectMany(frames => frames).Select(AreaFrameKey.From))
        .Concat(SceneryFrames.Values.SelectMany(frames => frames).Select(AreaFrameKey.From));
    /// <summary>The chart whose origin every projection and sort key of this plan is relative to.</summary>
    public AreaChart Chart { get; }
    /// <summary>ARC-RENDER-024: the same plan with its chart's spawn moved (a window move stands the actor where it walked
    /// meanwhile); every projection keeps the chart origin.</summary>
    public AreaScenePlan WithSpawn(Arcanum.Simulation.Movement.CellCoord spawn) => new(WorldBounds, Commands, Coverage, Chart with { Spawn = spawn }, Roofs, PortalFrames,
        SceneryFrames);
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
            // ARC-DATA-038: an object whose ART id names no ART (ledger NA1) has no body to draw.
            if (visual is null && body.EffectiveFields.SingleOrDefault(f => f.Slot == 0 && f.Presence == AreaPresence.Value)?.Number is { } art
                && Arcanum.Data.Area.AreaDataOwner.HasNoBody(body.Type, unchecked((uint)art)))
            { coverage.Add(new(body.Key, null, AreaDisposition.Suppressed, "NoArt")); continue; }
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
            var ranks = BodyRanks(body.Type, visual.RawArt, visual.ObjectOffsetX, visual.ObjectOffsetY);
            commands.Add(new(visual.Key, AreaFrameKey.From(visual), body.Key, new(0, 0, visual.Width, visual.Height),
                destination, visual.Mirrored != state.FlipX, state.FlipY, phase, checked((int)body.WorldX.Value), checked((int)body.WorldY.Value),
                ranks.First, ranks.Second, state.Alpha, state.Additive)
            {
                Hot = (hotX * state.Scale / 100 / (state.Shrunk ? 2 : 1), hotY * state.Scale / 100 / (state.Shrunk ? 2 : 1)),
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
        // ARC-WORLD-051: the other frames of each drawn portal (the area owner decodes them for a portal ART of several frames).
        var drawn = commands.Where(command => command.Identity is not null && command.ArtClass == 3).Select(command => command.Identity!.Value).ToHashSet();
        var portalFrames = OtherFrames(AreaLayerKind.PortalFrame, drawn);
        // ARC-RENDER-035: the other frames of each drawn animated scenery (decoded on whole maps only).
        var bodies = commands.Where(command => command.Identity is not null).Select(command => command.Identity!.Value).ToHashSet();
        var sceneryFrames = OtherFrames(AreaLayerKind.SceneryFrame, bodies);
        return new(new(left, top, checked(right - left), checked(bottom - top)), Array.AsReadOnly(ordered), coverage.AsReadOnly(), chart,
            roofDraws.AsReadOnly(), portalFrames, sceneryFrames);
        Dictionary<AreaObjectKey, IReadOnlyList<AreaVisualRecord>> OtherFrames(AreaLayerKind layer, HashSet<AreaObjectKey> owners) =>
            catalog.Values.Where(v => v.Key.Layer == layer && v.Key.Object is { } owner && owners.Contains(owner))
                .GroupBy(v => v.Key.Object!.Value)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<AreaVisualRecord>)Array.AsReadOnly(g.Append(bodyVisuals[g.Key]!)
                    .OrderBy(v => (int)((v.RawArt >> 14) & 31)).ToArray()));
    }

    /// <summary>
    /// ARC-WORLD-051: <paramref name="command"/> (the T0 body of the portal <paramref name="body"/>, drawn from
    /// <paramref name="visual"/>) with another frame of its ART, <paramref name="frame"/>: the same object, cell, order and
    /// blend, its destination placed from the frame's size and drawn hotspot (<see cref="Hotspot"/>) by the rule
    /// <see cref="Create"/> places a body with.
    /// </summary>
    public static AreaDrawCommand PortalFrame(AreaDrawCommand command, AreaObjectRecord body, AreaVisualRecord visual, AreaVisualRecord frame)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(body); ArgumentNullException.ThrowIfNull(visual);
        ArgumentNullException.ThrowIfNull(frame);
        if (command.Identity != body.Key || command.Visual != visual.Key || visual.RawArt >> 28 != 3 || frame.Key.Object != body.Key)
            throw new ArgumentException("AREA_PORTAL_FRAME_BODY");
        return OtherFrame(command, body, visual, frame, 0, 0);
    }

    /// <summary>
    /// ARC-RENDER-035: <paramref name="command"/> (the T0 body of the scenery <paramref name="body"/>, drawn from
    /// <paramref name="visual"/>) on another frame of its animation loop, <paramref name="frame"/>, with the object offset the
    /// loop's frame deltas moved it by (<paramref name="offsetX"/>, <paramref name="offsetY"/>, ledger AS6): placed as
    /// <see cref="PortalFrame"/> places a portal's frame.
    /// </summary>
    public static AreaDrawCommand SceneryFrame(AreaDrawCommand command, AreaObjectRecord body, AreaVisualRecord visual, AreaVisualRecord frame,
        int offsetX, int offsetY)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(body); ArgumentNullException.ThrowIfNull(visual);
        ArgumentNullException.ThrowIfNull(frame);
        if (command.Identity != body.Key || command.Visual != visual.Key || body.Type != 3 || frame.Key.Object != body.Key)
            throw new ArgumentException("AREA_SCENERY_FRAME_BODY");
        return OtherFrame(command, body, visual, frame, offsetX, offsetY);
    }

    private static AreaDrawCommand OtherFrame(AreaDrawCommand command, AreaObjectRecord body, AreaVisualRecord visual, AreaVisualRecord frame,
        int offsetX, int offsetY)
    {
        var state = State(body);
        int Scaled(int value) => checked(value * state.Scale / 100) / (state.Shrunk ? 2 : 1);
        var (hotX, hotY) = Hotspot(visual); var (frameX, frameY) = Hotspot(frame);
        var w = Scaled(frame.Width); var h = Scaled(frame.Height);
        if (w <= 0 || h <= 0) throw new InvalidOperationException("AREA_BODY_SCALE");
        var destination = new TerrainRect(checked(command.Destination.X + offsetX + (hotX - frameX) * state.Scale / 100 / (state.Shrunk ? 2 : 1)),
            checked(command.Destination.Y + offsetY + (hotY - frameY) * state.Scale / 100 / (state.Shrunk ? 2 : 1)), w, h);
        return command with
        {
            // The visual key stays the body's (ambient channel, pick and wall check go by it); only the frame changes.
            Frame = AreaFrameKey.From(frame), Source = new(0, 0, frame.Width, frame.Height), Destination = destination,
            WadeRows = command.WadeRows == 0 ? 0 : command.Phase == 4 ? Math.Min(WadingRows, h) : h,
            Hot = (frameX * state.Scale / 100 / (state.Shrunk ? 2 : 1), frameY * state.Scale / 100 / (state.Shrunk ? 2 : 1)),
        };
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
            Hot = (hotX * state.Scale / 100 / (state.Shrunk ? 2 : 1), hotY * state.Scale / 100 / (state.Shrunk ? 2 : 1)),
        };
    }

    /// <summary>
    /// ARC-RENDER-033: a body the T0 plan does not hold (an item lying where no T0 body stood), drawn from <paramref name="visual"/>
    /// at world cell (<paramref name="worldX"/>, <paramref name="worldY"/>) by the rule <see cref="Create"/> places a body with: no
    /// object offset, scale 100, no blend, upright (phase 4) unless <paramref name="flat"/> (phase 3).
    /// </summary>
    public static AreaDrawCommand Place(AreaChart chart, AreaObjectKey identity, AreaVisualRecord visual, long worldX, long worldY, bool flat)
    {
        ArgumentNullException.ThrowIfNull(chart); ArgumentNullException.ThrowIfNull(visual);
        if (visual.Width <= 0 || visual.Height <= 0 || visual.RawArt >> 28 is 1 or 3) throw new ArgumentException("AREA_PLACE_BODY");
        var projected = TerrainGeometry.Project(checked((int)(worldX - chart.OriginX)), checked((int)(worldY - chart.OriginY)));
        var (hotX, hotY) = Hotspot(visual);
        var destination = new TerrainRect(checked(projected.X + 40 - hotX), checked(projected.Y + 20 - hotY), visual.Width, visual.Height);
        var ranks = SameCellRanks(0, 0);
        return new(visual.Key, AreaFrameKey.From(visual), identity, new(0, 0, visual.Width, visual.Height), destination, visual.Mirrored, false,
            flat ? 3 : 4, checked((int)worldX), checked((int)worldY), ranks.First, ranks.Second, 255, false) { ArtClass = (int)(visual.RawArt >> 28), Hot = (hotX, hotY) };
    }

    /// <summary>
    /// ARC-MAGIC-054: <paramref name="command"/> (a body made by <see cref="Place"/>) drawn with the blend the object's blit flags choose, as the map's own bodies get it in <c>State</c>
    /// (the original's <c>object_setup_blit</c>, ARC-RENDER-018): additive (0x10) wins over multiply (0x40), which wins over constant alpha (0x100, <paramref name="blitAlpha"/>); each of these three is drawn
    /// unlit (ARC-RENDER-026); the mirror bits 0x1 and 0x2 flip the body across x and y. A created scenery has no serialized blit state, so its prototype's flags are the object's (a wall of fire's
    /// piece, prototype file 4051, is additive: its black frame background adds nothing instead of being painted over the ground). Any other bit draws nothing different.
    /// </summary>
    public static AreaDrawCommand WithBlend(AreaDrawCommand command, uint blit, int blitAlpha)
    {
        ArgumentNullException.ThrowIfNull(command);
        var additive = (blit & 0x10) != 0; var multiply = !additive && (blit & 0x40) != 0;
        var constant = !additive && !multiply && (blit & 0x100) != 0 && blitAlpha is >= 0 and <= 255;
        return command with
        {
            FlipX = command.FlipX != ((blit & 1) != 0), FlipY = (blit & 2) != 0,
            Alpha = constant ? blitAlpha : command.Alpha, Additive = additive, Multiply = multiply, Unlit = additive || multiply || constant,
        };
    }

    /// <summary>
    /// ARC-UI-063 (PF11): <paramref name="command"/> (a body placed at full size by <see cref="Place"/>) drawn at <paramref name="percent"/> of its size
    /// around its hotspot, the way an object's scale field scales a body (width and height by the percent, the hotspot staying on the tile's
    /// centre). PROJECT: the original's renderer was not read for this; the scene plan's own scaling of a scaled object is the model.
    /// </summary>
    public static AreaDrawCommand ScaledAboutHotspot(AreaDrawCommand command, AreaVisualRecord visual, int percent)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(visual);
        if (percent <= 0) throw new ArgumentOutOfRangeException(nameof(percent));
        if (percent == 100) return command;
        var (hotX, hotY) = Hotspot(visual);
        var at = command.Destination;
        var width = Math.Max(1, checked(at.Width * percent / 100));
        var height = Math.Max(1, checked(at.Height * percent / 100));
        return command with
        {
            Destination = new TerrainRect(checked(at.X + hotX - hotX * percent / 100), checked(at.Y + hotY - hotY * percent / 100), width, height),
            Hot = (hotX * percent / 100, hotY * percent / 100),
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

    /// <summary>
    /// ARC-DEV-173: the same-cell ranks of an object as the original's tile list sorts it (insert 0x4F20A0, rank 0x4F2230, offset
    /// rank 0x4B93F0; read in the retail executable). A wall (type 0) or portal (type 1) ignores its offsets: its offset is
    /// (0, 19) for the ART rotations 2 to 5 (the walls on the near sides of a building, drawn after what stands on the tile) and
    /// (0, -20) for every other rotation (drawn before it); every other type ranks by its own offsets
    /// (<see cref="SameCellRanks"/>). <paramref name="rawArt"/> is the object's current art id.
    /// </summary>
    public static (int First, int Second) BodyRanks(int type, uint rawArt, int offsetX, int offsetY)
        => type is 0 or 1 ? SameCellRanks(0, ArtRotation(rawArt) is >= 2 and <= 5 ? 19 : -20) : SameCellRanks(offsetX, offsetY);

    /// <summary>The ART rotation of an art id (the original's 0x502880): none for the art classes 0, 5, 6, 8, 10 and 11, bits 9..11
    /// for the classes 9 and 14, bits 11..13 for every other class.</summary>
    [OriginalFunction(0x502880, OriginalFunctionScope.Full)]
    public static int ArtRotation(uint art) => (art >> 28) switch
    {
        0 or 5 or 6 or 8 or 10 or 11 => 0,
        9 or 14 => (int)((art >> 9) & 7),
        _ => (int)((art >> 11) & 7),
    };

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

    /// <summary>ARC-RENDER-033: the plan's order of two commands (phase first, then the upright order and the visual's index), as
    /// <see cref="Create"/> sorts them.</summary>
    public static int ComparePlan(AreaDrawCommand a, AreaDrawCommand b)
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b);
        var c = a.Phase.CompareTo(b.Phase); if (c != 0) return c;
        c = CompareUpright(a, b); if (c != 0) return c;
        return a.Visual.SourceIndex.CompareTo(b.Visual.SourceIndex);
    }

    /// <summary>ARC-RENDER-033: <paramref name="flat"/> (commands in plan order) less the bodies <paramref name="movingKeys"/> name,
    /// with the flat ones of <paramref name="moving"/> (phase below 4) inserted in plan order.</summary>
    public static IReadOnlyList<AreaDrawCommand> WithMovingFlat(IEnumerable<AreaDrawCommand> flat, IReadOnlyList<AreaDrawCommand>? moving,
        IReadOnlySet<AreaObjectKey?> movingKeys)
    {
        ArgumentNullException.ThrowIfNull(flat); ArgumentNullException.ThrowIfNull(movingKeys);
        var list = flat.Where(command => command.Identity is null || !movingKeys.Contains(command.Identity)).ToList();
        foreach (var command in (moving ?? []).Where(command => command.Phase < 4))
        {
            int low = 0, high = list.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (ComparePlan(list[middle], command) <= 0) low = middle + 1; else high = middle;
            }
            list.Insert(low, command);
        }
        return list;
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
        var reason = (flags & 0x1000) != 0 ? "ExteriorSectorInsertionRefusedByInventory" : (flags & (body.Type == 16 ? 0x101u : 0x103u)) != 0
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
        // ARC-RENDER-035: a moved command whose key equals its T0 body's (an animated scenery on another frame) takes the T0
        // place when no other body or moved command shares that key, which is where the insertion below would put it; so a frame
        // change costs no insertion.
        var upright = moving.Where(command => command.Phase == 4).ToArray();
        var inPlace = upright.Where(command => command.Identity is not null)
            .GroupBy(command => command.Identity!.Value).Where(g => g.Count() == 1).Select(g => g.Single())
            .Where(command => upright.Count(other => AreaScenePlan.CompareUpright(other, command) == 0) == 1)
            .ToDictionary(command => command.Identity!.Value);
        var placed = new HashSet<AreaDrawCommand>(ReferenceEqualityComparer.Instance);
        var ordered = new AreaDrawCommand[_bodies.Length + moving.Count + 1];
        var count = 0;
        for (var i = 0; i < _bodies.Length; i++)
        {
            var body = _bodies[i];
            if (!keys.Contains(body.Identity)) { ordered[count++] = body; continue; }
            if (body.Identity is { } identity && inPlace.TryGetValue(identity, out var moved) && AreaScenePlan.CompareUpright(body, moved) == 0
                && (i == 0 || AreaScenePlan.CompareUpright(_bodies[i - 1], moved) < 0)
                && (i + 1 == _bodies.Length || AreaScenePlan.CompareUpright(_bodies[i + 1], moved) > 0))
            { ordered[count++] = moved; placed.Add(moved); }
        }
        foreach (var command in upright.Where(command => !placed.Contains(command))) Insert(ordered, ref count, command);
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
