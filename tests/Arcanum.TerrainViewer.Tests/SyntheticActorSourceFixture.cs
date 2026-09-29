using System.Buffers.Binary;
using System.Text;
using Arcanum.Data;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// Public synthetic 1TAD/ART producer for exercising the production actor
/// source boundary. It deliberately creates real archives rather than a parser
/// double: the module has ordinal 643 and the resource has ordinals 4753/4754.
/// </summary>
internal sealed class SyntheticActorSourceFixture : IDisposable
{
    private readonly byte[] _resourceBytes;
    private readonly bool _invalidIdleArt;
    private readonly bool _missingIdle;
    private TrackingMemoryStream? _openedResource;

    private SyntheticActorSourceFixture(byte[] moduleBytes, byte[] resourceBytes, bool invalidIdleArt, bool missingIdle)
    {
        Module = DatArchive.Open(new MemoryStream(moduleBytes));
        _resourceBytes = resourceBytes;
        _invalidIdleArt = invalidIdleArt;
        _missingIdle = missingIdle;
    }

    public DatArchive Module { get; }
    public int ResourceDisposeCount => _openedResource?.DisposeCount ?? 0;

    public static SyntheticActorSourceFixture Create(bool invalidIdleArt = false, bool missingIdle = false, int idleFrames = ActorDiagnosticSource.IdleFrames,
        int walkFrames = ActorDiagnosticSource.WalkFrames, int idleFps = ActorDiagnosticSource.IdleFps, int walkFps = ActorDiagnosticSource.WalkFps)
    {
        var module = Dat(ActorDiagnosticSource.ModuleOrdinal + 1, _ => Array.Empty<byte>());
        var resource = Dat(missingIdle ? ActorDiagnosticSource.IdleResourceOrdinal : ActorDiagnosticSource.WalkResourceOrdinal + 1, ordinal => ordinal switch
        {
            ActorDiagnosticSource.IdleResourceOrdinal when invalidIdleArt => [1],
            ActorDiagnosticSource.IdleResourceOrdinal => Art(idleFrames, idleFps),
            ActorDiagnosticSource.WalkResourceOrdinal => Art(walkFrames, walkFps),
            _ => Array.Empty<byte>()
        });
        return new SyntheticActorSourceFixture(module, resource, invalidIdleArt, missingIdle);
    }

    public ActorDiagnosticSourceOperation CreateOperation()
        => new(() =>
        {
            _openedResource = new TrackingMemoryStream(_resourceBytes);
            return DatArchive.Open(_openedResource);
        });

    public ActorDiagnosticPlacementInput Input(ActorDiagnosticAction action = ActorDiagnosticAction.Idle, int facing = 0, int frame = 0)
        => new(0, 0, 0, 0, 0, 0, ActorDiagnosticRenderBranch.NormalUncached, 100, false, action, facing, frame,
            1, 1, 0, 0, 0, 0);

    public void Dispose() => Module.Dispose();

    private static byte[] Art(int framesPerGroup, int fps)
    {
        const int headerBytes = 132;
        const int paletteBytes = 1024;
        const int frameHeaderBytes = 28;
        var total = ActorDiagnosticSource.PhysicalGroups * framesPerGroup;
        var table = headerBytes + paletteBytes;
        var payload = table + (total * frameHeaderBytes);
        var bytes = new byte[payload + total];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), fps);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), framesPerGroup);
        bytes[headerBytes + 4] = 255;
        bytes[headerBytes + 5] = 255;
        bytes[headerBytes + 6] = 255;
        bytes[headerBytes + 7] = 255;
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
