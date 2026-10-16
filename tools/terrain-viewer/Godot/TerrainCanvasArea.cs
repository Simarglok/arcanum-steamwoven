using Godot;
using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

public partial class TerrainCanvas
{
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

    private static readonly AreaObjectKey ActorSentinel = new("controlled-session", 5051, AreaSourceKind.Mobile, null, int.MaxValue, 0, "controlled-session", 0);
    public Action<Exception>? AreaFailure { get; set; }
    public int AreaVisibleCount { get; private set; }
    internal int AreaPoolCount => _areaItems.Count;
    public int AreaCulledCount => (_area?.Commands.Count ?? 0) - AreaVisibleCount;
    public IReadOnlyList<AreaDrawCommand> AreaVisibleCommands { get; private set; } = Array.Empty<AreaDrawCommand>();
    private (Rect2 View, Transform2D Transform, ActorDiagnosticDrawCommand Actor,
        ActorDiagnosticPlacementInput Placement, bool ActorVisible, bool BodiesVisible)? _areaView;

    public void PublishArea(AreaLoadResult area, ActorDiagnosticScenePlan actor, ActorDiagnosticPlacementInput placement, TerrainTextureCache textures)
        => PublishArea(area.Plan, area.Frames, actor, placement, textures);

    // Same production compositor, accepting the immutable numeric plan and
    // retained frame repository separately for independently authored fixtures.
    internal void PublishArea(AreaScenePlan plan, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> frames,
        ActorDiagnosticScenePlan actor, ActorDiagnosticPlacementInput placement, TerrainTextureCache textures)
    {
        if (!ReferenceEquals(_area, plan) || !ReferenceEquals(_areaFrames, frames)) _areaView = null;
        if (!ReferenceEquals(_area, plan) || _areaUpright is null) _areaUpright = new AreaUprightOrder(plan.Commands);
        _plan = null; _staticPlan = null;
        _area = plan; _areaFrames = frames; _actorPlan = actor; _areaActorPlacement = placement; _textures = textures;
        UpdateAreaVisible(); QueueRedraw();
    }
    public override void _Process(double delta)
    {
        if (_area is null) return;
        try { UpdateAreaVisible(); }
        catch (Exception error) { AreaFailure?.Invoke(error); }
    }
    private void UpdateAreaVisible()
    {
        if (!IsInsideTree() || _area is null || _areaFrames is null || _textures is null || _actorPlan is null || _areaActorPlacement is null) return;
        var view = GetViewport().GetVisibleRect(); var inverse = GetViewport().GetCanvasTransform().AffineInverse();
        var state = (view, GetViewport().GetCanvasTransform(), _actorPlan.Current, _areaActorPlacement, _showActor, _showStatics);
        if (_areaView == state) { TrimAreaPool(); return; }
        var a = inverse * view.Position; var b = inverse * view.End;
        var world = new Rect2(a, b - a).Abs();
        bool Intersects(AreaDrawCommand command) => new Rect2(command.Destination.X, command.Destination.Y,
            command.Destination.Width, command.Destination.Height).Intersects(world);
        var upright = _areaUpright!.With(_areaActorPlacement, _actorPlan.Current, ActorSentinel);
        var ordered = _area.Commands.Where(command => command.Phase < 4).Concat(upright);
        var visible = ordered.Where(command => Intersects(command) && (command.Identity == ActorSentinel ? _showActor : command.Identity is null || _showStatics)).ToArray();
        if (visible.Length > _area.Commands.Count + 1) throw new InvalidOperationException("AREA_SUBMISSION_BUDGET");
        var mappingBytes = visible.Sum(command => AreaDrawItem.EstimateMappingBytes(GetViewport(), command));
        if (mappingBytes > AreaDrawItem.MaxMappingBytes) throw new InvalidOperationException("AREA_NEAREST_MAPPING_BUDGET");
        // Bound replacement overlap as well as final residency. Mapping metadata
        // may be evicted; the original ART repository is not re-decoded. Hidden
        // pool items have already released theirs.
        var liveMapping = 0L; for (var index = 0; index < _areaActive; index++) liveMapping += _areaItems[index].MappingBytes;
        if (mappingBytes + liveMapping > AreaDrawItem.MaxMappingBytes)
            for (var index = 0; index < _areaActive; index++) _areaItems[index].ReleaseMapping();
        _textures.UploadAreaVisible(_areaFrames, visible.Where(command => command.Identity != ActorSentinel).Select(command => command.Frame).ToHashSet());
        while (_areaItems.Count < visible.Length)
        {
            var item = new AreaDrawItem { TextureFilter = TextureFilterEnum.Nearest };
            AddChild(item); _areaItems.Add(item);
        }
        // Hide only items that were shown by the previous submission.
        for (var index = visible.Length; index < _areaActive; index++)
        {
            var item = _areaItems[index];
            item.Hide(); item.FrameTexture = null; item.ReleaseMapping();
        }
        _areaActive = visible.Length;
        for (var index = 0; index < visible.Length; index++)
        {
            var item = _areaItems[index];
            var command = visible[index];
            ImageTexture texture;
            var isActor = command.Identity == ActorSentinel;
            if (!(isActor ? _textures.TryGet(_actorPlan.Current.TextureKey, out texture!) : _textures.TryGet(command.Frame, out texture!)))
                throw new InvalidOperationException("AREA_VISIBLE_TEXTURE_MISSING");
            item.FrameTexture = texture; item.Command = command; item.Actor = isActor ? _actorPlan.Current : null;
            item.PrepareMapping(command);
            item.Show(); item.QueueRedraw();
        }
        TrimAreaPool();
        AreaVisibleCommands = Array.AsReadOnly(visible.Where(command => command.Identity != ActorSentinel).ToArray());
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
    private void ClearArea()
    {
        _area = null; _areaFrames = null; _areaActorPlacement = null;
        _areaView = null; _areaUpright = null; _areaActive = 0;
        foreach (var item in _areaItems) { item.FrameTexture = null; RemoveChild(item); item.Free(); }
        _areaItems.Clear(); AreaVisibleCount = 0; AreaVisibleCommands = Array.Empty<AreaDrawCommand>();
    }
    public override void _ExitTree()
    {
        ClearArea();
    }
}
