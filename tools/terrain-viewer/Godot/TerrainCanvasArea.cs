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

    private static readonly AreaObjectKey ActorSentinel = new("controlled-session", 5051, AreaSourceKind.Mobile, null, int.MaxValue, 0, "controlled-session", 0);
    public Action<Exception>? AreaFailure { get; set; }
    public int AreaVisibleCount { get; private set; }
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
        if (_areaView == state) return;
        var a = inverse * view.Position; var b = inverse * view.End;
        var world = new Rect2(a, b - a).Abs();
        bool Intersects(AreaDrawCommand command) => new Rect2(command.Destination.X, command.Destination.Y,
            command.Destination.Width, command.Destination.Height).Intersects(world);
        var upright = AreaScenePlan.UprightOrder(_area.Commands, _areaActorPlacement, _actorPlan.Current, ActorSentinel);
        var ordered = _area.Commands.Where(command => command.Phase < 4).Concat(upright);
        var visible = ordered.Where(command => Intersects(command) && (command.Identity == ActorSentinel ? _showActor : command.Identity is null || _showStatics)).ToArray();
        if (visible.Length > _area.Commands.Count + 1) throw new InvalidOperationException("AREA_SUBMISSION_BUDGET");
        var mappingBytes = visible.Sum(command => AreaDrawItem.EstimateMappingBytes(GetViewport(), command));
        if (mappingBytes > AreaDrawItem.MaxMappingBytes) throw new InvalidOperationException("AREA_NEAREST_MAPPING_BUDGET");
        // Bound replacement overlap as well as final residency. Mapping metadata
        // may be evicted; the original ART repository is not re-decoded.
        if (mappingBytes + _areaItems.Sum(item => item.MappingBytes) > AreaDrawItem.MaxMappingBytes)
            foreach (var item in _areaItems) item.ReleaseMapping();
        _textures.UploadAreaVisible(_areaFrames, visible.Where(command => command.Identity != ActorSentinel).Select(command => command.Frame).ToHashSet());
        while (_areaItems.Count < visible.Length)
        {
            var item = new AreaDrawItem { TextureFilter = TextureFilterEnum.Nearest };
            AddChild(item); _areaItems.Add(item);
        }
        for (var index = 0; index < _areaItems.Count; index++)
        {
            var item = _areaItems[index];
            if (index >= visible.Length) { item.Hide(); item.FrameTexture = null; item.ReleaseMapping(); continue; }
            var command = visible[index];
            ImageTexture texture;
            var isActor = command.Identity == ActorSentinel;
            if (!(isActor ? _textures.TryGet(_actorPlan.Current.TextureKey, out texture!) : _textures.TryGet(command.Frame, out texture!)))
                throw new InvalidOperationException("AREA_VISIBLE_TEXTURE_MISSING");
            item.FrameTexture = texture; item.Command = command; item.Actor = isActor ? _actorPlan.Current : null;
            item.PrepareMapping(command);
            item.Show(); item.QueueRedraw();
        }
        AreaVisibleCommands = Array.AsReadOnly(visible.Where(command => command.Identity != ActorSentinel).ToArray());
        AreaVisibleCount = AreaVisibleCommands.Count;
        _areaView = state;
    }
    private void ClearArea()
    {
        _area = null; _areaFrames = null; _areaActorPlacement = null;
        _areaView = null;
        foreach (var item in _areaItems) { item.FrameTexture = null; RemoveChild(item); item.Free(); }
        _areaItems.Clear(); AreaVisibleCount = 0; AreaVisibleCommands = Array.Empty<AreaDrawCommand>();
    }
    public override void _ExitTree()
    {
        ClearArea();
    }
}
