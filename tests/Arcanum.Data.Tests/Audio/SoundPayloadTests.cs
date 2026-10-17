using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Audio;

/// <summary>ARC-DATA-041: payload admission on literal synthetic WAV and MP3 bytes (no original data).</summary>
[TestClass]
public sealed class SoundPayloadTests
{
    private static byte[] Chunk(string id, byte[] body)
    {
        var chunk = new byte[8 + body.Length + (body.Length & 1)];
        Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)body.Length);
        body.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] Fmt(ushort tag, ushort channels, uint rate, ushort bits, ushort? blockAlign = null)
    {
        var body = new byte[16];
        var align = blockAlign ?? (ushort)(channels * bits / 8);
        BinaryPrimitives.WriteUInt16LittleEndian(body, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), channels);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), rate);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), rate * align);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(12), align);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(14), bits);
        return Chunk("fmt ", body);
    }

    private static byte[] Smpl(params (uint Start, uint End, uint Count)[] loops)
    {
        var body = new byte[36 + loops.Length * 24];
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(28), (uint)loops.Length);
        for (var i = 0; i < loops.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(36 + i * 24 + 8), loops[i].Start);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(36 + i * 24 + 12), loops[i].End);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(36 + i * 24 + 20), loops[i].Count);
        }
        return Chunk("smpl", body);
    }

    private static byte[] Riff(params byte[][] chunks)
    {
        var content = chunks.SelectMany(c => c).ToArray();
        var file = new byte[12 + content.Length];
        "RIFF"u8.CopyTo(file); BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)(4 + content.Length)); "WAVE"u8.CopyTo(file.AsSpan(8));
        content.CopyTo(file, 12);
        return file;
    }

    /// <summary>MPEG-1 Layer III, 128 kbit/s, 44.1 kHz, mono, no padding: 144 × 128000 / 44100 = 417 bytes per frame.</summary>
    private static byte[] Frame(byte second = 0xFB, byte third = 0x90)
    {
        var frame = new byte[417];
        frame[0] = 0xFF; frame[1] = second; frame[2] = third; frame[3] = 0xC0;
        return frame;
    }

    private static SoundPayload Inspect(byte[] bytes) => SoundPayloadReader.Inspect("sound/x", bytes);

    [TestMethod]
    public void Pcm_wav_is_admitted_with_its_geometry_and_loops()
    {
        var payload = Inspect(Riff(Fmt(1, 1, 11025, 16), Chunk("LIST", [1, 2, 3]), Chunk("data", new byte[8]), Smpl((1, 3, 0))));
        Assert.AreEqual((SoundPayloadStatus.Admitted, SoundPayloadKind.Wav), (payload.Status, payload.Kind));
        var wav = payload.Wav!;
        Assert.AreEqual((1, 11025, 16, 2, 4, 8), (wav.Channels, wav.SampleRate, wav.BitsPerSample, wav.BlockAlign, wav.FrameCount, wav.DataLength));
        Assert.AreEqual(12 + 24 + 12 + 8, wav.DataOffset, "after the fmt chunk, the padded odd LIST chunk and the data header");
        Assert.AreEqual(new WavLoop(0, 1, 3, 0), wav.Loops.Single());
        Assert.AreEqual(0, wav.IgnoredTailBytes);

        Assert.AreEqual(SoundPayloadStatus.Admitted, Inspect(Riff(Fmt(1, 2, 44100, 16), Chunk("data", new byte[8]))).Status);
        Assert.AreEqual(SoundPayloadStatus.Admitted, Inspect(Riff(Fmt(1, 1, 8000, 8), Chunk("data", new byte[3]))).Status);
        Assert.AreEqual(3, Inspect(Riff(Fmt(1, 1, 22050, 16), Chunk("data", new byte[7]))).Wav!.FrameCount, "a partial frame is not counted");
    }

    [TestMethod]
    public void Wav_classes_outside_the_admitted_set_are_unsupported()
    {
        Assert.AreEqual(SoundPayloadStatus.Unsupported, Inspect(Riff(Fmt(2, 1, 22050, 4, 256), Chunk("data", new byte[8]))).Status, "ADPCM");
        Assert.AreEqual(SoundPayloadStatus.Unsupported, Inspect(Riff(Fmt(1, 1, 22050, 24), Chunk("data", new byte[6]))).Status);
        Assert.AreEqual(SoundPayloadStatus.Unsupported, Inspect(Riff(Fmt(1, 1, 12345, 16), Chunk("data", new byte[2]))).Status);
        Assert.AreEqual(SoundPayloadStatus.Unsupported, Inspect(Riff(Fmt(1, 6, 22050, 16), Chunk("data", new byte[12]))).Status);
        var unsupported = Inspect(Riff(Fmt(3, 1, 22050, 32), Chunk("data", new byte[4])));
        Assert.IsFalse(unsupported.Bytes.IsEmpty, "an unsupported payload is still handed back for reporting");
    }

    [TestMethod]
    public void Damaged_wav_structure_is_malformed()
    {
        Assert.AreEqual(SoundPayloadStatus.Malformed, Inspect(Riff(Fmt(1, 1, 22050, 16))).Status, "no data");
        Assert.AreEqual(SoundPayloadStatus.Malformed, Inspect(Riff(Chunk("data", new byte[2]))).Status, "no fmt");
        Assert.AreEqual(SoundPayloadStatus.Malformed, Inspect(Riff(Fmt(1, 1, 22050, 16), Fmt(1, 1, 22050, 16), Chunk("data", new byte[2]))).Status);
        Assert.AreEqual(SoundPayloadStatus.Malformed, Inspect(Riff(Fmt(1, 1, 22050, 16, blockAlign: 4), Chunk("data", new byte[4]))).Status);
        Assert.AreEqual(SoundPayloadStatus.Malformed, Inspect(Riff(Fmt(1, 1, 22050, 16), Chunk("data", new byte[8]), Smpl((2, 4, 0)))).Status, "loop end past the samples");
        Assert.AreEqual(SoundPayloadStatus.Malformed, Inspect(Riff(Fmt(1, 1, 22050, 16), Chunk("data", new byte[8]), Smpl(Enumerable.Repeat((0u, 1u, 0u), 17).ToArray()))).Status);

        var truncated = Riff(Fmt(1, 1, 22050, 16), Chunk("data", new byte[8]));
        BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(12 + 24 + 4), 100);
        Assert.AreEqual(SoundPayloadStatus.Malformed, Inspect(truncated).Status, "data runs past the end");
    }

    [TestMethod]
    public void A_damaged_chunk_after_fmt_and_data_is_ignored_and_reported()
    {
        var bytes = Riff(Fmt(1, 1, 22050, 16), Chunk("data", new byte[8])).Concat(new byte[] { 0x10, 0x20, 0x30, 0x40, 0xFF, 0xFF, 0xFF, 0xF0, 1, 2 }).ToArray();
        var payload = Inspect(bytes);
        Assert.AreEqual((SoundPayloadStatus.Admitted, 10), (payload.Status, payload.Wav!.IgnoredTailBytes));
        Assert.AreEqual(SoundPayloadStatus.Admitted, Inspect(Riff(Fmt(1, 1, 22050, 16), Chunk("data", new byte[8])).Concat(new byte[3]).ToArray()).Status, "zero padding");
    }

    [TestMethod]
    public void Mpeg_layer_three_needs_two_consecutive_frames()
    {
        var payload = Inspect([.. Frame(), .. Frame()]);
        Assert.AreEqual((SoundPayloadStatus.Admitted, SoundPayloadKind.Mp3), (payload.Status, payload.Kind));
        Assert.AreEqual(new Mp3Format(1, 44100, false, 0, 128), payload.Mp3);

        Assert.AreEqual(SoundPayloadStatus.Malformed, Inspect(Frame()).Status, "one frame only");
        Assert.AreEqual(SoundPayloadStatus.Unsupported, Inspect([.. Frame(0xFD, 0x80), .. Frame(0xFD, 0x80)]).Status, "layer II at 128 kbit/s (index 8)");
        Assert.AreEqual(SoundPayloadStatus.Malformed, Inspect([.. Frame(third: 0x00), .. Frame(third: 0x00)]).Status, "free-format bit rate");
        Assert.AreEqual(SoundPayloadStatus.UnknownContainer, Inspect(Encoding.ASCII.GetBytes("not audio at all")).Status);
    }

    [TestMethod]
    public void An_id3v2_tag_is_skipped()
    {
        byte[] tag = [(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 1, 0]; // synchsafe size 128
        var payload = Inspect([.. tag, .. new byte[128], .. Frame(), .. Frame()]);
        Assert.AreEqual((SoundPayloadStatus.Admitted, 138), (payload.Status, payload.Mp3!.FirstFrameOffset));
        byte[] damaged = [(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0x80, 0, 0, 0];
        Assert.AreEqual(SoundPayloadStatus.Malformed, Inspect([.. damaged, .. Frame(), .. Frame()]).Status);
        byte[] longTag = [(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0x7F, 0];
        Assert.AreEqual(SoundPayloadStatus.Malformed, Inspect([.. longTag, .. Frame()]).Status, "tag runs past the end");
    }

    [TestMethod]
    public void The_payload_limit_is_checked_first()
    {
        var payload = SoundPayloadReader.Inspect("sound/x", new byte[100], new SoundPayloadOptions { MaxBytes = 99 });
        Assert.AreEqual((SoundPayloadStatus.TooLarge, true), (payload.Status, payload.Bytes.IsEmpty));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SoundPayloadReader.Inspect("sound/x", new byte[1], new SoundPayloadOptions { MaxBytes = 0 }));
    }
}
