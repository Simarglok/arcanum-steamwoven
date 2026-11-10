using System.Collections.ObjectModel;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.World;

namespace Arcanum.World;

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
/// tile channel under each core cell (FR-LIGHT-006). Bodies follow the source's object colour
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

    // ARC-RENDER-030: with object lights the night and the day differ (nocturnal scenery, ledger LT4/LT5); the night form
    // keeps the day form here. Without them both are this one.
    private AreaLocalLighting? _day;
    // ARC-RENDER-031: the animated lights' poses of this view (null: the loaded poses).
    private readonly IReadOnlyDictionary<int, LightPose>? _poses;
    // ARC-DEV-335 (V7d): the critters' carried lights of this view, sampled after the sector's lights (null: none).
    private readonly StationaryLightField? _carried;

    private AreaLocalLighting(StationaryLightField field, AreaChart chart, Dictionary<(long, long), AreaAmbientChannel> tiles,
        Dictionary<AreaVisualKey, AreaVisualRecord> visuals)
    {
        Field = field; Chart = chart; _tiles = tiles; _visuals = visuals;
        _hourIndependent = !field.Source.Lights.Any(light => (light.Flags & (StationaryLightFlags.Indoor | StationaryLightFlags.Outdoor)) != 0);
        // Built at load either way, so a lattice over its node cap fails the load instead of a draw.
        var lattice = field.Lattice(AmbientPair.Neutral);
        if (_hourIndependent) _lattice = lattice;
    }

    // A view of the same lighting with the animated lights posed (shares the field, tiles and visuals).
    private AreaLocalLighting(AreaLocalLighting basis, IReadOnlyDictionary<int, LightPose> poses)
    {
        Field = basis.Field; Chart = basis.Chart; _tiles = basis._tiles; _visuals = basis._visuals;
        _hourIndependent = false; _lattice = null; _poses = poses;
    }

    // A view of the same lighting with the critters' carried lights (shares the field, tiles, visuals and poses).
    private AreaLocalLighting(AreaLocalLighting basis, StationaryLightField? carried)
    {
        Field = basis.Field; Chart = basis.Chart; _tiles = basis._tiles; _visuals = basis._visuals;
        _hourIndependent = false; _lattice = null; _poses = basis._poses; _carried = carried;
    }

    /// <summary>
    /// ARC-DEV-335 (V7d, ledger LT10, LT12): this lighting with the critters' carried lights standing where the critters stand now.
    /// They are sampled after the sector's lights (the order between the two kinds is not read, NOT_VERIFIED) and never dark; the
    /// view shares everything else, so it is cheap to make for every step. Take <see cref="AtHour"/> and <see cref="AtAnimation"/>
    /// first. No lights gives this lighting without any.
    /// </summary>
    public AreaLocalLighting WithCarried(StationaryLightFrame frame, IEnumerable<CarriedLight> lights)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(lights);
        var list = lights.ToArray();
        if (list.Length == 0) return _carried is null ? this : new(this, (StationaryLightField?)null);
        return new(this, new StationaryLightField(AreaStationaryLightSource.ForCarried(Field.Source.CatalogueMap, frame, list), Chart.OriginX, Chart.OriginY));
    }

    /// <summary>The critters' carried lights of this view (0 for none).</summary>
    public int CarriedLightCount => _carried?.Source.Lights.Count ?? 0;

    /// <summary>ARC-DEV-335: the carried lights alone as a terrain lattice over the carried lights' rectangles (null without any); the
    /// terrain draws it next to the sector lights' lattice.</summary>
    public StationaryLightLattice? CarriedLattice(AmbientPair pair) => _carried?.Lattice(pair);

    /// <summary>ARC-RENDER-031: this lighting with its animated lights where they stand after <paramref name="animationMs"/>
    /// (<see cref="StationaryLightField.PosesAt"/>); this one when nothing animates. Take <see cref="AtHour"/> first: a posed
    /// view keeps no day form.</summary>
    public AreaLocalLighting AtAnimation(long animationMs) => Field.Animated.Count == 0 ? this : new(this, Field.PosesAt(animationMs));

    /// <summary>The animated lights' poses as a comparable key (empty when nothing animates).</summary>
    public string AnimationKey(long animationMs) => Field.Animated.Count == 0 ? ""
        : string.Join(";", Field.PosesAt(animationMs).OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.Frame}:{p.Value.OffsetX}:{p.Value.OffsetY}"));

    public StationaryLightField Field { get; }
    public AreaChart Chart { get; }
    public int LightCount => Field.Source.Lights.Count;
    /// <summary>ARC-RENDER-030: the object lights among <see cref="LightCount"/>.</summary>
    public int ObjectLightCount => Field.Source.Lights.Count(light => light.Owner is not null);

    /// <summary>The lighting of a game hour: the day form (hours 6..17) when object lights differ by day, else this.</summary>
    public AreaLocalLighting AtHour(int hour)
    {
        if (hour is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(hour));
        return _day is { } day && hour is >= 6 and <= 17 ? day : this;
    }

    public static AreaLocalLighting Create(AreaSnapshot snapshot, AreaChart chart, AreaStationaryLightSource lights)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Create(snapshot.Terrain, snapshot.Visuals, chart, lights);
    }

    /// <summary>ARC-RENDER-030: the stationary lights with the snapshot's object lights, as the original builds them by night
    /// and by day (<see cref="AreaStationaryLightSource.WithObjectLights"/>).</summary>
    public static AreaLocalLighting Create(AreaSnapshot snapshot, AreaChart chart, AreaStationaryLightSource lights, AreaObjectLightSource objects)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Create(snapshot.Terrain, snapshot.Visuals, chart, lights, objects);
    }

    // Value-input seam shared with synthetic tests.
    public static AreaLocalLighting Create(IEnumerable<AreaTerrainRecord> terrain, IEnumerable<AreaVisualRecord> visuals, AreaChart chart,
        AreaStationaryLightSource lights, AreaObjectLightSource objects)
    {
        ArgumentNullException.ThrowIfNull(terrain); ArgumentNullException.ThrowIfNull(visuals);
        ArgumentNullException.ThrowIfNull(lights); ArgumentNullException.ThrowIfNull(objects);
        var tiles = terrain as IReadOnlyCollection<AreaTerrainRecord> ?? terrain.ToArray();
        var bodies = visuals as IReadOnlyCollection<AreaVisualRecord> ?? visuals.ToArray();
        var night = Create(tiles, bodies, chart, lights.WithObjectLights(objects, 0));
        // Only nocturnal scenery on an outdoor tile differs: it is not built by day.
        if (objects.Lights.Any(light => !ObjectLightReader.BuiltAtLoad(light, 12)))
            night._day = Create(tiles, bodies, chart, lights.WithObjectLights(objects, 12));
        return night;
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

    /// <summary>
    /// ARC-DEV-335: a body's colour with this view's carried lights added to <paramref name="basis"/>, the colour the sector's lights gave
    /// it (the cached view's body colour): the same anchor and wall rule as <see cref="Body(AreaDrawCommand, AreaAmbientChannel, AmbientPair, bool)"/>.
    /// Without carried lights, or for an unlit art channel, the basis itself.
    /// </summary>
    public AreaLitColors AddCarried(AreaDrawCommand command, AreaAmbientChannel artChannel, AmbientPair pair, AreaLitColors basis)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_carried is null || artChannel == AreaAmbientChannel.Unlit) return basis;
        if (command.Identity is null) throw new ArgumentException("A body command is required.", nameof(command));
        // A body this lighting does not know (drawn from another map's plan for a frame) keeps the colour it has.
        return _visuals.TryGetValue(command.Visual, out var visual) ? AddCarried(command, visual, pair, basis) : basis;
    }

    /// <summary>The same for a body whose visual record comes from elsewhere (a companion, a body drawn from extra art).</summary>
    public AreaLitColors AddCarried(AreaDrawCommand command, AreaVisualRecord visual, AmbientPair pair, AreaLitColors basis)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(visual);
        if (_carried is null) return basis;
        if (visual.RawArt >> 28 == 1) return Wall(command, visual, pair, true, basis);
        var tile = TileScreen(command.MapX, command.MapY);
        var color = Sample(checked(tile.X + visual.ObjectOffsetX + 40L), checked(tile.Y + visual.ObjectOffsetY + 20L), basis.Left, pair, null, true);
        return new(color, color);
    }

    /// <summary>The source's non-wall object colour: tile-class ambient plus the lights at the anchor.</summary>
    public AmbientColor Point(long worldX, long worldY, int offsetX, int offsetY, AmbientPair pair)
    {
        var tile = TileScreen(worldX, worldY);
        return Sample(checked(tile.X + offsetX + 40L), checked(tile.Y + offsetY + 20L), Ambient(TileChannel(worldX, worldY), pair), pair);
    }

    // The sector's lights from the basis colour, then the carried lights (ARC-DEV-335); carriedOnly starts from a colour that already
    // holds the sector's lights.
    private AmbientColor Sample(long x, long y, AmbientColor basis, AmbientPair pair, Func<StationaryLight, bool>? admit = null, bool carriedOnly = false)
    {
        var color = carriedOnly ? basis : Field.Point(x, y, basis, pair, admit, _poses);
        return _carried is null ? color : _carried.Point(x, y, color, pair, admit);
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
    private AreaLitColors Wall(AreaDrawCommand command, AreaVisualRecord visual, AmbientPair pair, bool lit, AreaLitColors? basis = null)
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
        // A carried-only pass (basis) starts each end from the colour the sector's lights gave it.
        AmbientColor At(int column, AmbientColor? from) => lit ? Sample(x + column, y + column / 2 * step, from ?? baseColor, pair, Front, from is not null) : baseColor;
        return new(At(0, basis?.Left), At(width - 1, basis?.Right));
    }

    /// <summary>The whole view for one hour: lattice and every body command's colour.</summary>
    public AreaLocalLightView View(AreaScenePlan plan, IReadOnlyDictionary<AreaVisualKey, AreaAmbientChannel> channels, AmbientPair pair)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(channels);
        var bodies = new Dictionary<AreaVisualKey, AreaLitColors>();
        foreach (var command in plan.Commands.Where(command => command.Identity is not null))
            bodies[command.Visual] = Body(command, channels.TryGetValue(command.Visual, out var channel) ? channel
                : throw new InvalidOperationException("AREA_AMBIENT_VISUAL_REQUIRED"), pair);
        return new(_hourIndependent ? _lattice : Field.Lattice(pair, _poses), new ReadOnlyDictionary<AreaVisualKey, AreaLitColors>(bodies), placement => Actor(placement, pair));
    }
}
