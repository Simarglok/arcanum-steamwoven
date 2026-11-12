using Godot;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-RENDER-010: the applied hourly pair and the channel of every drawn visual.</summary>
public sealed record AreaAmbientView(AmbientPair Pair, IReadOnlyDictionary<AreaVisualKey, AreaAmbientChannel> Channels)
{
    /// <summary>ARC-RENDER-012 stationary lights of the shown map at this hour; null draws ambient only.</summary>
    public AreaLocalLightView? Local { get; init; }
    /// <summary>ARC-WORLD-017: the light of a moved companion body where it stands now (null for any other command);
    /// absent lights moved bodies by their T0 visual like the plan's.</summary>
    /// <summary>ARC-DEV-335: the second argument is the lighting with the critters' carried lights where they stand now (null: none), which the
    /// delegate lights the body by in place of the map's own.</summary>
    public Func<AreaDrawCommand, AreaLocalLighting?, AreaLitColors?>? Moving { get; init; }
}

/// <summary>ARC-RENDER-036: the covered-tile masks and roofs one submission drew (ARC-RENDER-022), for pixel observations.</summary>
public sealed record AreaVisibleCovers(IReadOnlyList<AreaDrawCommand> Masks, IReadOnlyList<AreaDrawCommand> Roofs)
{
    public static AreaVisibleCovers None { get; } = new(Array.Empty<AreaDrawCommand>(), Array.Empty<AreaDrawCommand>());
}

public partial class TerrainCanvas
{
    private AreaAmbientView? _areaAmbient;
    private IReadOnlyDictionary<AreaObjectKey, AreaWallFade>? _areaWallFades;
    private IReadOnlySet<AreaObjectKey>? _areaHidden;
    // ARC-WORLD-016: bodies drawn where their live actor stands (companions), replacing their T0 command.
    private IReadOnlyList<AreaDrawCommand>? _areaMoving;
    private IReadOnlySet<AreaObjectKey?> _areaMovingKeys = new HashSet<AreaObjectKey?>();
    private int _areaMovingStamp;
    // ARC-WORLD-017: companions of this map standing on another one; their T0 body is not drawn.
    private IReadOnlySet<AreaObjectKey>? _areaAbsent;
    // ARC-RENDER-022: the roofs drawn after every body (unfilled blocks, with a faded block's corner alphas), the terrain
    // commands an unfaded roof covers (drawn again in black over the terrain: the original leaves them undrawn) and the
    // bodies a roof covers (not drawn).
    private IReadOnlyList<(AreaDrawCommand Command, Vector4? Corners)>? _areaRoofs;
    private IReadOnlyList<AreaDrawCommand>? _areaCoveredTiles;
    private IReadOnlySet<AreaObjectKey>? _areaCovered;
    // ARC-UI-057: the hovered object drawn a second time over itself (0x43C690), or null.
    private AreaObjectKey? _areaHighlight;
    private AreaScenePlan? _area;
    private IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame>? _areaFrames;
    private ActorDiagnosticPlacementInput? _areaActorPlacement;
    private readonly List<AreaDrawItem> _areaItems = new();
    // Upper bound of items that may be shown: [_areaActive, Count) are hidden and
    // released. The pool is bounded by AreaPoolFloor / twice the visible count.
    private int _areaActive;
    private const int AreaPoolFloor = 256;
    private const int AreaPoolTrimPerUpdate = 1024;
    private AreaUprightOrder? _areaUpright;
    // Non-upright commands (phase < 4) in plan order and a coarse grid over them, so a
    // moving view does not scan every command; the exact intersection test is unchanged.
    private AreaDrawCommand[] _areaFlat = Array.Empty<AreaDrawCommand>();
    private AreaVisibilityIndex? _areaIndex;
    private readonly AreaMappingCache _areaMappings = new();
    // Terrain drawn as pre-composed chunks when the plan admits it (exact under the
    // nearest rule; see docs/rendering/area-viewer.md); null keeps
    // per-command terrain items.
    private AreaTerrainChunks? _areaChunks;
    private readonly AreaChunkTextureCache _areaChunkTextures = new();
    private readonly Dictionary<AreaDrawCommand, (int X, int Y)> _areaChunkCommands = new(ReferenceEqualityComparer.Instance);
    internal bool AreaTerrainChunksEnabled { get; set; } = true;
    /// <summary>Terrain chunk items in the current submission, 0 when terrain is drawn per command.</summary>
    public int AreaChunkCount { get; private set; }
    public int AreaChunkLevel { get; private set; }
    internal long AreaChunkCreatedCount => _areaChunkTextures.CreatedCount;
    internal long AreaChunkDisposedCount => _areaChunkTextures.DisposedCount;
    /// <summary>Resident terrain chunk textures and their RGBA bytes.</summary>
    public int AreaChunkResidentCount => _areaChunkTextures.Count;
    public long AreaChunkResidentBytes => _areaChunkTextures.Bytes;
    /// <summary>Whether terrain can be drawn as chunks at all (plan admitted, not disabled).</summary>
    public bool AreaTerrainChunked => AreaTerrainChunksEnabled && _areaChunks is not null;

    private static readonly AreaObjectKey ActorSentinel = new("controlled-session", 5051, AreaSourceKind.Mobile, null, int.MaxValue, 0, "controlled-session", 0);
    public Action<Exception>? AreaFailure { get; set; }
    public int AreaVisibleCount { get; private set; }
    internal int AreaPoolCount => _areaItems.Count;
    internal long AreaMappingCreatedCount => _areaMappings.CreatedCount;
    /// <summary>Index rows resident in the shared cache (ARC-RENDER-032 smoke).</summary>
    internal int AreaMappingRows => _areaMappings.Count;
    public int AreaCulledCount => (_area?.Commands.Count ?? 0) - AreaVisibleCount;
    public IReadOnlyList<AreaDrawCommand> AreaVisibleCommands { get; private set; } = Array.Empty<AreaDrawCommand>();
    private (Rect2 View, Transform2D Transform, ActorDiagnosticDrawCommand Actor,
        ActorDiagnosticPlacementInput Placement, bool ActorVisible, bool BodiesVisible, bool Chunks, int Moving, AreaObjectKey? Highlight)? _areaView;

    /// <summary>ARC-UI-057 (ledger OH2, OH3): the hovered portal, container, scenery or item, drawn once more after the bodies and before the
    /// roofs, additive with the constant colour (200, 200, 200) and unlit, without its alpha modes (0x43C690 masks them off); null none.</summary>
    internal AreaObjectKey? AreaHighlight
    {
        get => _areaHighlight;
        set
        {
            if (_areaHighlight == value) return;
            _areaHighlight = value;
            try { UpdateAreaVisible(); }
            catch (Exception error) { AreaFailure?.Invoke(error); }
        }
    }
    /// <summary>The colour the highlight's source pixels are multiplied by before they are added.</summary>
    internal static readonly Color AreaHighlightColour = Color.Color8(AreaHoverWindow.HighlightLevel, AreaHoverWindow.HighlightLevel, AreaHoverWindow.HighlightLevel);
    /// <summary>The highlight command drawn by the current submission (smoke seam), or null.</summary>
    internal AreaDrawCommand? AreaHighlightDrawnForSelfTest { get; private set; }

    public void PublishArea(AreaLoadResult area, ActorDiagnosticScenePlan actor, ActorDiagnosticPlacementInput placement, TerrainTextureCache textures)
        => PublishArea(area.Plan, area.Frames, actor, placement, textures);

    // Same production compositor, accepting the immutable numeric plan and
    // retained frame repository separately for independently authored fixtures.
    internal void PublishArea(AreaScenePlan plan, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> frames,
        ActorDiagnosticScenePlan actor, ActorDiagnosticPlacementInput placement, TerrainTextureCache textures)
    {
        // ARC-DEV-380: the canvas's own share of a publication (steps print as canvas.publish.NAME, the first view as canvas.view.NAME).
        // Only a publication of a new plan is timed; the others (a moved hero, a changed body) are per-frame.
        using var lap = PerfProbe.Enabled && (!ReferenceEquals(_area, plan) || !ReferenceEquals(_areaFrames, frames)) ? PerfProbe.Lap("canvas.publish") : null;
        lap?.Step("chunks");
        if (!ReferenceEquals(_textures, textures)) _areaView = null;
        if (!ReferenceEquals(_area, plan) || !ReferenceEquals(_areaFrames, frames))
        {
            _areaPerfView = true;
            var chunks = AreaTerrainChunks.TryCreate(plan, frames);
            _areaView = null; _areaChunkTextures.Clear(); _areaChunks = chunks; _areaChunkChannels.Clear(); _hdChunkQueries.Clear();
            if (_area is not null) ReleaseAreaHd(); // a map change releases the previous map's HD textures
        }
        lap?.Step("upright");
        if (!ReferenceEquals(_area, plan) || _areaUpright is null) _areaUpright = new AreaUprightOrder(plan.Commands, plan.Chart);
        lap?.Step("index");
        if (!ReferenceEquals(_area, plan) || _areaIndex is null)
        {
            _areaFlat = plan.Commands.Where(command => command.Phase < 4).ToArray();
            _areaIndex = new AreaVisibilityIndex(_areaFlat.Select(command => command.Destination).ToArray());
        }
        _plan = null; _staticPlan = null;
        _area = plan; _areaFrames = frames; _actorPlan = actor; _areaActorPlacement = placement; _textures = textures;
        lap?.Step("view");
        UpdateAreaVisible(); QueueRedraw();
    }
    /// <summary>Applies a new ambient pair to every item from the next submission (null draws source
    /// pixels unlit). Only the per-item multiplier changes; plan, frames and index rows are reused.</summary>
    public void SetAreaAmbient(AreaAmbientView? ambient)
    {
        if (Equals(_areaAmbient, ambient)) return;
        if (!ReferenceEquals(_areaAmbient?.Channels, ambient?.Channels)) _areaChunkChannels.Clear();
        _areaAmbient = ambient; _areaView = null; QueueRedraw();
    }
    /// <summary>ARC-RENDER-011: the wall check's fade of each wall from the next submission (null or absent = opaque).
    /// Only the per-item alpha changes; plan, order and index rows are reused.</summary>
    public void SetAreaWallFades(IReadOnlyDictionary<AreaObjectKey, AreaWallFade>? fades)
    {
        if (ReferenceEquals(_areaWallFades, fades)) return;
        _areaWallFades = fades; _areaView = null; QueueRedraw();
    }
    /// <summary>ARC-UI-004: bodies no longer in the world (items the PC took) from the next submission; null = none.</summary>
    public void SetAreaHidden(IReadOnlySet<AreaObjectKey>? hidden)
    {
        if (ReferenceEquals(_areaHidden, hidden)) return;
        _areaHidden = hidden; _areaView = null; QueueRedraw();
    }
    internal IReadOnlySet<AreaObjectKey>? AreaHidden => _areaHidden;

    /// <summary>ARC-WORLD-096: the union of the drawn rectangles of an object's T0 body in the published plan (canvas space), or null
    /// when the plan draws none for it (the restock's screen test, 0x43DFD0).</summary>
    internal Rect2? AreaBodyRect(AreaObjectKey key)
    {
        if (_area is null) return null;
        Rect2? union = null;
        foreach (var command in _area.Commands)
            if (command.Identity == key)
            {
                var rect = new Rect2(command.Destination.X, command.Destination.Y, command.Destination.Width, command.Destination.Height);
                union = union is { } sum ? sum.Merge(rect) : rect;
            }
        return union;
    }
    /// <summary>ARC-RENDER-022: the roof state from the next submission (null = no roofs).</summary>
    public void SetAreaRoofs(IReadOnlyList<(AreaDrawCommand Command, Vector4? Corners)>? roofs, IReadOnlyList<AreaDrawCommand>? coveredTiles,
        IReadOnlySet<AreaObjectKey>? coveredBodies)
    {
        if (ReferenceEquals(_areaRoofs, roofs) && ReferenceEquals(_areaCoveredTiles, coveredTiles) && ReferenceEquals(_areaCovered, coveredBodies)) return;
        _areaRoofs = roofs; _areaCoveredTiles = coveredTiles; _areaCovered = coveredBodies; _areaView = null; QueueRedraw();
    }
    /// <summary>ARC-RENDER-022: the frames of the roofs and covered-tile masks the last submission drew per item.</summary>
    public IReadOnlySet<AreaFrameKey> AreaVisibleRoofFrames { get; private set; } = new HashSet<AreaFrameKey>();
    /// <summary>ARC-RENDER-036: what the last submission drew over <see cref="AreaVisibleCommands"/> outside that list: the
    /// covered-tile masks (over the terrain only) and the roofs (over everything), in draw order.</summary>
    public AreaVisibleCovers AreaVisibleCovers { get; private set; } = AreaVisibleCovers.None;
    public int AreaVisibleRoofCount { get; private set; }
    /// <summary>ARC-WORLD-016: moved body commands from the next submission (null or empty = every body at T0).</summary>
    public void SetAreaMovingBodies(IReadOnlyList<AreaDrawCommand>? moving)
    {
        if (moving is null or { Count: 0 } && _areaMoving is null or { Count: 0 }) return;
        if (_areaMoving is { } old && moving is { } next && old.SequenceEqual(next)) return;
        _areaMoving = moving; _areaMovingKeys = (moving ?? []).Select(command => command.Identity).ToHashSet();
        _areaMovingStamp++; _areaView = null; QueueRedraw();
    }
    // ARC-MAGIC-045: the body styles the object flags a spell wrote ask for (don't-draw, invisible, translucent, shrunk), by body; the actor's own is applied to its marker.
    private IReadOnlyDictionary<AreaObjectKey, AreaBodyStyle>? _areaBodyStyles;
    private AreaBodyStyle? _actorBodyStyle;

    /// <summary>ARC-MAGIC-045: the bodies drawn other than the data says (<see cref="AreaBodyStyleRules"/>), and the actor's; an equal set changes nothing.</summary>
    public void SetAreaBodyStyles(IReadOnlyDictionary<AreaObjectKey, AreaBodyStyle>? styles, AreaBodyStyle? actor)
    {
        if (styles is { Count: 0 }) styles = null;
        if (actor == AreaBodyStyle.Plain) actor = null;
        var same = actor == _actorBodyStyle && (styles is null ? _areaBodyStyles is null : _areaBodyStyles is { } old && old.Count == styles.Count && styles.All(p => old.TryGetValue(p.Key, out var o) && o == p.Value));
        if (same) return;
        _areaBodyStyles = styles; _actorBodyStyle = actor;
        _areaMovingStamp++; _areaView = null; QueueRedraw();
    }

    internal IReadOnlyDictionary<AreaObjectKey, AreaBodyStyle>? AreaBodyStyles => _areaBodyStyles;

    // The style of a command's body: the actor marker's, or the body's by identity (null: as drawn).
    private AreaBodyStyle? BodyStyleOf(AreaDrawCommand command) =>
        command.Identity is not { } key ? null : key == ActorSentinel ? _actorBodyStyle : _areaBodyStyles is { } styles && styles.TryGetValue(key, out var style) ? style : null;

    /// <summary>ARC-WORLD-017: this map's companions that stand on another map, from the next submission (null = none).</summary>
    public void SetAreaAbsent(IReadOnlySet<AreaObjectKey>? absent)
    {
        if (absent is null or { Count: 0 } && _areaAbsent is null or { Count: 0 }) return;
        if (_areaAbsent is { } old && absent is { } next && old.SetEquals(next)) return;
        _areaAbsent = absent; _areaView = null; QueueRedraw();
    }
    /// <summary>ARC-RENDER-018 smoke seam: one body left out of the next submissions (null = none), so the native blend
    /// observation can read the pixels beneath it.</summary>
    internal AreaObjectKey? AreaSmokeOmitted
    {
        get => _areaSmokeOmitted;
        set { if (_areaSmokeOmitted == value) return; _areaSmokeOmitted = value; _areaView = null; QueueRedraw(); }
    }
    private AreaObjectKey? _areaSmokeOmitted;
    // ARC-RENDER-022: a body a roof covers is not drawn (the pick does not test the cover).
    internal bool AreaHiddenOf(AreaDrawCommand command) => command.Identity is { } key && (_areaHidden?.Contains(key) == true || _areaAbsent?.Contains(key) == true
        || key == _areaSmokeOmitted || _areaCovered?.Contains(key) == true);
    /// <summary>ARC-WORLD-037 smoke seam: whether a roof covers the body (it is then not drawn, as in the original).</summary>
    internal bool AreaRoofCovers(AreaObjectKey key) => _areaCovered?.Contains(key) == true;
    internal AreaWallFade AreaWallFadeOf(AreaDrawCommand command)
        => command.Identity is { } key && _areaWallFades is { } fades && fades.TryGetValue(key, out var fade) ? fade : AreaWallFade.None;
    // One ambient multiplier per chunk item: a chunk is drawn as a chunk under lighting only
    // when all its terrain shares one channel (null = mixed, drawn per command instead).
    private readonly Dictionary<(int X, int Y), AreaAmbientChannel?> _areaChunkChannels = new();
    private AreaAmbientChannel? AreaChunkChannel((int X, int Y) chunk)
    {
        if (_areaAmbient is not { } ambient) return AreaAmbientChannel.Unlit;
        if (_areaChunkChannels.TryGetValue(chunk, out var known)) return known;
        var channels = _areaChunks!.CommandsIn(chunk).Select(command => ambient.Channels.TryGetValue(command.Visual, out var found)
            ? found : throw new InvalidOperationException("AREA_AMBIENT_VISUAL_REQUIRED")).Distinct().ToArray();
        var channel = channels.Length == 1 ? channels[0] : (AreaAmbientChannel?)null;
        _areaChunkChannels[chunk] = channel; return channel;
    }
    // ARC-RENDER-012: terrain under a light lattice samples it per pixel; bodies take the source's object
    // colour (walls a two-end gradient), the actor its colour at the current placement.
    private (Color Left, Color Right, AreaLatticeBinding? Lattice, AreaCarriedBinding? Carried) AreaItemLight(AreaDrawCommand command, bool isActor)
    {
        static Color Lit(AmbientColor color) => new(color.R / 255f, color.G / 255f, color.B / 255f);
        // ARC-WORLD-017: a moved companion body is lit where it stands, by its own visual (it may come from another map).
        if (!isActor && _areaAmbient?.Moving is { } moving && _areaMovingKeys.Contains(command.Identity) && moving(command, _areaCarriedNow) is { } lit)
            return (Lit(lit.Left), Lit(lit.Right), null, null);
        var ambient = AreaAmbientColor(command, isActor);
        if (_areaAmbient?.Local is not { } local) return (ambient, ambient, null, null);
        // ARC-DEV-335 (V7d): the carried lights of the critters stand where the critters stand now, so they are added at draw time to
        // the cached view's colour, not built into it.
        var pair = _areaAmbient.Pair;
        if (isActor)
        {
            var actor = Lit(_areaCarriedNow is { } carriedActor ? carriedActor.Actor(_areaActorPlacement!, pair) : local.Actor(_areaActorPlacement!));
            return (actor, actor, null, null);
        }
        if (command.Identity is not null)
        {
            if (!local.Bodies.TryGetValue(command.Visual, out var body)) throw new InvalidOperationException("AREA_LIGHT_BODY_REQUIRED");
            if (_areaCarriedNow is { } carried && _areaAmbient.Channels.TryGetValue(command.Visual, out var channel))
                body = carried.AddCarried(command, command.Unlit ? AreaAmbientChannel.Unlit : channel, pair, body);
            return (Lit(body.Left), Lit(body.Right), null, null);
        }
        // Terrain (tiles, facades, their chunks) is always on the indoor or outdoor channel.
        return (ambient, ambient, local.Lattice is { } lattice ? AreaLattice(lattice) : null, _areaCarriedBinding);
    }
    // ARC-DEV-335: the critters' carried lights as the view of the current submission (null: none), made from the hero's placement by the host.
    private Func<ActorDiagnosticPlacementInput, TerrainRect, AreaLocalLighting?>? _areaCarried;
    private AreaLocalLighting? _areaCarriedNow;
    // The carried lights' lattice of the current submission, made once for all its terrain items.
    private AreaCarriedBinding? _areaCarriedBinding;
    /// <summary>ARC-DEV-335 smoke seams: the lighting with the hero's carried light of the last submission (null: none) and the hero's placement.</summary>
    internal AreaLocalLighting? AreaCarriedForSelfTest => _areaCarriedNow;
    internal ActorDiagnosticPlacementInput? AreaActorPlacementForSelfTest => _areaActorPlacement;
    /// <summary>ARC-WORLD-1149: where the hero is drawn now, for the light a check reads at a cell (null: no area is shown).</summary>
    internal ActorDiagnosticPlacementInput? AreaActorPlacementNow => _areaActorPlacement;
    private (StationaryLightLattice Lattice, AreaCarriedBinding Binding)? _areaCarriedLattice;
    /// <summary>ARC-DEV-335 (V7d, ledger LT12): the lighting with the critters' carried lights, made from the hero's placement at each
    /// submission (the hero's light moves with it); null draws no carried light. The terrain, the bodies and the actor take it from the next
    /// submission.</summary>
    public void SetAreaCarried(Func<ActorDiagnosticPlacementInput, TerrainRect, AreaLocalLighting?>? carried)
    {
        if (ReferenceEquals(_areaCarried, carried)) return;
        _areaCarried = carried; _areaCarriedNow = null; _areaCarriedBinding = null; _areaView = null; QueueRedraw();
    }
    private AreaCarriedBinding AreaCarried(StationaryLightLattice lattice)
    {
        if (_areaCarriedLattice is { } known && ReferenceEquals(known.Lattice, lattice)) return known.Binding;
        DisposeAreaCarried();
        ImageTexture Texture(IReadOnlyList<byte> rows)
        {
            using var image = Image.CreateFromData(lattice.Columns, lattice.Rows, false, Image.Format.Rgba8, rows.ToArray());
            return ImageTexture.CreateFromImage(image);
        }
        var binding = new AreaCarriedBinding(Texture(lattice.Lighter), Texture(lattice.Darker), new(lattice.FirstColumn, lattice.FirstRow, lattice.Columns, lattice.Rows));
        _areaCarriedLattice = (lattice, binding); return binding;
    }
    private void DisposeAreaCarried()
    {
        if (_areaCarriedLattice is { } old) { old.Binding.Lighter.Dispose(); old.Binding.Darker.Dispose(); }
        _areaCarriedLattice = null;
    }
    private (StationaryLightLattice Lattice, AreaLatticeBinding Binding)? _areaLattice;
    private AreaLatticeBinding AreaLattice(StationaryLightLattice lattice)
    {
        if (_areaLattice is { } known && ReferenceEquals(known.Lattice, lattice)) return known.Binding;
        DisposeAreaLattice();
        ImageTexture Texture(IReadOnlyList<byte> rows)
        {
            using var image = Image.CreateFromData(lattice.Columns, lattice.Rows, false, Image.Format.Rgba8, rows.ToArray());
            return ImageTexture.CreateFromImage(image);
        }
        var binding = new AreaLatticeBinding(Texture(lattice.Lighter), Texture(lattice.Darker), new(lattice.FirstColumn, lattice.FirstRow, lattice.Columns, lattice.Rows));
        _areaLattice = (lattice, binding); return binding;
    }
    // Materials still holding the textures keep their own references; this drops the canvas's.
    private void DisposeAreaLattice()
    {
        if (_areaLattice is { } old) { old.Binding.Lighter.Dispose(); old.Binding.Darker.Dispose(); }
        _areaLattice = null;
    }
    private Color AreaRoofLight()
    {
        if (_areaAmbient is not { } ambient) return Colors.White;
        var color = ambient.Pair.Outdoor;
        return new Color(color.R / 255f, color.G / 255f, color.B / 255f);
    }
    // The actor is a critter: outdoor channel (the source's palette callback), like other bodies.
    private Color AreaAmbientColor(AreaDrawCommand command, bool isActor)
    {
        if (_areaAmbient is not { } ambient) return Colors.White;
        var channel = isActor ? AreaAmbientChannel.Outdoor
            : _areaChunkCommands.TryGetValue(command, out var chunk) ? AreaChunkChannel(chunk) ?? throw new InvalidOperationException("AREA_AMBIENT_MIXED_CHUNK")
            : ambient.Channels.TryGetValue(command.Visual, out var found) ? found : throw new InvalidOperationException("AREA_AMBIENT_VISUAL_REQUIRED");
        var color = channel switch
        {
            AreaAmbientChannel.Outdoor => ambient.Pair.Outdoor,
            AreaAmbientChannel.Indoor => ambient.Pair.Indoor,
            _ => AmbientColor.Neutral,
        };
        return new Color(color.R / 255f, color.G / 255f, color.B / 255f);
    }
    public override void _Process(double delta)
    {
        if (_area is null) { ProcessWorldHd(); return; }
        try { UpdateAreaVisible(); }
        catch (Exception error) { AreaFailure?.Invoke(error); }
    }
    // ARC-DEV-380: set by a publication of a new plan; the first full view after it is timed (canvas.view.NAME).
    private bool _areaPerfView;
    private void UpdateAreaVisible()
    {
        if (!IsInsideTree() || _area is null || _areaFrames is null || _textures is null || _actorPlan is null || _areaActorPlacement is null) return;
        var view = GetViewport().GetVisibleRect(); var inverse = GetViewport().GetCanvasTransform().AffineInverse();
        var state = (view, GetViewport().GetCanvasTransform(), _actorPlan.Current, _areaActorPlacement, _showActor, _showStatics, AreaTerrainChunksEnabled, _areaMovingStamp, _areaHighlight);
        // Pending HD frames that became ready rebind the same view (ARC-RENDER-014).
        if (_areaView == state && !HdRefreshDue()) { TrimAreaPool(); return; }
        using var lap = _areaPerfView && PerfProbe.Enabled ? PerfProbe.Lap("canvas.view") : null;
        _areaPerfView = false;
        lap?.Step("cull");
        var a = inverse * view.Position; var b = inverse * view.End;
        var world = new Rect2(a, b - a).Abs();
        // ARC-DEV-335, ARC-DEV-353: the carried lights (the hero's, the critters') stand where their creatures stand in this submission; only the ones
        // that can reach the view (its rectangle widened by the largest light frame) are built, so a map's far lights stay out of the lattice.
        const int LightReach = 1024;
        _areaCarriedNow = _areaCarried?.Invoke(_areaActorPlacement, new TerrainRect(
            (int)Math.Floor(world.Position.X) - LightReach, (int)Math.Floor(world.Position.Y) - LightReach,
            (int)Math.Ceiling(world.Size.X) + 2 * LightReach, (int)Math.Ceiling(world.Size.Y) + 2 * LightReach));
        _areaCarriedBinding = _areaAmbient is { } shown && _areaCarriedNow?.CarriedLattice(shown.Pair) is { } carriedLattice ? AreaCarried(carriedLattice) : null;
        bool Intersects(AreaDrawCommand command) => new Rect2(command.Destination.X, command.Destination.Y,
            command.Destination.Width, command.Destination.Height).Intersects(world);
        var upright = _areaUpright!.With(_areaActorPlacement, _actorPlan.Current, ActorSentinel, _areaMoving);
        var flat = _areaIndex!.Candidates(world.Position.X, world.Position.Y, world.End.X, world.End.Y);
        // ARC-RENDER-033: flat moved bodies (an item lying where no T0 body stood) join the flat commands in plan order.
        var flatCommands = flat.Select(index => _areaFlat[index]);
        var ordered = (_areaMoving is { Count: > 0 } ? AreaScenePlan.WithMovingFlat(flatCommands, _areaMoving, _areaMovingKeys) : flatCommands).Concat(upright);
        var visible = ordered.Where(command => Intersects(command) && (command.Identity == ActorSentinel ? _showActor : command.Identity is null || (_showStatics && !AreaHiddenOf(command)))).ToArray();
        // ARC-MAGIC-045: a body a spell hid (don't-draw, invisible to the player) is not drawn nor picked; a translucent or shrunk one is drawn so (the original's draw list builder and blit set-up).
        if (_areaBodyStyles is not null || _actorBodyStyle is not null)
            visible = visible.Where(command => BodyStyleOf(command) is not { Drawn: false }).Select(command => BodyStyleOf(command) is { } style ? AreaBodyStyleRules.Apply(command, style) : command).ToArray();
        if (visible.Length > _area.Commands.Count + 1 + (_areaMoving?.Count ?? 0)) throw new InvalidOperationException("AREA_SUBMISSION_BUDGET");
        // Index rows used by this submission stay resident; rows from earlier views are
        // reused across pans or evicted under the shared 16 MiB cap. The original ART
        // repository is not re-decoded.
        _areaMappings.BeginSubmission();
        // Terrain chunks replace the visible terrain commands in the draw list only;
        // AreaVisibleCommands below keeps the per-command visibility.
        var draw = visible; _areaChunkCommands.Clear(); AreaChunkCount = 0; AreaChunkLevel = 0; var chunked = false;
        lap?.Step("chunks");
        if (AreaTerrainChunksEnabled && _areaChunks is not null)
        {
            var level = ChunkLevel(AreaDrawItem.RasterTransform(GetViewport()));
            var chunks = _areaChunks.Intersecting(world.Position.X, world.Position.Y, world.Size.X, world.Size.Y).ToArray();
            if (AreaChunkTextureCache.Fits(chunks.Length, level) && chunks.All(chunk => AreaChunkChannel(chunk) is not null))
            {
                _areaChunkTextures.BeginSubmission();
                // Missing chunks of this view are composed in parallel before the items acquire them.
                _areaChunkTextures.Prepare(_areaChunks, chunks, level);
                var chunkDraws = chunks.Select(chunk => AreaTerrainChunks.Command(chunk, level)).ToArray();
                for (var index = 0; index < chunks.Length; index++) _areaChunkCommands.Add(chunkDraws[index], chunks[index]);
                draw = chunkDraws.Concat(visible.Where(command => command.Identity is not null)).ToArray();
                AreaChunkCount = chunks.Length; AreaChunkLevel = level; chunked = true;
            }
        }
        // Per-command terrain holds no chunk textures alongside its frame textures.
        if (!chunked) _areaChunkTextures.Clear();
        // ARC-RENDER-022: covered tiles are blacked out right after the terrain, roofs drawn after everything else.
        var masks = (_areaCoveredTiles ?? []).Where(Intersects).ToArray();
        var roofs = (_areaRoofs ?? []).Where(roof => Intersects(roof.Command)).ToArray();
        var terrainCount = draw.TakeWhile(command => command.Identity is null).Count();
        // ARC-UI-057: the hovered object's second draw follows every body (its blit is queued last, 0x43C9DE) and comes before the roofs.
        var highlight = _areaHighlight is { } lit && visible.LastOrDefault(command => command.Identity == lit) is { } body
            ? body with { Additive = true, Alpha = 255, Multiply = false, WadeRows = 0, Unlit = true } : null;
        AreaHighlightDrawnForSelfTest = highlight;
        draw = draw.Take(terrainCount).Concat(masks).Concat(draw.Skip(terrainCount)).Concat(highlight is null ? Array.Empty<AreaDrawCommand>() : new[] { highlight })
            .Concat(roofs.Select(roof => roof.Command)).ToArray();
        var maskStart = terrainCount; var maskEnd = terrainCount + masks.Length; var roofStart = draw.Length - roofs.Length;
        var highlightIndex = highlight is null ? -1 : roofStart - 1;
        AreaVisibleRoofFrames = masks.Concat(roofs.Select(roof => roof.Command)).Select(command => command.Frame).ToHashSet();
        // A roof faded out at all four corners leaves the pixels beneath it as they are.
        AreaVisibleCovers = new(Array.AsReadOnly(masks), Array.AsReadOnly(roofs.Where(roof => roof.Corners != Vector4.Zero).Select(roof => roof.Command).ToArray()));
        AreaVisibleRoofCount = roofs.Length;
        lap?.Step("upload");
        _textures.UploadAreaVisible(_areaFrames, draw.Where((command, index) => command.Identity != ActorSentinel
            && (!_areaChunkCommands.ContainsKey(command) || index >= maskStart && index < maskEnd)).Select(command => command.Frame).ToHashSet());
        var logical = visible; visible = draw;
        // ARC-RENDER-014: HD textures for characters and objects, after the Legacy set (every
        // fallback draws Legacy, so the Legacy visible set stays resident under both profiles).
        lap?.Step("hd");
        var hd = BindAreaHd(visible); var hdScale = HdActive ? ActiveHdSource.Scale : 1;
        lap?.Step("items");
        while (_areaItems.Count < visible.Length)
        {
            var item = new AreaDrawItem { TextureFilter = TextureFilterEnum.Nearest, Mappings = _areaMappings };
            AddChild(item); _areaItems.Add(item);
        }
        // Hide only items that were shown by the previous submission.
        for (var index = visible.Length; index < _areaActive; index++)
        {
            var item = _areaItems[index];
            item.Hide(); item.FrameTexture = null; item.HdScale = 1; item.ReleaseMapping();
        }
        _areaActive = visible.Length;
        for (var index = 0; index < visible.Length; index++)
        {
            var item = _areaItems[index];
            var command = visible[index];
            ImageTexture texture;
            var isActor = command.Identity == ActorSentinel;
            var mask = index >= maskStart && index < maskEnd; var roof = index >= roofStart;
            if (!mask && _areaChunkCommands.TryGetValue(command, out var chunk)) texture = _areaChunkTextures.Acquire(_areaChunks!, chunk, AreaChunkLevel);
            else if (!(isActor ? _textures.TryGet(_actorPlan.Current.TextureKey, out texture!) : _textures.TryGet(command.Frame, out texture!)))
                throw new InvalidOperationException("AREA_VISIBLE_TEXTURE_MISSING");
            if (hd[index].Texture is { } hdTexture) { texture = hdTexture; item.HdScale = hdScale; } else item.HdScale = 1;
            item.FrameTexture = texture; item.Command = command; item.Actor = isActor ? _actorPlan.Current : null; item.ShowGuide = _showActorGuides;
            // A roof is tinted by the outdoor light only (RF2); a covered tile's mask is black.
            if (mask) (item.Ambient, item.AmbientRight, item.Lattice, item.Carried) = (Colors.Black, Colors.Black, null, null);
            else if (roof) { var outdoor = AreaRoofLight(); (item.Ambient, item.AmbientRight, item.Lattice, item.Carried) = (outdoor, outdoor, null, null); }
            else if (index == highlightIndex) (item.Ambient, item.AmbientRight, item.Lattice, item.Carried) = (AreaHighlightColour, AreaHighlightColour, null, null);
            else (item.Ambient, item.AmbientRight, item.Lattice, item.Carried) = AreaItemLight(command, isActor);
            item.RoofCorners = roof ? roofs[index - roofStart].Corners : null;
            // The source drops every alpha mode for an additive (or sub/mul) object, and a constant-alpha body
            // replaces them with its own, so such a wall never fades (ARC-RENDER-018).
            item.WallFade = isActor || command.Additive || command.Unlit ? AreaWallFade.None : AreaWallFadeOf(command);
            item.PrepareMapping(command);
            item.Show(); item.QueueRedraw();
        }
        TrimAreaPool();
        AreaVisibleCommands = Array.AsReadOnly(logical.Where(command => command.Identity != ActorSentinel).ToArray());
        AreaVisibleCount = AreaVisibleCommands.Count;
        _areaView = state;
    }
    // A large view (Fit at minimum zoom) must not leave tens of thousands of hidden
    // nodes behind once the view shrinks. Every frame, still or moving, frees at most
    // AreaPoolTrimPerUpdate hidden items until max(floor, 2 x shown) remain, so one
    // zoom change does not stall a frame on node teardown.
    private void TrimAreaPool()
    {
        var keep = Math.Max(Math.Max(AreaPoolFloor, checked(2 * _areaActive)), _areaItems.Count - AreaPoolTrimPerUpdate);
        while (_areaItems.Count > keep)
        {
            var last = _areaItems.Count - 1; var item = _areaItems[last];
            _areaItems.RemoveAt(last); item.FrameTexture = null; RemoveChild(item); item.Free();
        }
    }
    // Level k >= 1 (texels 2^(k-1) mod 2^k only) is exact only at zoom 2^-k on both axes
    // with an integer raster origin; full-resolution chunks are exact at any zoom.
    private static int ChunkLevel(Transform2D transform)
    {
        var o = transform.Origin;
        if (transform.X.X != transform.Y.Y || MathF.Floor(o.X) != o.X || MathF.Floor(o.Y) != o.Y) return 0;
        for (var level = AreaTerrainChunks.MaxLevel; level >= 1; level--)
            if (transform.X.X == 1f / (1 << level)) return level;
        return 0;
    }
    private void ClearArea()
    {
        _area = null; _areaFrames = null; _areaActorPlacement = null; _areaIndex = null; _areaFlat = Array.Empty<AreaDrawCommand>(); _areaAmbient = null; _areaWallFades = null; _areaHidden = null; _areaMoving = null; _areaMovingKeys = new HashSet<AreaObjectKey?>(); _areaAbsent = null; _areaRoofs = null; _areaCoveredTiles = null; _areaCovered = null; DisposeAreaLattice(); DisposeAreaCarried(); _areaCarriedNow = null; _areaCarriedBinding = null;
        _areaView = null; _areaUpright = null; _areaActive = 0;
        foreach (var item in _areaItems) { item.FrameTexture = null; RemoveChild(item); item.Free(); }
        _areaItems.Clear(); AreaVisibleCount = 0; AreaVisibleCommands = Array.Empty<AreaDrawCommand>(); AreaVisibleCovers = AreaVisibleCovers.None;
        _areaMappings.Clear(); // after the items stopped sampling the rows
        _areaChunkTextures.Clear(); _areaChunks = null; _areaChunkCommands.Clear(); AreaChunkCount = 0; _areaChunkChannels.Clear();
        ReleaseAreaHd(); // after the items stopped sampling HD textures
    }
    public override void _ExitTree()
    {
        ClearArea();
        ReleaseHd(); _hdOwnedSource?.Dispose(); _hdOwnedSource = null;
    }
}
