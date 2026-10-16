using System.IO.Compression;
using Arcanum.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests;

[TestClass]
public sealed class DatInflateProjectionEquivalenceTests
{
    // The production projection must decode exactly the symbols the original
    // bit-at-a-time canonical decoder (kept verbatim below as the reference)
    // decodes: same envelope, or the same structural failure, for every input.
    [TestMethod]
    public void Fast_projection_matches_the_bit_serial_reference_on_valid_truncated_and_corrupted_streams()
    {
        var random = new Random(20261016); var compared = 0; var failures = 0;
        foreach (var level in new[] { CompressionLevel.NoCompression, CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.SmallestSize })
        for (var sample = 0; sample < 60; sample++)
        {
            var size = sample switch { 0 => 0, 1 => 1, _ => random.Next(1, 200_000) };
            var data = new byte[size];
            var alphabet = random.Next(1, 257);
            var run = random.Next(1, 64);
            for (var i = 0; i < size; i++) data[i] = i % run == 0 || random.Next(4) == 0 ? (byte)random.Next(alphabet) : data[Math.Max(0, i - 1)];
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, level, leaveOpen: true)) zlib.Write(data);
            var stored = output.ToArray();
            Compare(stored, size); Compare(stored, size + 1); Compare(stored, Math.Max(0, size - 1)); compared += 3;
            for (var variant = 0; variant < 6; variant++)
            {
                var broken = (byte[])stored.Clone();
                if (variant < 3 && broken.Length > 3) broken[random.Next(2, broken.Length)] ^= (byte)(1 << random.Next(8));
                else if (broken.Length > 3) Array.Resize(ref broken, random.Next(2, broken.Length));
                var declared = variant == 5 ? size + 1 : size;
                if (!Compare(broken, declared)) failures++; compared++;
            }
        }
        Assert.IsTrue(compared > 1000 && failures > 100, $"compared={compared} failures={failures}");
    }

    // Crafted fixed-Huffman blocks reach the codes random zlib output does not: literal/
    // length 286/287 and distance 30/31 are invalid, distance 29 needs 24,577 produced
    // bytes. A preceding stored block of 32,768 zeros makes each reachable. Repeated empty
    // fixed blocks exercise the reused fixed lookup tables.
    [TestMethod]
    public void Crafted_fixed_blocks_with_edge_codes_match_the_reference()
    {
        static byte[] Stream(Action<BitWriter> fixedBody, int emptyFixedBlocks = 0)
        {
            var writer = new BitWriter(); writer.Bytes(0x78, 0x01);
            writer.Bits(0, 1); writer.Bits(0, 2); writer.Align();
            writer.Bytes(0x00, 0x80, 0xFF, 0x7F); writer.Bytes(new byte[32768]);
            for (var i = 0; i < emptyFixedBlocks; i++) { writer.Bits(0, 1); writer.Bits(1, 2); writer.Code(0, 7); }
            writer.Bits(1, 1); writer.Bits(1, 2); fixedBody(writer); writer.Code(0, 7);
            writer.Align(); writer.Bytes(0, 0, 0, 0);
            return writer.ToArray();
        }
        // Fixed literal/length codes: 256-279 are 7 bits from 0, 280-287 are 8 bits from 0xC0.
        static void Length(BitWriter w, int symbol) { if (symbol < 280) w.Code(symbol - 256, 7); else w.Code(0xC0 + symbol - 280, 8); }
        var cases = new (string Name, byte[] Stored, int Declared, bool Valid)[]
        {
            ("distance 29", Stream(w => { Length(w, 257); w.Code(29, 5); w.Bits(0, 13); }), 32771, true),
            ("distance 29, repeated empty fixed blocks", Stream(w => { Length(w, 257); w.Code(29, 5); w.Bits(0, 13); }, 40), 32771, true),
            ("distance 30", Stream(w => { Length(w, 257); w.Code(30, 5); w.Bits(0, 13); }), 32771, false),
            ("distance 31", Stream(w => { Length(w, 257); w.Code(31, 5); }), 32771, false),
            ("length 285", Stream(w => { Length(w, 285); w.Code(0, 5); }), 33026, true),
            ("length 286", Stream(w => { Length(w, 286); w.Code(0, 5); }), 33026, false),
            ("length 287", Stream(w => { Length(w, 287); w.Code(0, 5); }), 33026, false),
        };
        foreach (var (name, stored, declared, valid) in cases)
            Assert.AreEqual(valid, Compare(stored, declared), name);
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = []; private int _bit;
        public void Bits(int value, int count) { for (var i = 0; i < count; i++) Bit((value >> i) & 1); }
        // Huffman codes are stored most significant bit first.
        public void Code(int code, int length) { for (var i = length - 1; i >= 0; i--) Bit((code >> i) & 1); }
        public void Align() => _bit = 0;
        public void Bytes(params byte[] values) { Align(); _bytes.AddRange(values); }
        public byte[] ToArray() => _bytes.ToArray();
        private void Bit(int value)
        {
            if (_bit == 0) _bytes.Add(0);
            _bytes[^1] |= (byte)(value << _bit); _bit = (_bit + 1) & 7;
        }
    }

    private static bool Compare(byte[] stored, int declared)
    {
        long? expected = null, actual = null; Exception? expectedError = null, actualError = null;
        try { expected = ReferenceInflateProjection.Measure(stored, declared, default); } catch (Exception error) { expectedError = error; }
        try { actual = DatInflateProjection.Measure(stored, declared, default); } catch (Exception error) { actualError = error; }
        Assert.AreEqual(expected, actual);
        Assert.AreEqual(expectedError?.GetType(), actualError?.GetType());
        Assert.AreEqual(expectedError?.Message, actualError?.Message);
        return expectedError is null;
    }
}

// Verbatim pre-optimization projection (git 7970f19), test-only reference.
internal static class ReferenceInflateProjection
{
    // Includes the 32768-byte window/header, Inflater/Adler/input/window objects,
    // probe, cold fixed trees/static arrays, and bounded exception wrappers.
    // The output array and each dynamic header/tree are charged separately.
    internal const long BaseEnvelope = 49152;

    internal static long Measure(ReadOnlySpan<byte> stored, int declaredSize, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (stored.Length < 2 || ((stored[0] << 8) | stored[1]) % 31 != 0 || (stored[0] & 15) != 8)
            throw Invalid();
        if ((stored[1] & 32) != 0) throw Invalid(); // no preset dictionaries
        var bits = new Bits(stored[2..], token);
        Span<byte> lengths = stackalloc byte[320];
        Span<byte> meta = stackalloc byte[19];
        Span<int> litSymbols = stackalloc int[288];
        Span<int> distSymbols = stackalloc int[32];
        Span<int> metaSymbols = stackalloc int[19];
        Span<int> litCounts = stackalloc int[16];
        Span<int> distCounts = stackalloc int[16];
        Span<int> metaCounts = stackalloc int[16];
        ReadOnlySpan<byte> order = [16,17,18,0,8,7,9,6,10,5,11,4,12,3,13,2,14,1,15];
        long envelope = checked(BaseEnvelope + declaredSize + 32L);
        long produced = 0;
        bool final;
        do
        {
            final = bits.Read(1) != 0;
            var type = bits.Read(2);
            if (type == 0)
            {
                bits.Align();
                var count = bits.Read(16);
                if ((count ^ bits.Read(16)) != 65535) throw Invalid();
                bits.SkipBytes(count);
                produced = checked(produced + count);
                if (produced > declaredSize) throw Invalid();
                continue;
            }
            int literalCount, distanceCount;
            if (type == 1)
            {
                literalCount = 288; distanceCount = 32;
                lengths[..144].Fill(8); lengths[144..256].Fill(9);
                lengths[256..280].Fill(7); lengths[280..288].Fill(8);
                lengths[288..320].Fill(5);
            }
            else if (type == 2)
            {
                literalCount = bits.Read(5) + 257; distanceCount = bits.Read(5) + 1;
                var metaCount = bits.Read(4) + 4;
                if (literalCount > 286 || distanceCount > 30) throw Invalid();
                meta.Clear();
                for (var i = 0; i < metaCount; i++) meta[order[i]] = (byte)bits.Read(3);
                var metaTree = new Codes(meta, metaCounts, metaSymbols);
                envelope = checked(envelope + 640 + metaTree.Allocation);
                var index = 0;
                var total = literalCount + distanceCount;
                while (index < total)
                {
                    var symbol = Codes.Symbol(metaTree, ref bits);
                    if (symbol < 16) { lengths[index++] = (byte)symbol; continue; }
                    int repeat;
                    byte value;
                    if (symbol == 16)
                    {
                        if (index == 0) throw Invalid();
                        value = lengths[index - 1]; repeat = bits.Read(2) + 3;
                    }
                    else if (symbol == 17) { value = 0; repeat = bits.Read(3) + 3; }
                    else if (symbol == 18) { value = 0; repeat = bits.Read(7) + 11; }
                    else throw Invalid();
                    if (repeat > total - index) throw Invalid();
                    lengths.Slice(index, repeat).Fill(value); index += repeat;
                }
                if (lengths[256] == 0) throw Invalid();
            }
            else throw Invalid();

            var literals = new Codes(lengths[..literalCount], litCounts, litSymbols);
            var distances = new Codes(lengths.Slice(literalCount, distanceCount), distCounts, distSymbols);
            if (type == 2) envelope = checked(envelope + literals.Allocation + distances.Allocation);
            while (true)
            {
                var symbol = Codes.Symbol(literals, ref bits);
                if (symbol == 256) break;
                if (symbol < 256) produced++;
                else
                {
                    if (symbol > 285) throw Invalid();
                    var n = symbol - 257;
                    var extra = n < 8 || n == 28 ? 0 : (n - 4) / 4;
                    var length = n < 8 ? n + 3 : n == 28 ? 258 : ((4 + ((n - 8) % 4)) << extra) + 3;
                    length += bits.Read(extra);
                    var distanceSymbol = Codes.Symbol(distances, ref bits);
                    if (distanceSymbol > 29) throw Invalid();
                    var distanceExtra = distanceSymbol < 4 ? 0 : distanceSymbol / 2 - 1;
                    var distance = distanceSymbol < 4 ? distanceSymbol + 1 : ((2 + (distanceSymbol & 1)) << distanceExtra) + 1;
                    distance += bits.Read(distanceExtra);
                    if (distance > produced || distance > 32768) throw Invalid();
                    produced = checked(produced + length);
                }
                if (produced > declaredSize) throw Invalid();
            }
        } while (!final);
        bits.Align();
        // Trailer validity/Adler/trailing bytes remain the stock inflater's job.
        // A truncated trailer never reaches guarded inflate through this seam.
        bits.SkipBytes(4);
        if (produced != declaredSize) throw Invalid();
        return envelope;
    }

    private static Arcanum.Data.DatFormatException Invalid() => new("Zlib structure cannot be safely projected.");

    private ref struct Bits(ReadOnlySpan<byte> input, CancellationToken token)
    {
        private readonly ReadOnlySpan<byte> _input = input;
        private readonly CancellationToken _token = token;
        private long _position;
        internal int Read(int count)
        {
            _token.ThrowIfCancellationRequested();
            if (count < 0 || count > 16 || _position + count > (long)_input.Length * 8) throw Invalid();
            var value = 0;
            for (var i = 0; i < count; i++, _position++)
                value |= ((_input[(int)(_position >> 3)] >> (int)(_position & 7)) & 1) << i;
            return value;
        }
        internal void Align() => _position = (_position + 7) & ~7L;
        internal void SkipBytes(int count)
        {
            _token.ThrowIfCancellationRequested();
            var next = checked(_position + count * 8L);
            if (count < 0 || next > (long)_input.Length * 8) throw Invalid();
            _position = next;
        }
    }

    private readonly ref struct Codes
    {
        private readonly Span<int> _counts;
        private readonly Span<int> _symbols;
        internal long Allocation { get; }
        internal Codes(ReadOnlySpan<byte> lengths, Span<int> counts, Span<int> symbols)
        {
            _counts = counts; _symbols = symbols;
            counts.Clear();
            foreach (var length in lengths)
            {
                if (length > 15) throw Invalid();
                counts[length]++;
            }
            Span<int> offsets = stackalloc int[16];
            var available = 1;
            var first = 0;
            long tableSlots = 512;
            for (var length = 1; length <= 15; length++)
            {
                available = available * 2 - counts[length];
                if (available < 0) throw Invalid();
                first = (first + (length == 1 ? 0 : counts[length - 1])) * 2;
                if (length > 9 && counts[length] != 0)
                {
                    // Cover every intersected nine-bit prefix bucket at this
                    // depth, including partially occupied terminal buckets.
                    // This bounds the stock tree's extra-table construction
                    // without reproducing its signed/reversed lookup layout.
                    var bucket = 1 << (length - 9);
                    var buckets = (first + counts[length] + bucket - 1) / bucket - first / bucket;
                    tableSlots = checked(tableSlots + (long)buckets * bucket);
                }
                offsets[length] = length == 1 ? 0 : offsets[length - 1] + counts[length - 1];
            }
            for (var symbol = 0; symbol < lengths.Length; symbol++)
                if (lengths[symbol] != 0) symbols[offsets[lengths[symbol]]++] = symbol;
            // Two int[16] arrays (176), tree object (24), short[] header +
            // alignment (32); 24 extra bytes conservatively cover wrappers.
            Allocation = checked(256 + tableSlots * 2);
        }
        internal static int Symbol(scoped Codes tree, scoped ref Bits bits)
        {
            var code = 0; var first = 0; var index = 0;
            for (var length = 1; length <= 15; length++)
            {
                code = (code << 1) | bits.Read(1);
                var count = tree._counts[length];
                if (code >= first && code - first < count) return tree._symbols[index + code - first];
                index += count; first = (first + count) << 1;
            }
            throw Invalid();
        }
    }
}
