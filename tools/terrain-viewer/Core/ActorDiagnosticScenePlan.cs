using Arcanum.Data.Terrain;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Closed, source-derived state accepted by the fixed actor diagnostic.</summary>
public enum ActorDiagnosticAction { Idle, Walk }
public enum ActorDiagnosticRenderBranch { NormalUncached, CachedUnsupported, Unknown }

/// <summary>Path-free identity for one selected actor ART frame.</summary>
public readonly record struct ActorDiagnosticTextureKey(int ResourceOrdinal, uint Selector, int PhysicalGroup, int Frame, int Palette);

/// <summary>Caller-selected normal-branch input. No field has an inferred default.</summary>
public sealed record ActorDiagnosticPlacementInput(long? MapX, long? MapY, long? CameraOriginX, long? CameraOriginY,
    int? InstanceOffsetX, int? InstanceOffsetY, ActorDiagnosticRenderBranch RenderBranch, int ScalePercent, bool? Shrunk,
    ActorDiagnosticAction Action, int Facing, int Frame, int Width, int Height, int HotX, int HotY, int ArtOffsetX, int ArtOffsetY);

/// <summary>One admitted frame's geometry and source identity, retained without ART bytes.</summary>
public sealed record ActorDiagnosticFrame(ActorDiagnosticTextureKey TextureKey, int Width, int Height, int HotX, int HotY,
    int ArtOffsetX, int ArtOffsetY, int FramesPerAction, int RawFramesPerSecond);

/// <summary>Public production planning seam. Its destination deliberately excludes ART frame offsets.</summary>
public sealed record ActorDiagnosticDrawCommand(ActorDiagnosticTextureKey TextureKey, TerrainRect Destination, long AnchorX, long AnchorY,
    ActorDiagnosticAction Action, int RequestedFacing, int PhysicalGroup, bool FlipX, int SourceOffsetX, int SourceOffsetY,
    int EffectiveOffsetX, int EffectiveOffsetY, int Frame, int FrameCount, int RawFramesPerSecond);

public sealed class ActorDiagnosticScenePlan
{
    private ActorDiagnosticScenePlan(IReadOnlyDictionary<(ActorDiagnosticAction Action, int Group, int Frame), ActorDiagnosticFrame> catalog,
        ActorDiagnosticDrawCommand current, IReadOnlyList<TerrainRect> admittedBounds)
    {
        Catalog = catalog;
        Current = current;
        AdmittedBounds = admittedBounds;
    }

    public IReadOnlyDictionary<(ActorDiagnosticAction Action, int Group, int Frame), ActorDiagnosticFrame> Catalog { get; }
    public ActorDiagnosticDrawCommand Current { get; }
    /// <summary>
    /// Every visible actor primitive at the fixed caller anchor, including the
    /// magenta rectangle and yellow anchor cross, used to stabilize camera fit.
    /// </summary>
    public IReadOnlyList<TerrainRect> AdmittedBounds { get; }

    /// <summary>Rebuilds only the selected draw command from the immutable caller anchor and decoded catalogue.</summary>
    public ActorDiagnosticScenePlan Select(ActorDiagnosticPlacementInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var facing = MapFacing(input.Facing);
        if (!Catalog.TryGetValue((input.Action, facing.Group, input.Frame), out var selected))
            throw Failure(TerrainViewerErrorCode.ActorFrame);
        var selectedInput = input with { Width = selected.Width, Height = selected.Height, HotX = selected.HotX, HotY = selected.HotY,
            ArtOffsetX = selected.ArtOffsetX, ArtOffsetY = selected.ArtOffsetY };
        return new ActorDiagnosticScenePlan(Catalog, Place(selectedInput, selected, facing), AdmittedBounds);
    }

    public static ActorDiagnosticScenePlan CreateSynthetic(ActorDiagnosticPlacementInput input, IEnumerable<ActorDiagnosticFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var catalog = frames.ToArray();
        if (catalog.Length == 0) throw Failure(TerrainViewerErrorCode.ActorMetadata);
        var facing = MapFacing(input.Facing);
        var grouped = catalog.GroupBy(frame => (Action: ActionFor(frame.TextureKey.Selector), frame.TextureKey.PhysicalGroup, frame.TextureKey.Frame)).ToArray();
        if (grouped.Any(group => group.Count() != 1)) throw Failure(TerrainViewerErrorCode.ActorMetadata);
        var lookup = grouped.ToDictionary(group => group.Key, group => group.Single());
        if (!lookup.TryGetValue((input.Action, facing.Group, input.Frame), out var selected)) throw Failure(TerrainViewerErrorCode.ActorFrame);
        var current = Place(input, selected, facing);
        var all = new List<TerrainRect>();
        foreach (var item in lookup.Values)
        {
            if (item.TextureKey.Palette != 1 || item.TextureKey.PhysicalGroup is < 0 or > 7 || item.Width <= 0 || item.Height <= 0 || item.FramesPerAction <= 0 || item.RawFramesPerSecond <= 0)
                throw Failure(TerrainViewerErrorCode.ActorMetadata);
            // Apply this frame at the unchanged caller anchor. Groups 5..7
            // have both an unmirrored request and a reflected 1..3 request;
            // both rectangles participate in stable diagnostic fit bounds.
            var frameInput = input with { Action = ActionFor(item.TextureKey.Selector), Frame = item.TextureKey.Frame,
                Facing = item.TextureKey.PhysicalGroup, Width = item.Width, Height = item.Height, HotX = item.HotX, HotY = item.HotY,
                ArtOffsetX = item.ArtOffsetX, ArtOffsetY = item.ArtOffsetY };
            all.Add(VisibleGuideBounds(Place(frameInput, item, new ActorPhysicalFacing(item.TextureKey.PhysicalGroup, false))));
            var mirroredRequest = item.TextureKey.PhysicalGroup switch { 7 => 1, 6 => 2, 5 => 3, _ => -1 };
            if (mirroredRequest >= 0)
                all.Add(VisibleGuideBounds(Place(frameInput with { Facing = mirroredRequest }, item, new ActorPhysicalFacing(item.TextureKey.PhysicalGroup, true))));
        }
        return new ActorDiagnosticScenePlan(lookup, current, Array.AsReadOnly(all.ToArray()));
    }

    public static ActorDiagnosticDrawCommand Place(ActorDiagnosticPlacementInput input, ActorDiagnosticFrame frame)
        => Place(input, frame, MapFacing(input.Facing));

    private static ActorDiagnosticDrawCommand Place(ActorDiagnosticPlacementInput input, ActorDiagnosticFrame frame, ActorPhysicalFacing facing)
    {
        if (input.MapX is null || input.MapY is null || input.CameraOriginX is null || input.CameraOriginY is null ||
            input.InstanceOffsetX is null || input.InstanceOffsetY is null || input.Shrunk is null)
            throw Failure(TerrainViewerErrorCode.ActorInput);
        if (input.RenderBranch != ActorDiagnosticRenderBranch.NormalUncached || input.ScalePercent != 100 || input.Shrunk != false)
            throw Failure(TerrainViewerErrorCode.ActorRenderState);
        if (!Enum.IsDefined(input.Action))
            throw Failure(TerrainViewerErrorCode.ActorInput);
        if (frame.TextureKey.Palette != 1 || facing.Group != frame.TextureKey.PhysicalGroup)
            throw Failure(TerrainViewerErrorCode.ActorMetadata);
        if (input.Frame < 0 || input.Frame >= frame.FramesPerAction || input.Width != frame.Width || input.Height != frame.Height ||
            input.Width <= 0 || input.Height <= 0)
            throw Failure(TerrainViewerErrorCode.ActorFrame);
        try
        {
            var projectedX = checked(input.CameraOriginX.Value + checked(40L * checked(checked(input.MapY.Value - input.MapX.Value) - 1L)));
            var projectedY = checked(input.CameraOriginY.Value + checked(20L * checked(input.MapY.Value + input.MapX.Value)));
            var anchorX = checked(checked(projectedX + input.InstanceOffsetX.Value) + 40L);
            var anchorY = checked(checked(projectedY + input.InstanceOffsetY.Value) + 20L);
            var effectiveHotX = facing.FlipX ? checked(frame.Width - frame.HotX - 1) : frame.HotX;
            var effectiveOffsetX = facing.FlipX ? checked(-frame.ArtOffsetX) : frame.ArtOffsetX;
            var left = checked(anchorX - effectiveHotX);
            var top = checked(anchorY - frame.HotY);
            // Drawing uses integer Rect2 coordinates.  Validate and narrow all
            // four extrema before constructing the command, rather than merely
            // proving the right/bottom in widened arithmetic.
            var narrowLeft = checked((int)left);
            var narrowTop = checked((int)top);
            var right = checked((int)checked(left + frame.Width));
            var bottom = checked((int)checked(top + frame.Height));
            return new ActorDiagnosticDrawCommand(frame.TextureKey, new TerrainRect(narrowLeft, narrowTop,
                checked(right - narrowLeft), checked(bottom - narrowTop)),
                anchorX, anchorY, input.Action, input.Facing, facing.Group, facing.FlipX, frame.ArtOffsetX, frame.ArtOffsetY,
                effectiveOffsetX, frame.ArtOffsetY, input.Frame, frame.FramesPerAction, frame.RawFramesPerSecond);
        }
        catch (OverflowException)
        {
            throw Failure(TerrainViewerErrorCode.ActorPlan);
        }
    }

    public static ActorPhysicalFacing MapFacing(int facing) => facing switch
    {
        0 => new(0, false), 1 => new(7, true), 2 => new(6, true), 3 => new(5, true),
        4 => new(4, false), 5 => new(5, false), 6 => new(6, false), 7 => new(7, false), _ => throw Failure(TerrainViewerErrorCode.ActorFacing)
    };

    private static TerrainRect VisibleGuideBounds(ActorDiagnosticDrawCommand command)
    {
        try
        {
            // DrawRect's one-pixel outline is contained by the draw rectangle.
            // DrawLine endpoints make the anchor cross visible from -4 through
            // +4 inclusive, which is represented by an exclusive +5 edge.
            var left = Math.Min(command.Destination.X, checked((int)checked(command.AnchorX - 4)));
            var top = Math.Min(command.Destination.Y, checked((int)checked(command.AnchorY - 4)));
            var right = Math.Max(checked(command.Destination.X + command.Destination.Width), checked((int)checked(command.AnchorX + 5)));
            var bottom = Math.Max(checked(command.Destination.Y + command.Destination.Height), checked((int)checked(command.AnchorY + 5)));
            return new TerrainRect(left, top, checked(right - left), checked(bottom - top));
        }
        catch (OverflowException)
        {
            throw Failure(TerrainViewerErrorCode.ActorPlan);
        }
    }

    // Synthetic catalogues use the documented selector action bit and production keeps action explicit.
    private static ActorDiagnosticAction ActionFor(uint selector) => selector switch
    {
        ActorDiagnosticSource.IdleSelector => ActorDiagnosticAction.Idle,
        ActorDiagnosticSource.WalkSelector => ActorDiagnosticAction.Walk,
        _ => throw Failure(TerrainViewerErrorCode.ActorMetadata)
    };
    private static TerrainViewerException Failure(TerrainViewerErrorCode code) => new(new TerrainViewerError(code, Stage: code switch
    {
        TerrainViewerErrorCode.ActorFacing => TerrainViewerFailureStage.ActorFacing,
        TerrainViewerErrorCode.ActorInput => TerrainViewerFailureStage.ActorInput,
        TerrainViewerErrorCode.ActorRenderState => TerrainViewerFailureStage.ActorRenderState,
        TerrainViewerErrorCode.ActorMetadata => TerrainViewerFailureStage.ActorMetadata,
        TerrainViewerErrorCode.ActorFrame => TerrainViewerFailureStage.ActorFrame,
        TerrainViewerErrorCode.ActorDecode => TerrainViewerFailureStage.ActorDecode,
        _ => TerrainViewerFailureStage.ActorPlan
    }));
}

public readonly record struct ActorPhysicalFacing(int Group, bool FlipX);
