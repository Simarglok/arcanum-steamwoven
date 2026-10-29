using Arcanum.Data.Terrain;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Closed, source-derived state accepted by the fixed actor diagnostic.</summary>
/// <remarks>ARC-COMBAT-021: <see cref="FallDown"/> and <see cref="GetUp"/> are the hero body's fall (animation 7) and getting up (8);
/// ARC-RENDER-039: <see cref="LowAttack"/> and <see cref="Throw"/> its low attack (21) and throw (14).</remarks>
public enum ActorDiagnosticAction { Idle, Walk, Run, Vault, Attack, FallDown, GetUp, LowAttack, Throw }
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
    int EffectiveOffsetX, int EffectiveOffsetY, int Frame, int FrameCount, int RawFramesPerSecond)
{
    public ActorDiagnosticProfileId ProfileId { get; init; } = ActorDiagnosticProfileId.MonsterRegression;
    public int SourceFacing { get; init; }
}

public sealed class ActorDiagnosticScenePlan
{
    private ActorDiagnosticScenePlan(IReadOnlyDictionary<(ActorDiagnosticAction Action, int Group, int Frame), ActorDiagnosticFrame> catalog,
        ActorDiagnosticDrawCommand current, IReadOnlyList<TerrainRect> admittedBounds, ActorDiagnosticProfile profile)
    {
        Profile = profile;
        Catalog = catalog;
        Current = current;
        AdmittedBounds = admittedBounds;
    }

    public ActorDiagnosticProfile Profile { get; }
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
        ValidateSelectionInput(input);
        var facing = Profile.Facing(input.Action, input.Facing);
        if (!Catalog.TryGetValue((input.Action, facing.PhysicalGroup, input.Frame), out var selected))
            throw Failure(TerrainViewerErrorCode.ActorFrame);
        var selectedInput = input with { Width = selected.Width, Height = selected.Height, HotX = selected.HotX, HotY = selected.HotY,
            ArtOffsetX = selected.ArtOffsetX, ArtOffsetY = selected.ArtOffsetY };
        return new ActorDiagnosticScenePlan(Catalog, Place(selectedInput, selected, facing, Profile), AdmittedBounds, Profile);
    }

    public static ActorDiagnosticScenePlan CreateSynthetic(ActorDiagnosticPlacementInput input, IEnumerable<ActorDiagnosticFrame> frames,
        ActorDiagnosticProfile? profile = null)
    {
        profile ??= ActorDiagnosticProfile.MonsterRegression;
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(frames);
        ValidateSelectionInput(input);
        var catalog = frames.ToArray();
        if (catalog.Length == 0) throw Failure(TerrainViewerErrorCode.ActorMetadata);
        var facing = profile.Facing(input.Action, input.Facing);
        // This is a fixed two-resource profile, not a general actor catalogue.
        // Validate each descriptor before grouping so a substituted resource,
        // selector, action, timing, or frame range never becomes a plausible
        // missing-frame result later in selection.
        foreach (var item in catalog) profile.ValidateFrame(item);
        var grouped = catalog.GroupBy(frame => (Action: profile.ActionFor(frame.TextureKey.Selector), frame.TextureKey.PhysicalGroup, frame.TextureKey.Frame)).ToArray();
        if (grouped.Any(group => group.Count() != 1)) throw Failure(TerrainViewerErrorCode.ActorMetadata);
        var lookup = grouped.ToDictionary(group => group.Key, group => group.Single());
        if (!lookup.TryGetValue((input.Action, facing.PhysicalGroup, input.Frame), out var selected)) throw Failure(TerrainViewerErrorCode.ActorFrame);
        var current = Place(input, selected, facing, profile);
        var all = new List<TerrainRect>();
        foreach (var item in lookup.Values)
        {

            var action = profile.ActionFor(item.TextureKey.Selector);
            foreach (var slot in Enumerable.Range(0, 8))
            {
                var binding = profile.Facing(action, slot);
                if (binding.PhysicalGroup != item.TextureKey.PhysicalGroup) continue;
                var frameInput = input with { Action = action, Frame = item.TextureKey.Frame, Facing = slot,
                    Width = item.Width, Height = item.Height, HotX = item.HotX, HotY = item.HotY,
                    ArtOffsetX = item.ArtOffsetX, ArtOffsetY = item.ArtOffsetY };
                all.Add(VisibleGuideBounds(Place(frameInput, item, binding, profile)));
            }
        }
        return new ActorDiagnosticScenePlan(lookup, current, Array.AsReadOnly(all.ToArray()), profile);
    }

    public static ActorDiagnosticDrawCommand Place(ActorDiagnosticPlacementInput input, ActorDiagnosticFrame frame,
        ActorDiagnosticProfile? profile = null)
    {
        profile ??= ActorDiagnosticProfile.MonsterRegression;
        ArgumentNullException.ThrowIfNull(input);
        ValidateSelectionInput(input);
        profile.ValidateFrame(frame);
        if (profile.ActionFor(frame.TextureKey.Selector) != input.Action)
            throw Failure(TerrainViewerErrorCode.ActorMetadata);
        return Place(input, frame, profile.Facing(input.Action, input.Facing), profile);
    }

    private static ActorDiagnosticDrawCommand Place(ActorDiagnosticPlacementInput input, ActorDiagnosticFrame frame,
        ActorDiagnosticFacing facing, ActorDiagnosticProfile profile)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateSelectionInput(input);
        profile.ValidateFrame(frame);
        if (profile.ActionFor(frame.TextureKey.Selector) != input.Action || facing.PhysicalGroup != frame.TextureKey.PhysicalGroup)
            throw Failure(TerrainViewerErrorCode.ActorMetadata);
        if (input.Frame < 0 || input.Frame >= frame.FramesPerAction || input.Frame != frame.TextureKey.Frame
            || input.Width != frame.Width || input.Height != frame.Height ||
            input.Width <= 0 || input.Height <= 0)
            throw Failure(TerrainViewerErrorCode.ActorFrame);
        try
        {
            var projectedX = checked(input.CameraOriginX!.Value + checked(40L * checked(checked(input.MapY!.Value - input.MapX!.Value) - 1L)));
            var projectedY = checked(input.CameraOriginY!.Value + checked(20L * checked(input.MapY!.Value + input.MapX!.Value)));
            var anchorX = checked(checked(projectedX + input.InstanceOffsetX!.Value) + 40L);
            var anchorY = checked(checked(projectedY + input.InstanceOffsetY!.Value) + 20L);
            var effectiveHotX = facing.FlipX ? checked(frame.Width - frame.HotX - 1) : frame.HotX;
            var effectiveOffsetX = facing.FlipX ? checked(-frame.ArtOffsetX) : frame.ArtOffsetX;
            var left = checked(anchorX - effectiveHotX);
            var top = checked(anchorY - frame.HotY);
            // Form every extent in widened arithmetic before narrowing any
            // edge.  This makes overflow at left/top/right/bottom/extent a
            // deterministic ActorPlan failure rather than an accidental
            // implementation-order artefact.
            var rightWide = checked(left + frame.Width);
            var bottomWide = checked(top + frame.Height);
            var narrowLeft = checked((int)left);
            var narrowTop = checked((int)top);
            var right = checked((int)rightWide);
            var bottom = checked((int)bottomWide);
            return new ActorDiagnosticDrawCommand(frame.TextureKey, new TerrainRect(narrowLeft, narrowTop,
                checked(right - narrowLeft), checked(bottom - narrowTop)),
                anchorX, anchorY, input.Action, input.Facing, facing.PhysicalGroup, facing.FlipX, frame.ArtOffsetX, frame.ArtOffsetY,
                effectiveOffsetX, frame.ArtOffsetY, input.Frame, frame.FramesPerAction, frame.RawFramesPerSecond)
                { ProfileId = profile.Id, SourceFacing = facing.SourceFacing };
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

    /// <summary>
    /// Public selection methods validate the closed caller state before they
    /// access a facing or catalogue, so invalid input cannot become a
    /// misleading missing-frame result.
    /// </summary>
    private static void ValidateSelectionInput(ActorDiagnosticPlacementInput input)
    {
        if (input.MapX is null || input.MapY is null || input.CameraOriginX is null || input.CameraOriginY is null ||
            input.InstanceOffsetX is null || input.InstanceOffsetY is null || input.Shrunk is null)
            throw Failure(TerrainViewerErrorCode.ActorInput);
        if (!Enum.IsDefined(input.Action)) throw Failure(TerrainViewerErrorCode.ActorInput);
        if (input.RenderBranch != ActorDiagnosticRenderBranch.NormalUncached || input.ScalePercent != 100 || input.Shrunk != false)
            throw Failure(TerrainViewerErrorCode.ActorRenderState);
        _ = MapFacing(input.Facing);
    }

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
