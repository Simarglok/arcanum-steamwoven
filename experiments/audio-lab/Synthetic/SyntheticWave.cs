using System.Buffers.Binary;

namespace AudioLab.Synthetic;

// Own deterministic square-wave recipe, not original audio. PCM16 mono 16kHz,
// exactly 4000 frames (250ms), alternating +4096/-4096 every 20 samples (400Hz).
public static class SyntheticWave
{
    public static byte[] Create()
    {
        var bytes = new byte[8044];
        "RIFF"u8.CopyTo(bytes); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 8036);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 16000);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), 32000);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), 8000);
        for (int i = 0; i < 4000; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44 + i * 2), (short)(i % 40 < 20 ? 4096 : -4096));
        return bytes;
    }
}
