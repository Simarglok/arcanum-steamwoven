using System.Buffers.Binary;
using Arcanum.Data;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

/// <summary>
/// Public-data-only deterministic actor fixture for the application self-test.
/// It writes tiny asymmetric ART containers and decodes every texture through
/// the production <see cref="ArtDecoder"/> before normal publication.
/// </summary>
internal static class SyntheticActorFixture
{
    internal const int Width = 9;
    internal const int Height = 7;
    internal static readonly (byte R, byte G, byte B) IdleLeftPixel = (231, 37, 19);
    internal static readonly (byte R, byte G, byte B) IdleRightPixel = (24, 218, 236);
    internal static readonly (byte R, byte G, byte B) WalkLeftPixel = (18, 197, 71);
    internal static readonly (byte R, byte G, byte B) WalkRightPixel = (237, 58, 184);

    internal static SyntheticActorLoad Create(bool human = false)
    {
        var input = new ActorDiagnosticPlacementInput(0, 0, 100, 40, 0, 0,
            ActorDiagnosticRenderBranch.NormalUncached, 100, false, ActorDiagnosticAction.Idle, 0, 0,
            Width, Height, 4, 3, 0, 0);
        var frames = new List<ActorDiagnosticFrame>(TerrainCachePolicy.MaxActorDistinctKeys);
        var textures = new Dictionary<ActorDiagnosticTextureKey, DecodedActorTexture>(TerrainCachePolicy.MaxActorDistinctKeys);
        AddAction(ActorDiagnosticAction.Idle, human ? 2667 : 4753, human ? 0x20000000u : 0xc9001810u,
            human ? 1 : 15, human ? 6 : 8, IdleLeftPixel, IdleRightPixel, frames, textures, human);
        AddAction(ActorDiagnosticAction.Walk, human ? 2668 : 4754, human ? 0x20000040u : 0xc9001850u,
            human ? 10 : 8, human ? 17 : 15, WalkLeftPixel, WalkRightPixel, frames, textures, human);
        var actor = ActorDiagnosticScenePlan.CreateSynthetic(input, frames, human ? ActorDiagnosticProfile.HumanFixed : ActorDiagnosticProfile.MonsterRegression);
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
        (byte R, byte G, byte B) leftColor, (byte R, byte G, byte B) rightColor, ICollection<ActorDiagnosticFrame> frames,
        IDictionary<ActorDiagnosticTextureKey, DecodedActorTexture> textures, bool human)
    {
        var palette = human ? 0 : 1;
        var art = CreateActorArt(frameCount, fps, leftColor, rightColor, palette);
        for (var group = 0; group < ActorDiagnosticSource.PhysicalGroups; group++)
        {
            if (human && group is >= 1 and <= 3) continue;
            for (var frame = 0; frame < frameCount; frame++)
            {
                var key = new ActorDiagnosticTextureKey(ordinal, selector, group, frame, palette);
                var decoded = ArtDecoder.Decode(art, new ArtFrameSelection(group, frame, palette));
                frames.Add(new ActorDiagnosticFrame(key, decoded.Width, decoded.Height, decoded.Frame.HotX, decoded.Frame.HotY,
                    decoded.Frame.OffsetX, decoded.Frame.OffsetY, frameCount, fps));
                textures.Add(key, new DecodedActorTexture(key, decoded.Width, decoded.Height, decoded.Rgba.ToArray()));
            }
        }
    }

    private static byte[] CreateActorArt(int framesPerGroup, int fps, (byte R, byte G, byte B) leftColor, (byte R, byte G, byte B) rightColor, int palette)
    {
        const int headerBytes = 132;
        const int paletteBytes = 1024;
        const int frameHeaderBytes = 28;
        var frameCount = ActorDiagnosticSource.PhysicalGroups * framesPerGroup;
        var payloadBytes = Width * Height;
        var tableOffset = headerBytes + paletteBytes;
        var payloadOffset = tableOffset + (frameCount * frameHeaderBytes);
        var bytes = new byte[payloadOffset + (frameCount * payloadBytes)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), fps);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12 + palette * 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), framesPerGroup);
        SetPalette(bytes, 1, leftColor);
        SetPalette(bytes, 2, rightColor);
        for (var index = 0; index < frameCount; index++)
        {
            var header = tableOffset + (index * frameHeaderBytes);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header), Width);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header + 4), Height);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header + 8), payloadBytes);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header + 12), 4);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header + 16), 3);
            var payload = payloadOffset + (index * payloadBytes);
            bytes[payload + Width + 1] = 1;
            bytes[payload + Width + 7] = 2;
        }
        return bytes;
    }

    private static void SetPalette(byte[] bytes, int index, (byte R, byte G, byte B) color)
    {
        var offset = 132 + (index * 4);
        bytes[offset] = color.B;
        bytes[offset + 1] = color.G;
        bytes[offset + 2] = color.R;
        bytes[offset + 3] = byte.MaxValue;
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

}

internal sealed record SyntheticActorLoad(TerrainViewerArguments Arguments, TerrainLoadResult Result);
