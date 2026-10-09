using System.Buffers.Binary;

namespace AudioLab.Core;

// Admission metadata only, not a PCM decoder. The bounded lab accepts canonical
// 44-byte PCM16 WAV headers; broader/ancillary/compressed WAV is deferred.
public static class PcmWaveProfile
{
    public static bool Accept(ReadOnlySpan<byte> b)
    {
        if (b.Length < 46 || !b[..4].SequenceEqual("RIFF"u8) || !b.Slice(8, 8).SequenceEqual("WAVEfmt "u8)
            || !b.Slice(36, 4).SequenceEqual("data"u8)) return false;
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(b[22..]);
        int rate = BinaryPrimitives.ReadInt32LittleEndian(b[24..]);
        return BinaryPrimitives.ReadUInt32LittleEndian(b[4..]) == b.Length - 8
            && BinaryPrimitives.ReadInt32LittleEndian(b[16..]) == 16
            && BinaryPrimitives.ReadInt16LittleEndian(b[20..]) == 1
            && channels is 1 or 2 && rate is >= 8000 and <= 48000
            && BinaryPrimitives.ReadInt32LittleEndian(b[28..]) == rate * channels * 2
            && BinaryPrimitives.ReadInt16LittleEndian(b[32..]) == channels * 2
            && BinaryPrimitives.ReadInt16LittleEndian(b[34..]) == 16
            && BinaryPrimitives.ReadUInt32LittleEndian(b[40..]) == b.Length - 44
            && (b.Length - 44) % (channels * 2) == 0;
    }
}
