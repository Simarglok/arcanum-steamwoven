using System.Buffers.Binary;

namespace Arcanum.Data;

// Admission is internal and per call; finite public DAT callers cannot supply a
// trusted projection. The stock inflater remains the sole payload decoder.
internal interface IDatEntryAdmission
{
    void Admit(long bytes);
}

/// <summary>RFC1950/1951 token traversal without an output/window allocation.
/// Projects cumulative SharpZipLib 1.4.2 table construction, not decoded bytes.
/// Fixed workspace is stack-only (< 4 KiB, including the 1.5 KiB short-code
/// lookup tables), reused across every block.</summary>
internal static class DatInflateProjection
{
    // Includes the 32768-byte window/header, Inflater/Adler/input/window objects,
    // probe, cold fixed trees/static arrays, and bounded exception wrappers.
    // The output array and each dynamic header/tree are charged separately.
    internal const long BaseEnvelope = 49152;
    private const int LiteralFastBits = 9, DistanceFastBits = 8;

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
        // Short-code lookups only accelerate the canonical decode; Symbol falls back
        // to it for longer or absent codes, so every decoded symbol is unchanged.
        Span<ushort> litFast = stackalloc ushort[1 << LiteralFastBits];
        Span<ushort> distFast = stackalloc ushort[1 << DistanceFastBits];
        Span<int> metaCounts = stackalloc int[16];
        ReadOnlySpan<byte> order = [16,17,18,0,8,7,9,6,10,5,11,4,12,3,13,2,14,1,15];
        long envelope = checked(BaseEnvelope + declaredSize + 32L);
        long produced = 0;
        // The lookups currently hold the fixed (type 1) code, so consecutive fixed
        // blocks do not rebuild them; a dynamic block overwrites them.
        var fixedTables = false;
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

            var reuse = type == 1 && fixedTables;
            var literals = new Codes(lengths[..literalCount], litCounts, litSymbols, litFast, LiteralFastBits, !reuse);
            var distances = new Codes(lengths.Slice(literalCount, distanceCount), distCounts, distSymbols, distFast, DistanceFastBits, !reuse);
            fixedTables = type == 1;
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
                    if (extra != 0) length += bits.Read(extra);
                    var distanceSymbol = Codes.Symbol(distances, ref bits);
                    if (distanceSymbol > 29) throw Invalid();
                    var distanceExtra = distanceSymbol < 4 ? 0 : distanceSymbol / 2 - 1;
                    var distance = distanceSymbol < 4 ? distanceSymbol + 1 : ((2 + (distanceSymbol & 1)) << distanceExtra) + 1;
                    if (distanceExtra != 0) distance += bits.Read(distanceExtra);
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

    private static DatFormatException Invalid() => new("Zlib structure cannot be safely projected.");

    private ref struct Bits(ReadOnlySpan<byte> input, CancellationToken token)
    {
        private readonly ReadOnlySpan<byte> _input = input;
        private readonly CancellationToken _token = token;
        private long _position;
        internal readonly long Available => (long)_input.Length * 8 - _position;
        internal readonly void ThrowIfCancellationRequested() => _token.ThrowIfCancellationRequested();
        // The next 24 bits, least significant first (deflate bit order), reading as
        // zero past the input end. Callers check Available before consuming any.
        internal readonly int Peek24()
        {
            var at = (int)(_position >> 3); uint value;
            if (at + 4 <= _input.Length) value = BinaryPrimitives.ReadUInt32LittleEndian(_input[at..]);
            else
            {
                value = 0;
                for (var i = 0; at + i < _input.Length; i++) value |= (uint)_input[at + i] << (8 * i);
            }
            return (int)((value >> (int)(_position & 7)) & 0xFFFFFF);
        }
        internal int Read(int count)
        {
            _token.ThrowIfCancellationRequested();
            if (count < 0 || count > 16 || count > Available) throw Invalid();
            var value = count == 0 ? 0 : Peek24() & ((1 << count) - 1);
            _position += count;
            return value;
        }
        internal void Consume(int count)
        {
            if (count < 0 || count > Available) throw Invalid();
            _position += count;
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
        private readonly Span<ushort> _fast;
        private readonly int _fastBits;
        internal long Allocation { get; }
        internal Codes(ReadOnlySpan<byte> lengths, Span<int> counts, Span<int> symbols, Span<ushort> fast = default, int fastBits = 0, bool buildFast = true)
        {
            if (fastBits is < 0 or > 9 || fast.Length < (fastBits == 0 ? 0 : 1 << fastBits)) throw new ArgumentOutOfRangeException(nameof(fastBits));
            _counts = counts; _symbols = symbols; _fast = fast; _fastBits = fastBits;
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
            if (fastBits == 0 || !buildFast) return;
            // Entry = symbol | length << 9 for every canonical code of length <=
            // fastBits, at all indexes whose low bits are that code bit-reversed
            // (deflate stores code bits most significant first). Zero = no entry.
            fast.Clear();
            int code = 0, index = 0;
            for (var length = 1; length <= fastBits; length++)
            {
                for (var j = 0; j < counts[length]; j++, code++)
                {
                    var reversed = 0;
                    for (var bit = 0; bit < length; bit++) reversed |= ((code >> bit) & 1) << (length - 1 - bit);
                    var entry = (ushort)(symbols[index + j] | length << 9);
                    for (var slot = reversed; slot < 1 << fastBits; slot += 1 << length) fast[slot] = entry;
                }
                index += counts[length]; code <<= 1;
            }
        }
        // Canonical decode one code bit at a time, as before, but from one peeked word:
        // a bit past the input end fails exactly where reading it one by one did. A
        // short code found in the lookup is the same symbol the canonical decode
        // returns, because codes are prefix-free.
        internal static int Symbol(scoped Codes tree, scoped ref Bits bits)
        {
            bits.ThrowIfCancellationRequested();
            var peek = bits.Peek24(); var available = bits.Available;
            if (tree._fastBits != 0 && tree._fast[peek & ((1 << tree._fastBits) - 1)] is var hit && hit != 0)
            {
                var hitLength = hit >> 9;
                if (hitLength > available) throw Invalid();
                bits.Consume(hitLength);
                return hit & 511;
            }
            var code = 0; var first = 0; var index = 0;
            for (var length = 1; length <= 15; length++)
            {
                if (length > available) throw Invalid();
                code = (code << 1) | ((peek >> (length - 1)) & 1);
                var count = tree._counts[length];
                if (code >= first && code - first < count) { bits.Consume(length); return tree._symbols[index + code - first]; }
                index += count; first = (first + count) << 1;
            }
            throw Invalid();
        }
    }
}
