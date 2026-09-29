using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

/// <summary>
/// Public-data-only deterministic actor fixture for the application self-test.
/// The fixture constructs bounded decoded inputs; production publication, cache,
/// canvas, controls and timeline remain the code under test.
/// </summary>
internal static class SyntheticActorFixture
{
    internal const int Width = 9;
    internal const int Height = 7;
    internal static readonly (byte R, byte G, byte B) IdlePixel = (231, 37, 19);
    internal static readonly (byte R, byte G, byte B) WalkPixel = (18, 197, 71);

    internal static SyntheticActorLoad Create()
    {
        var input = new ActorDiagnosticPlacementInput(0, 0, 100, 40, 0, 0,
            ActorDiagnosticRenderBranch.NormalUncached, 100, false, ActorDiagnosticAction.Idle, 0, 0,
            Width, Height, 4, 3, 0, 0);
        var frames = new List<ActorDiagnosticFrame>(TerrainCachePolicy.MaxActorDistinctKeys);
        var textures = new Dictionary<ActorDiagnosticTextureKey, DecodedActorTexture>(TerrainCachePolicy.MaxActorDistinctKeys);
        AddAction(ActorDiagnosticAction.Idle, ActorDiagnosticSource.IdleResourceOrdinal, ActorDiagnosticSource.IdleSelector,
            ActorDiagnosticSource.IdleFrames, ActorDiagnosticSource.IdleFps, IdlePixel, frames, textures);
        AddAction(ActorDiagnosticAction.Walk, ActorDiagnosticSource.WalkResourceOrdinal, ActorDiagnosticSource.WalkSelector,
            ActorDiagnosticSource.WalkFrames, ActorDiagnosticSource.WalkFps, WalkPixel, frames, textures);
        var actor = ActorDiagnosticScenePlan.CreateSynthetic(input, frames);
        var terrainKey = new TerrainTextureKey("synthetic", 1, 0, 1, "terrain", 0, 0, 0);
        var terrain = TerrainScenePlan.CreateSynthetic(new TerrainRect(0, 0, 200, 100),
        [
            new TerrainPlanInput(1, terrainKey, TerrainKind.Tile, 0, 0, 0, 200, 100,
                new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 0, 0, 0))
        ]);
        var terrainTextures = new Dictionary<TerrainTextureKey, DecodedTerrainTexture>
        {
            [terrainKey] = new(terrainKey, 200, 100, Solid(200, 100, 41, 82, 143))
        };
        var fingerprint = new InputFingerprint(1, "synthetic");
        var result = new TerrainLoadResult(1, terrain, terrainTextures, 1, 1, 0, 0, 0,
            fingerprint, fingerprint, fingerprint, fingerprint, 200L * 100 * 4 + textures.Count * Width * Height * 4, 0, 1)
        {
            ActorPlan = actor,
            ActorTextures = textures,
            DiagnosticPlan = DiagnosticScenePlan.Create(terrain, null, actor)
        };
        var arguments = TerrainViewerArguments.Parse([
            "--mode", "actor-diagnostic", "--module", "synthetic-module", "--resource", "synthetic-terrain",
            "--actor-resource", "synthetic-actor", "--prototypes", "synthetic-prototypes", "--source-id", "synthetic",
            "--actor-map-x", "0", "--actor-map-y", "0", "--actor-camera-origin-x", "100", "--actor-camera-origin-y", "40",
            "--actor-offset-x", "0", "--actor-offset-y", "0", "--actor-render-branch", "normal-uncached",
            "--actor-scale-percent", "100", "--actor-shrunk", "false", "--actor-action", "idle", "--actor-facing", "0", "--actor-frame", "0"
        ]);
        return new SyntheticActorLoad(arguments, result);
    }

    private static void AddAction(ActorDiagnosticAction action, int ordinal, uint selector, int frameCount, int fps,
        (byte R, byte G, byte B) color, ICollection<ActorDiagnosticFrame> frames,
        IDictionary<ActorDiagnosticTextureKey, DecodedActorTexture> textures)
    {
        for (var group = 0; group < ActorDiagnosticSource.PhysicalGroups; group++)
        {
            for (var frame = 0; frame < frameCount; frame++)
            {
                var key = new ActorDiagnosticTextureKey(ordinal, selector, group, frame, ActorDiagnosticSource.Palette);
                frames.Add(new ActorDiagnosticFrame(key, Width, Height, 4, 3, 0, 0, frameCount, fps));
                textures.Add(key, new DecodedActorTexture(key, Width, Height, Asymmetric(color)));
            }
        }
    }

    private static byte[] Asymmetric((byte R, byte G, byte B) color)
    {
        var rgba = new byte[Width * Height * 4];
        Set(rgba, 1, 1, color.R, color.G, color.B, 255);
        Set(rgba, 7, 1, (byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B), 255);
        return rgba;
    }

    private static byte[] Solid(int width, int height, byte r, byte g, byte b)
    {
        var rgba = new byte[width * height * 4];
        for (var index = 0; index < rgba.Length; index += 4)
        {
            rgba[index] = r; rgba[index + 1] = g; rgba[index + 2] = b; rgba[index + 3] = 255;
        }
        return rgba;
    }

    private static void Set(byte[] rgba, int x, int y, byte r, byte g, byte b, byte a)
    {
        var offset = ((y * Width) + x) * 4;
        rgba[offset] = r; rgba[offset + 1] = g; rgba[offset + 2] = b; rgba[offset + 3] = a;
    }
}

internal sealed record SyntheticActorLoad(TerrainViewerArguments Arguments, TerrainLoadResult Result);
