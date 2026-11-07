using System.Buffers.Binary;
using System.Text;
using Arcanum.Data;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// Public synthetic 1TAD/ART producer for exercising the production actor
/// source boundary. It deliberately creates real archives rather than a parser
/// double: the module has ordinal 643 and the resource has ordinals 4753/4754.
/// </summary>
internal sealed class SyntheticActorSourceFixture : IDisposable
{
    private readonly byte[] _resourceBytes;
    private readonly DatReaderOptions? _resourceOptions;
    private readonly TrackingMemoryStream _moduleStream;
    private TrackingMemoryStream? _openedResource;

    private SyntheticActorSourceFixture(byte[] moduleBytes, byte[] resourceBytes, DatReaderOptions? resourceOptions)
    {
        _moduleStream = new TrackingMemoryStream(moduleBytes);
        Module = DatArchive.Open(_moduleStream);
        _resourceBytes = resourceBytes;
        _resourceOptions = resourceOptions;
    }

    public DatArchive Module { get; }
    public int ModuleDisposeCount => _moduleStream.DisposeCount;
    public int ResourceDisposeCount => _openedResource?.DisposeCount ?? 0;

    public static SyntheticActorSourceFixture Create(bool invalidIdleArt = false, bool missingIdle = false, int idleFrames = ActorDiagnosticSource.IdleFrames,
        int walkFrames = ActorDiagnosticSource.WalkFrames, int idleFps = ActorDiagnosticSource.IdleFps, int walkFps = ActorDiagnosticSource.WalkFps,
        int moduleEntryCount = ActorDiagnosticSource.ModuleOrdinal + 1, int? resourceEntryCount = null, bool missingWalk = false,
        bool swapActionPayloads = false, int physicalGroupCount = ActorDiagnosticSource.PhysicalGroups, bool absentPalette = false,
        bool invalidPalette = false, int? substituteIdleResourceOrdinal = null, DatReaderOptions? resourceOptions = null)
    {
        var module = Dat(moduleEntryCount, _ => Array.Empty<byte>());
        var highestOrdinal = Math.Max(ActorDiagnosticSource.WalkResourceOrdinal, substituteIdleResourceOrdinal ?? 0);
        var defaultResourceEntryCount = substituteIdleResourceOrdinal is not null ? substituteIdleResourceOrdinal.Value + 1
            : missingIdle ? ActorDiagnosticSource.IdleResourceOrdinal : missingWalk ? ActorDiagnosticSource.WalkResourceOrdinal : highestOrdinal + 1;
        var resource = Dat(resourceEntryCount ?? defaultResourceEntryCount, ordinal =>
        {
            if (ordinal == ActorDiagnosticSource.IdleResourceOrdinal && !missingIdle && substituteIdleResourceOrdinal is null)
                return invalidIdleArt ? [1] : Art(swapActionPayloads ? walkFrames : idleFrames, swapActionPayloads ? walkFps : idleFps,
                    physicalGroupCount, absentPalette, invalidPalette);
            if (ordinal == ActorDiagnosticSource.WalkResourceOrdinal && !missingWalk)
                return Art(swapActionPayloads ? idleFrames : walkFrames, swapActionPayloads ? idleFps : walkFps,
                    physicalGroupCount, absentPalette, invalidPalette);
            if (ordinal == substituteIdleResourceOrdinal)
                return Art(idleFrames, idleFps, physicalGroupCount, absentPalette, invalidPalette);
            return Array.Empty<byte>();
        });
        return new SyntheticActorSourceFixture(module, resource, resourceOptions);
    }

    public ActorDiagnosticSourceOperation CreateOperation()
        => new(() =>
        {
            _openedResource = new TrackingMemoryStream(_resourceBytes);
            return DatArchive.Open(_openedResource, _resourceOptions);
        });

    public ActorDiagnosticPlacementInput Input(ActorDiagnosticAction action = ActorDiagnosticAction.Idle, int facing = 0, int frame = 0)
        => new(0, 0, 0, 0, 0, 0, ActorDiagnosticRenderBranch.NormalUncached, 100, false, action, facing, frame,
            1, 1, 0, 0, 0, 0);

    public void Dispose() => Module.Dispose();

    private static byte[] Art(int framesPerGroup, int fps, int physicalGroupCount, bool absentPalette, bool invalidPalette)
    {
        const int headerBytes = 132;
        const int paletteBytes = 1024;
        const int frameHeaderBytes = 28;
        if (physicalGroupCount is not 1 and not ActorDiagnosticSource.PhysicalGroups) throw new ArgumentOutOfRangeException(nameof(physicalGroupCount));
        if (invalidPalette) return new byte[headerBytes + 16];
        var total = physicalGroupCount * framesPerGroup;
        var table = headerBytes + (absentPalette ? 0 : paletteBytes);
        var payload = table + (total * frameHeaderBytes);
        var bytes = new byte[payload + total];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, physicalGroupCount == 1 ? 1u : 0u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), fps);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), absentPalette ? 0u : 1u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), framesPerGroup);
        if (!absentPalette)
        {
            bytes[headerBytes + 4] = 255;
            bytes[headerBytes + 5] = 255;
            bytes[headerBytes + 6] = 255;
            bytes[headerBytes + 7] = 255;
        }
        for (var index = 0; index < total; index++)
        {
            var frame = table + (index * frameHeaderBytes);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 8), 1);
            bytes[payload + index] = 1;
        }
        return bytes;
    }

    private static byte[] Dat(int entryCount, Func<int, byte[]> payloadFor)
    {
        var payloads = Enumerable.Range(0, entryCount).Select(payloadFor).ToArray();
        using var output = new MemoryStream();
        var offsets = new uint[entryCount];
        for (var index = 0; index < entryCount; index++)
        {
            offsets[index] = checked((uint)output.Position);
            output.Write(payloads[index]);
        }
        using var table = new MemoryStream();
        WriteU32(table, checked((uint)entryCount));
        for (var ordinal = 0; ordinal < entryCount; ordinal++)
        {
            var name = Encoding.ASCII.GetBytes($"f{ordinal}\0");
            WriteU32(table, checked((uint)name.Length));
            table.Write(name);
            table.Write(new byte[4]);
            WriteU32(table, 1);
            WriteU32(table, checked((uint)payloads[ordinal].Length));
            WriteU32(table, checked((uint)payloads[ordinal].Length));
            WriteU32(table, offsets[ordinal]);
        }
        table.Position = 0;
        table.CopyTo(output);
        output.Write(new byte[16]);
        output.Write("1TAD"u8);
        WriteU32(output, checked((uint)entryCount));
        WriteU32(output, checked((uint)(table.Length + 28)));
        return output.ToArray();
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private sealed class TrackingMemoryStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public int DisposeCount { get; private set; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeCount++;
            base.Dispose(disposing);
        }
    }
}
