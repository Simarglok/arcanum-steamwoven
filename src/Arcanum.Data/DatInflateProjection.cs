namespace Arcanum.Data;

// Admission is internal and per call; finite public DAT callers cannot supply a
// trusted projection. The stock inflater remains the sole payload decoder.
internal interface IDatEntryAdmission
{
    void Admit(long bytes);
}

/// <summary>RFC1950/1951 token traversal without an output/window allocation.
/// Projects cumulative SharpZipLib 1.4.2 table construction, not decoded bytes.
/// Fixed workspace is stack-only (< 4 KiB), reused across every block.</summary>
internal static class DatInflateProjection
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

    private static DatFormatException Invalid() => new("Zlib structure cannot be safely projected.");

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
