using System.Collections.ObjectModel;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;

namespace Arcanum.TerrainViewer.Core;

/// <summary>A body's light colour. Walls carry their two base-line ends and are interpolated across the
/// sprite's width (the source's two-colour path); every other body has one colour (Left == Right).</summary>
public readonly record struct AreaLitColors(AmbientColor Left, AmbientColor Right)
{
    public static AreaLitColors Neutral { get; } = new(AmbientColor.Neutral, AmbientColor.Neutral);
    public bool Gradient => Left != Right;
}

/// <summary>The local light view of one hour: the terrain lattice (null when no light is active) and the
/// colour of every body command, keyed by its visual; <see cref="Actor"/> colours the controlled actor
/// at its current placement.</summary>
public sealed record AreaLocalLightView(StationaryLightLattice? Lattice, IReadOnlyDictionary<AreaVisualKey, AreaLitColors> Bodies,
    Func<ActorDiagnosticPlacementInput, AmbientColor> Actor);

/// <summary>
/// ARC-RENDER-012 local lighting of one map chart: its stationary sector lights (FR-LIGHT-008) and the
/// tile channel under each core cell (FR-LIGHT-006). Bodies follow the pinned CE object colour
/// (light.c sub_4DC210 / sub_4D89E0): the ambient of the tile class under the body's cell plus the lights
/// at its anchor (tile + offset + (40, 20)). Walls sample their base line at the sprite's two ends, from
/// lights on their front side only, with the tile class for rotations 0/6 and outdoor for 2/4. Bodies
/// whose ART channel is unlit stay unlit. Terrain uses the lattice over its own tile channel.
/// Independently written; TIG rounding and LERP parity NOT_VERIFIED.
/// </summary>
public sealed class AreaLocalLighting
{
    private readonly IReadOnlyDictionary<(long X, long Y), AreaAmbientChannel> _tiles;
    private readonly IReadOnlyDictionary<AreaVisualKey, AreaVisualRecord> _visuals;

    // The lattice depends on the hour only through INDOOR/OUTDOOR-tinted lights; otherwise it is built once.
    private readonly bool _hourIndependent;
    private readonly StationaryLightLattice? _lattice;

    private AreaLocalLighting(StationaryLightField field, AreaChart chart, Dictionary<(long, long), AreaAmbientChannel> tiles,
        Dictionary<AreaVisualKey, AreaVisualRecord> visuals)
    {
        Field = field; Chart = chart; _tiles = tiles; _visuals = visuals;
        _hourIndependent = !field.Source.Lights.Any(light => (light.Flags & (StationaryLightFlags.Indoor | StationaryLightFlags.Outdoor)) != 0);
        // Built at load either way, so a lattice over its node cap fails the load instead of a draw.
        var lattice = field.Lattice(AmbientPair.Neutral);
        if (_hourIndependent) _lattice = lattice;
    }

    public StationaryLightField Field { get; }
    public AreaChart Chart { get; }
    public int LightCount => Field.Source.Lights.Count;

    public static AreaLocalLighting Create(AreaSnapshot snapshot, AreaChart chart, AreaStationaryLightSource lights)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Create(snapshot.Terrain, snapshot.Visuals, chart, lights);
    }

    // Value-input seam shared with synthetic tests.
    public static AreaLocalLighting Create(IEnumerable<AreaTerrainRecord> terrain, IEnumerable<AreaVisualRecord> visuals, AreaChart chart,
        AreaStationaryLightSource lights)
    {
        ArgumentNullException.ThrowIfNull(terrain); ArgumentNullException.ThrowIfNull(visuals);
        ArgumentNullException.ThrowIfNull(chart); ArgumentNullException.ThrowIfNull(lights);
        var tiles = new Dictionary<(long, long), AreaAmbientChannel>();
        // The source reads the sector's tile ART id under a location; it may be a tile (class bit 8) or a
        // facade id (bit 25), both classified like the terrain channel.
        foreach (var tile in terrain.Where(tile => tile.Scope == AreaScope.Core))
            if (!tiles.TryAdd((tile.WorldX, tile.WorldY), AreaAmbientPolicy.Channel(tile.RawArt))) throw new InvalidOperationException("AREA_LIGHT_TILE_DUPLICATE");
        var catalog = new Dictionary<AreaVisualKey, AreaVisualRecord>();
        foreach (var visual in visuals) catalog.TryAdd(visual.Key, visual);
        return new(new StationaryLightField(lights, chart.OriginX, chart.OriginY), chart, tiles, catalog);
    }

    /// <summary>The tile channel under a world cell; a cell without a core tile is an error, not a default.</summary>
    public AreaAmbientChannel TileChannel(long worldX, long worldY) =>
        _tiles.TryGetValue((worldX, worldY), out var channel) ? channel : throw new InvalidOperationException("AREA_LIGHT_TILE_REQUIRED");

    public static AmbientColor Ambient(AreaAmbientChannel channel, AmbientPair pair) => channel switch
    {
        AreaAmbientChannel.Outdoor => pair.Outdoor,
        AreaAmbientChannel.Indoor => pair.Indoor,
        _ => AmbientColor.Neutral,
    };

    private TerrainPoint TileScreen(long worldX, long worldY) =>
        TerrainGeometry.Project(checked((int)(worldX - Chart.OriginX)), checked((int)(worldY - Chart.OriginY)));

    /// <summary>The colour of one body command (Identity set) for the given ART channel and hour;
    /// <paramref name="lit"/> false gives the same rule without any stationary light (the reference a light
    /// observation must differ from).</summary>
    public AreaLitColors Body(AreaDrawCommand command, AreaAmbientChannel artChannel, AmbientPair pair, bool lit = true)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Identity is null) throw new ArgumentException("A body command is required.", nameof(command));
        if (artChannel == AreaAmbientChannel.Unlit) return AreaLitColors.Neutral;
        if (!_visuals.TryGetValue(command.Visual, out var visual)) throw new InvalidOperationException("AREA_AMBIENT_VISUAL_REQUIRED");
        return Body(command, visual, artChannel, pair, lit);
    }

    /// <summary>ARC-WORLD-017: the same rule for a body whose visual record comes from another map (a companion
    /// standing here): the colour is taken at the command's cell on this map.</summary>
    public AreaLitColors Body(AreaDrawCommand command, AreaVisualRecord visual, AreaAmbientChannel artChannel, AmbientPair pair, bool lit = true)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(visual);
        if (command.Identity is null) throw new ArgumentException("A body command is required.", nameof(command));
        if (artChannel == AreaAmbientChannel.Unlit) return AreaLitColors.Neutral;
        if (visual.RawArt >> 28 == 1) return Wall(command, visual, pair, lit);
        var color = lit ? Point(command.MapX, command.MapY, visual.ObjectOffsetX, visual.ObjectOffsetY, pair)
            : Ambient(TileChannel(command.MapX, command.MapY), pair);
        return new(color, color);
    }

    /// <summary>The source's non-wall object colour: tile-class ambient plus the lights at the anchor.</summary>
    public AmbientColor Point(long worldX, long worldY, int offsetX, int offsetY, AmbientPair pair)
    {
        var tile = TileScreen(worldX, worldY);
        return Field.Point(checked(tile.X + offsetX + 40L), checked(tile.Y + offsetY + 20L), Ambient(TileChannel(worldX, worldY), pair), pair);
    }

    /// <summary>The controlled actor (a critter) at its chart-relative placement.</summary>
    public AmbientColor Actor(ActorDiagnosticPlacementInput placement, AmbientPair pair)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (placement.MapX is not { } x || placement.MapY is not { } y) throw new InvalidOperationException("AREA_LIGHT_ACTOR_PLACEMENT");
        return Point(checked(Chart.OriginX + x), checked(Chart.OriginY + y), placement.InstanceOffsetX ?? 0, placement.InstanceOffsetY ?? 0, pair);
    }

    // light.c sub_4DC210, wall branch: odd rotations fold to the even one below; the base line starts at a
    // rotation-specific tile point, is shifted to the sprite's left edge and steps one pixel in y every
    // two pixels in x across the sprite width rounded up to even. Every base-line point lies in the band the
    // source intersects with each light, so only the light rectangle and the wall's front side (by tile
    // coordinate) decide. The two end colours are the source's hardware two-colour form.
    private AreaLitColors Wall(AreaDrawCommand command, AreaVisualRecord visual, AmbientPair pair, bool lit)
    {
        var rotation = (int)((visual.RawArt >> 11) & 7) & ~1;
        var tile = TileScreen(command.MapX, command.MapY);
        long x = tile.X, y = tile.Y; int step;
        var channel = rotation is 0 or 6 ? TileChannel(command.MapX, command.MapY) : AreaAmbientChannel.Outdoor;
        switch (rotation)
        {
            case 0: x += 40; step = 1; break;
            case 2: x += 40; y += 39; step = -1; break;
            case 4: y += 20; step = 1; break;
            default: y += 20; step = -1; break;
        }
        var rect = command.Destination;
        // Literal source arithmetic (one y per x pixel of the shift); kept as read, parity NOT_VERIFIED.
        y += (rect.X - x) * step; x = rect.X;
        var width = rect.Width + (rect.Width & 1);
        var baseColor = Ambient(channel, pair);
        long wallX = command.MapX, wallY = command.MapY;
        bool Front(StationaryLight light) => rotation switch
        {
            0 => light.X >= wallX,
            2 => light.Y > wallY,
            4 => light.X > wallX,
            _ => light.Y >= wallY,
        };
        AmbientColor At(int column) => lit ? Field.Point(x + column, y + column / 2 * step, baseColor, pair, Front) : baseColor;
        return new(At(0), At(width - 1));
    }

    /// <summary>The whole view for one hour: lattice and every body command's colour.</summary>
    public AreaLocalLightView View(AreaScenePlan plan, IReadOnlyDictionary<AreaVisualKey, AreaAmbientChannel> channels, AmbientPair pair)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(channels);
        var bodies = new Dictionary<AreaVisualKey, AreaLitColors>();
        foreach (var command in plan.Commands.Where(command => command.Identity is not null))
            bodies[command.Visual] = Body(command, channels.TryGetValue(command.Visual, out var channel) ? channel
                : throw new InvalidOperationException("AREA_AMBIENT_VISUAL_REQUIRED"), pair);
        return new(_hourIndependent ? _lattice : Field.Lattice(pair), new ReadOnlyDictionary<AreaVisualKey, AreaLitColors>(bodies), placement => Actor(placement, pair));
    }
}
