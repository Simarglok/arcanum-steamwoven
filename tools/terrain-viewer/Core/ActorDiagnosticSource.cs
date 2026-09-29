using Arcanum.Data;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// Bounded reader for the one owner-authorized diagnostic profile.  It accepts no
/// names or general actor records: resource ordinals and ART metadata are the
/// complete source boundary exposed to the viewer.
/// </summary>
public static class ActorDiagnosticSource
{
    public const int ModuleOrdinal = 643;
    public const uint BaseSelector = 0xc9001810;
    public const uint IdleSelector = 0xc9001810;
    public const uint WalkSelector = 0xc9001850;
    public const int IdleResourceOrdinal = 4753;
    public const int WalkResourceOrdinal = 4754;
    public const int Palette = 1;
    public const int PhysicalGroups = 8;
    public const int IdleFrames = 15;
    public const int WalkFrames = 8;
    public const int IdleFps = 8;
    public const int WalkFps = 15;

    public static ActorDiagnosticLoad Create(DatArchive module, DatArchive resource, ActorDiagnosticPlacementInput input)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(resource);
        if (input is null || input.MapX is null || input.MapY is null || input.CameraOriginX is null || input.CameraOriginY is null
            || input.InstanceOffsetX is null || input.InstanceOffsetY is null || input.Shrunk is null)
            throw Failure(TerrainViewerErrorCode.ActorInput, TerrainViewerFailureStage.ActorInput);
        if (input.RenderBranch != ActorDiagnosticRenderBranch.NormalUncached || input.ScalePercent != 100 || input.Shrunk != false)
            throw Failure(TerrainViewerErrorCode.ActorRenderState, TerrainViewerFailureStage.ActorRenderState);
        if (!Enum.IsDefined(input.Action)) throw Failure(TerrainViewerErrorCode.ActorInput, TerrainViewerFailureStage.ActorInput);
        if (module.Entries.Count(entry => entry.Ordinal == ModuleOrdinal) != 1)
            throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        var catalog = new List<ActorDiagnosticFrame>(PhysicalGroups * (IdleFrames + WalkFrames));
        var textures = new Dictionary<ActorDiagnosticTextureKey, DecodedActorTexture>();
        DecodeAction(resource, ActorDiagnosticAction.Idle, IdleSelector, IdleResourceOrdinal, IdleFps, IdleFrames, catalog, textures);
        DecodeAction(resource, ActorDiagnosticAction.Walk, WalkSelector, WalkResourceOrdinal, WalkFps, WalkFrames, catalog, textures);
        ActorPhysicalFacing physical;
        try { physical = ActorDiagnosticScenePlan.MapFacing(input.Facing); }
        catch (TerrainViewerException) { throw; }
        var selected = catalog.SingleOrDefault(frame => frame.TextureKey.PhysicalGroup == physical.Group && frame.TextureKey.Frame == input.Frame
            && (input.Action == ActorDiagnosticAction.Walk ? frame.TextureKey.Selector == WalkSelector : frame.TextureKey.Selector == IdleSelector));
        if (selected is null) throw Failure(TerrainViewerErrorCode.ActorFrame, TerrainViewerFailureStage.ActorFrame);
        var plannedInput = input with { Width = selected.Width, Height = selected.Height, HotX = selected.HotX, HotY = selected.HotY,
            ArtOffsetX = selected.ArtOffsetX, ArtOffsetY = selected.ArtOffsetY };
        var plan = ActorDiagnosticScenePlan.CreateSynthetic(plannedInput, catalog);
        return new ActorDiagnosticLoad(plan, textures);
    }

    private static void DecodeAction(DatArchive resource, ActorDiagnosticAction action, uint selector, int ordinal, int fps, int expectedFrames,
        ICollection<ActorDiagnosticFrame> catalog, IDictionary<ActorDiagnosticTextureKey, DecodedActorTexture> textures)
    {
        var entries = resource.Entries.Where(candidate => candidate.Ordinal == ordinal).ToArray();
        if (entries.Length != 1) throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        var entry = entries[0];
        try
        {
            var bytes = resource.ReadEntry(entry);
            var metadata = ArtDecoder.Inspect(bytes, new ArtDecoderOptions { MaxInputBytes = 1024 * 1024, MaxFrameCount = PhysicalGroups * IdleFrames });
            if (metadata.Header.PhysicalGroupCount != PhysicalGroups || metadata.Header.FramesPerGroup != expectedFrames || metadata.Header.FramesPerSecond != fps ||
                metadata.Header.PalettePresenceWords.Count <= Palette || metadata.Header.PalettePresenceWords[Palette] == 0)
                throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
            for (var group = 0; group < PhysicalGroups; group++)
            {
                for (var frame = 0; frame < expectedFrames; frame++)
                {
                    var decoded = ArtDecoder.Decode(bytes, new ArtFrameSelection(group, frame, Palette), new ArtDecoderOptions { MaxInputBytes = 1024 * 1024, MaxFrameCount = PhysicalGroups * IdleFrames });
                    if (decoded.EffectivePaletteSlot != Palette) throw Failure(TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
                    var key = new ActorDiagnosticTextureKey(ordinal, selector, group, frame, Palette);
                    var geometry = decoded.Frame;
                    catalog.Add(new ActorDiagnosticFrame(key, decoded.Width, decoded.Height, geometry.HotX, geometry.HotY, geometry.OffsetX, geometry.OffsetY, expectedFrames, fps));
                    textures.Add(key, new DecodedActorTexture(key, decoded.Width, decoded.Height, decoded.Rgba.ToArray()));
                }
            }
        }
        catch (TerrainViewerException) { throw; }
        catch (DatException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (ArtException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (ArgumentException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
        catch (InvalidOperationException) { throw Failure(TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode); }
    }

    private static TerrainViewerException Failure(TerrainViewerErrorCode code, TerrainViewerFailureStage stage) => new(new TerrainViewerError(code, Stage: stage));
}

public sealed record DecodedActorTexture(ActorDiagnosticTextureKey Key, int Width, int Height, byte[] Rgba);
public sealed record ActorDiagnosticLoad(ActorDiagnosticScenePlan Plan, IReadOnlyDictionary<ActorDiagnosticTextureKey, DecodedActorTexture> Textures);
