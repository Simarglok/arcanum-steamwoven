using Godot;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-RENDER-010: the applied hourly pair and the channel of every drawn visual.</summary>
public sealed record AreaAmbientView(AmbientPair Pair, IReadOnlyDictionary<AreaVisualKey, AreaAmbientChannel> Channels)
{
    /// <summary>ARC-RENDER-012 stationary lights of the shown map at this hour; null draws ambient only.</summary>
    public AreaLocalLightView? Local { get; init; }
}

public partial class TerrainCanvas
{
    private AreaAmbientView? _areaAmbient;
    private IReadOnlyDictionary<AreaObjectKey, AreaWallFade>? _areaWallFades;
    private IReadOnlySet<AreaObjectKey>? _areaHidden;
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
    // nearest rule; see docs/research/crash-site-area-draw-calls.md); null keeps
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
    public int AreaCulledCount => (_area?.Commands.Count ?? 0) - AreaVisibleCount;
    public IReadOnlyList<AreaDrawCommand> AreaVisibleCommands { get; private set; } = Array.Empty<AreaDrawCommand>();
    private (Rect2 View, Transform2D Transform, ActorDiagnosticDrawCommand Actor,
        ActorDiagnosticPlacementInput Placement, bool ActorVisible, bool BodiesVisible, bool Chunks)? _areaView;

    public void PublishArea(AreaLoadResult area, ActorDiagnosticScenePlan actor, ActorDiagnosticPlacementInput placement, TerrainTextureCache textures)
        => PublishArea(area.Plan, area.Frames, actor, placement, textures);

    // Same production compositor, accepting the immutable numeric plan and
    // retained frame repository separately for independently authored fixtures.
    internal void PublishArea(AreaScenePlan plan, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> frames,
        ActorDiagnosticScenePlan actor, ActorDiagnosticPlacementInput placement, TerrainTextureCache textures)
    {
        if (!ReferenceEquals(_textures, textures)) _areaView = null;
        if (!ReferenceEquals(_area, plan) || !ReferenceEquals(_areaFrames, frames))
        {
            var chunks = AreaTerrainChunks.TryCreate(plan, frames);
            _areaView = null; _areaChunkTextures.Clear(); _areaChunks = chunks; _areaChunkChannels.Clear();
            if (_area is not null) ReleaseAreaHd(); // a map change releases the previous map's HD textures
        }
        if (!ReferenceEquals(_area, plan) || _areaUpright is null) _areaUpright = new AreaUprightOrder(plan.Commands, plan.Chart);
        if (!ReferenceEquals(_area, plan) || _areaIndex is null)
        {
            _areaFlat = plan.Commands.Where(command => command.Phase < 4).ToArray();
            _areaIndex = new AreaVisibilityIndex(_areaFlat.Select(command => command.Destination).ToArray());
        }
        _plan = null; _staticPlan = null;
        _area = plan; _areaFrames = frames; _actorPlan = actor; _areaActorPlacement = placement; _textures = textures;
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
    internal bool AreaHiddenOf(AreaDrawCommand command) => command.Identity is { } key && _areaHidden?.Contains(key) == true;
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
    // ARC-RENDER-012: terrain under a light lattice samples it per pixel; bodies take their CE object
    // colour (walls a two-end gradient), the actor its colour at the current placement.
    private (Color Left, Color Right, AreaLatticeBinding? Lattice) AreaItemLight(AreaDrawCommand command, bool isActor)
    {
        var ambient = AreaAmbientColor(command, isActor);
        if (_areaAmbient?.Local is not { } local) return (ambient, ambient, null);
        static Color Of(AmbientColor color) => new(color.R / 255f, color.G / 255f, color.B / 255f);
        if (isActor) { var actor = Of(local.Actor(_areaActorPlacement!)); return (actor, actor, null); }
        if (command.Identity is not null)
        {
            if (!local.Bodies.TryGetValue(command.Visual, out var body)) throw new InvalidOperationException("AREA_LIGHT_BODY_REQUIRED");
            return (Of(body.Left), Of(body.Right), null);
        }
        // Terrain (tiles, facades, their chunks) is always on the indoor or outdoor channel.
        return (ambient, ambient, local.Lattice is { } lattice ? AreaLattice(lattice) : null);
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
    // The actor is a critter: outdoor channel (CE palette callback), like other bodies.
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
    private void UpdateAreaVisible()
    {
        if (!IsInsideTree() || _area is null || _areaFrames is null || _textures is null || _actorPlan is null || _areaActorPlacement is null) return;
        var view = GetViewport().GetVisibleRect(); var inverse = GetViewport().GetCanvasTransform().AffineInverse();
        var state = (view, GetViewport().GetCanvasTransform(), _actorPlan.Current, _areaActorPlacement, _showActor, _showStatics, AreaTerrainChunksEnabled);
        // Pending HD frames that became ready rebind the same view (ARC-RENDER-014).
        if (_areaView == state && !HdRefreshDue()) { TrimAreaPool(); return; }
        var a = inverse * view.Position; var b = inverse * view.End;
        var world = new Rect2(a, b - a).Abs();
        bool Intersects(AreaDrawCommand command) => new Rect2(command.Destination.X, command.Destination.Y,
            command.Destination.Width, command.Destination.Height).Intersects(world);
        var upright = _areaUpright!.With(_areaActorPlacement, _actorPlan.Current, ActorSentinel);
        var flat = _areaIndex!.Candidates(world.Position.X, world.Position.Y, world.End.X, world.End.Y);
        var ordered = flat.Select(index => _areaFlat[index]).Concat(upright);
        var visible = ordered.Where(command => Intersects(command) && (command.Identity == ActorSentinel ? _showActor : command.Identity is null || (_showStatics && !AreaHiddenOf(command)))).ToArray();
        if (visible.Length > _area.Commands.Count + 1) throw new InvalidOperationException("AREA_SUBMISSION_BUDGET");
        // Index rows used by this submission stay resident; rows from earlier views are
        // reused across pans or evicted under the shared 16 MiB cap. The original ART
        // repository is not re-decoded.
        _areaMappings.BeginSubmission();
        // Terrain chunks replace the visible terrain commands in the draw list only;
        // AreaVisibleCommands below keeps the per-command visibility.
        var draw = visible; _areaChunkCommands.Clear(); AreaChunkCount = 0; AreaChunkLevel = 0; var chunked = false;
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
        _textures.UploadAreaVisible(_areaFrames, draw.Where(command => command.Identity != ActorSentinel && !_areaChunkCommands.ContainsKey(command))
            .Select(command => command.Frame).ToHashSet());
        var logical = visible; visible = draw;
        // ARC-RENDER-014: HD textures for characters and objects, after the Legacy set (every
        // fallback draws Legacy, so the Legacy visible set stays resident under both profiles).
        var hd = BindAreaHd(visible); var hdScale = HdActive ? ActiveHdSource.Scale : 1;
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
            if (_areaChunkCommands.TryGetValue(command, out var chunk)) texture = _areaChunkTextures.Acquire(_areaChunks!, chunk, AreaChunkLevel);
            else if (!(isActor ? _textures.TryGet(_actorPlan.Current.TextureKey, out texture!) : _textures.TryGet(command.Frame, out texture!)))
                throw new InvalidOperationException("AREA_VISIBLE_TEXTURE_MISSING");
            if (hd[index].Texture is { } hdTexture) { texture = hdTexture; item.HdScale = hdScale; } else item.HdScale = 1;
            item.FrameTexture = texture; item.Command = command; item.Actor = isActor ? _actorPlan.Current : null;
            (item.Ambient, item.AmbientRight, item.Lattice) = AreaItemLight(command, isActor);
            // The source drops every alpha mode for an additive (or sub/mul) object, so such a wall never fades.
            item.WallFade = isActor || command.Additive ? AreaWallFade.None : AreaWallFadeOf(command);
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
        _area = null; _areaFrames = null; _areaActorPlacement = null; _areaIndex = null; _areaFlat = Array.Empty<AreaDrawCommand>(); _areaAmbient = null; _areaWallFades = null; _areaHidden = null; DisposeAreaLattice();
        _areaView = null; _areaUpright = null; _areaActive = 0;
        foreach (var item in _areaItems) { item.FrameTexture = null; RemoveChild(item); item.Free(); }
        _areaItems.Clear(); AreaVisibleCount = 0; AreaVisibleCommands = Array.Empty<AreaDrawCommand>();
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
