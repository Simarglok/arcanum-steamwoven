using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Security.Cryptography;
using Arcanum.Data.StaticObjects;

namespace Arcanum.Data.Objects;

// Independently authored from the documented v119 FILE layout. The existing
// finite loader supplies the shared type descriptors, not creation defaults.
internal static class ObjectWireReader
{
    internal static ObjectWireRecord Read(ReadOnlySpan<byte> bytes, StaticObjectLoaderOptions limits, bool prototype = false, Action<long>? reserve = null, int maxRecordBytes = int.MaxValue)
    {
        // Fixed schema/map/hash envelope; variable sparse graphs reserved below
        // before constructing their arrays. Finite callers retain scalar limits.
        reserve?.Invoke(16384);
        var cursor = new Reader(bytes, maxRecordBytes);
        if (cursor.Int32() != 119) throw Error(StaticObjectErrorCode.UnsupportedVersion);
        var parent = cursor.Id(prototype);
        var own = cursor.Id(false);
        if (prototype && (parent.Kind != -1 || own.Kind != 1)) throw Error(StaticObjectErrorCode.InvalidRecord);
        var type = cursor.Int32();
        if (type is < 0 or > 17) throw Error(StaticObjectErrorCode.UnsupportedType);
        var kinds = StaticObjectLoader.FieldsFor(type);
        var count = prototype ? -1 : cursor.Int16();
        if (!prototype && (count < 0 || count > limits.MaxFields)) throw Error(StaticObjectErrorCode.LimitExceeded);
        var selected = new bool[kinds.Length];
        var population = 0;
        for (var word = 0; word < StaticObjectLoader.MaskWords(type); word++)
        {
            var length = StaticObjectLoader.MaskLength(type, word);
            var start = StaticObjectLoader.MaskStart(type, word);
            var mask = cursor.UInt32();
            var allowed = length == 32 ? uint.MaxValue : (1U << length) - 1;
            if ((mask & ~allowed) != 0) throw Error(StaticObjectErrorCode.InvalidRecord);
            for (var bit = 0; bit < length; bit++)
                if ((mask & (1U << bit)) != 0) { selected[start + bit] = true; population++; }
        }
        if (!prototype && count != population) throw Error(StaticObjectErrorCode.InvalidRecord);
        // Per-field sorted node/value and optional empty-array wrappers are not
        // bounded by a per-record constant. Sparse element storage is separate.
        reserve?.Invoke(checked((prototype ? kinds.Length : population) * 192L));
        var fields = new SortedDictionary<int, ObjectWireValue>();
        using var semantic = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        semantic.AppendData(bytes[..cursor.Position]);
        Span<byte> zero = stackalloc byte[4]; zero.Clear();
        Span<byte> normalized = stackalloc byte[4];
        for (var slot = 0; slot < kinds.Length; slot++)
        {
            if (!prototype && !selected[slot]) continue;
            var start = cursor.Position;
            var kind = kinds[slot];
            ObjectWireValue value;
            if (kind == StaticObjectLoader.WireKind.I32)
                value = new(ObjectWirePresence.Value, cursor.Int32(), []);
            else if (!cursor.Present())
                value = new(ObjectWirePresence.Null, null, []);
            else if (kind == StaticObjectLoader.WireKind.OptI64)
                value = new(ObjectWirePresence.Value, cursor.Int64(), []);
            else if (kind == StaticObjectLoader.WireKind.OptObjectId)
            {
                reserve?.Invoke(768); // ID object plus digest/two hexadecimal strings
                var id = cursor.Id(false);
                value = new(ObjectWirePresence.Value, id.Number, [], id.Sha256);
            }
            else if (kind == StaticObjectLoader.WireKind.OptString)
            {
                var length = cursor.Int32();
                if (length < 0 || length > limits.MaxStringBytes) throw Error(StaticObjectErrorCode.LimitExceeded);
                var text = cursor.Take(checked(length + 1));
                if (text[^1] != 0) throw Error(StaticObjectErrorCode.InvalidRecord);
                reserve?.Invoke(checked(768L + length)); // the fingerprint and (ARC-WORLD-101) the bytes before the terminating zero
                value = new(ObjectWirePresence.Value, length, [], Hash(text)) { Text = text[..^1].ToArray() };
            }
            else
            {
                var width = cursor.Int32(); var elements = cursor.Int32(); _ = cursor.Int32();
                var expected = kind switch
                {
                    StaticObjectLoader.WireKind.ArrI32 or StaticObjectLoader.WireKind.ArrU32 => 4,
                    StaticObjectLoader.WireKind.ArrI64 or StaticObjectLoader.WireKind.ArrU64 => 8,
                    StaticObjectLoader.WireKind.ArrScript => 12,
                    StaticObjectLoader.WireKind.ArrQuest => 16,
                    StaticObjectLoader.WireKind.ArrObjectId => 24,
                    _ => throw Error(StaticObjectErrorCode.InvalidRecord)
                };
                if (width != expected || elements < 0) throw Error(StaticObjectErrorCode.InvalidRecord);
                if (elements > limits.MaxArrayElements) throw Error(StaticObjectErrorCode.LimitExceeded);
                var values = cursor.Take(checked(width * elements));
                var words = cursor.Int32();
                // FILE capacity is positive, not the runtime allocation minimum of two.
                if (words < 1) throw Error(StaticObjectErrorCode.InvalidRecord);
                if (words > limits.MaxArrayElements) throw Error(StaticObjectErrorCode.LimitExceeded);
                var bitset = cursor.Take(checked(words * 4));
                var bits = 0;
                for (var w = 0; w < words; w++) bits = checked(bits + BitOperations.PopCount(BinaryPrimitives.ReadUInt32LittleEndian(bitset[(w * 4)..])));
                if (bits != elements) throw Error(StaticObjectErrorCode.InvalidRecord);
                reserve?.Invoke(checked((long)elements * (128 + width * 4L)));
                var entries = new ObjectWireElement[elements]; var rank = 0;
                for (var w = 0; w < words; w++)
                {
                    var mask = BinaryPrimitives.ReadUInt32LittleEndian(bitset[(w * 4)..]);
                    while (mask != 0)
                    {
                        var bit = BitOperations.TrailingZeroCount(mask); var raw = values.Slice(rank * width, width);
                        var numeric = new long[width / 4];
                        for (var n = 0; n < numeric.Length; n++) numeric[n] = BinaryPrimitives.ReadUInt32LittleEndian(raw[(n * 4)..]);
                        // Complete array bytes are authenticated by both record
                        // hashes. Don't construct a discarded ID/hash per element;
                        // retain all six copied words as the numeric evidence.
                        if (kind == StaticObjectLoader.WireKind.ArrObjectId) _ = IdKind(raw, false);
                        entries[rank++] = new(checked(w * 32 + bit), Array.AsReadOnly(numeric));
                        mask &= mask - 1;
                    }
                }
                value = new(elements == 0 ? ObjectWirePresence.Empty : ObjectWirePresence.Array, null, Array.AsReadOnly(entries));
                // Only storage handle and zero suffix capacity are non-semantic.
                semantic.AppendData(bytes.Slice(start, 9));
                semantic.AppendData(zero);
                semantic.AppendData(values);
                var semanticWords = words;
                while (semanticWords > 0 && BinaryPrimitives.ReadUInt32LittleEndian(bitset[((semanticWords - 1) * 4)..]) == 0) semanticWords--;
                BinaryPrimitives.WriteInt32LittleEndian(normalized, semanticWords);
                semantic.AppendData(normalized); semantic.AppendData(bitset[..(semanticWords * 4)]);
                fields.Add(slot, value); continue;
            }
            fields.Add(slot, value);
            semantic.AppendData(bytes.Slice(start, cursor.Position - start));
        }
        return new(type, parent, own, cursor.Position, Hash(bytes[..cursor.Position]),
            Convert.ToHexString(semantic.GetHashAndReset()).ToLowerInvariant(), new ReadOnlyDictionary<int, ObjectWireValue>(fields));
    }

    private static ObjectWireId ReadId(ReadOnlySpan<byte> raw, bool blocked)
    {
        var kind = IdKind(raw, blocked);
        // Permanent entity equality is type-tagged and excludes wire padding.
        // Raw authentication always covers the complete mandatory 24 bytes.
        var identity = kind switch
        {
            1 => new ObjectEntityId(kind, BinaryPrimitives.ReadInt32LittleEndian(raw[8..]), 0, 0),
            2 => new ObjectEntityId(kind, BinaryPrimitives.ReadInt64LittleEndian(raw[8..]), BinaryPrimitives.ReadInt64LittleEndian(raw[16..]), 0),
            3 => new ObjectEntityId(kind, BinaryPrimitives.ReadInt64LittleEndian(raw[8..]), BinaryPrimitives.ReadInt32LittleEndian(raw[16..]), BinaryPrimitives.ReadInt32LittleEndian(raw[20..])),
            _ => (ObjectEntityId?)null
        };
        return new(kind, kind == 1 ? BinaryPrimitives.ReadInt32LittleEndian(raw[8..]) : null, Hash(raw), identity);
    }
    private static short IdKind(ReadOnlySpan<byte> raw, bool blocked)
    {
        var kind = BinaryPrimitives.ReadInt16LittleEndian(raw);
        if (kind is < 0 or > 3 && !(blocked && kind == -1)) throw Error(StaticObjectErrorCode.InvalidRecord);
        return kind;
    }
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static StaticObjectException Error(StaticObjectErrorCode code) => new(code, StaticObjectFailureStage.Record, "The v119 object record is invalid.");
    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private readonly int _limit;
        internal Reader(ReadOnlySpan<byte> bytes, int limit) { _bytes = bytes; _limit = limit; Position = 0; }
        internal int Position { get; private set; }
        internal ReadOnlySpan<byte> Take(int length)
        {
            // The aggregate envelope can be larger than one record. Enforce
            // its independent byte limit while advancing, before variable
            // field graphs are admitted/materialized, not after Read returns.
            if (length >= 0 && length > _limit - Position) throw Error(StaticObjectErrorCode.LimitExceeded);
            if (length < 0 || length > _bytes.Length - Position) throw Error(StaticObjectErrorCode.InvalidRecord);
            var result = _bytes.Slice(Position, length); Position = checked(Position + length); return result;
        }
        internal short Int16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
        internal int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        internal uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        internal long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        internal bool Present()
        {
            var value = Take(1)[0]; if (value > 1) throw Error(StaticObjectErrorCode.InvalidRecord); return value == 1;
        }
        internal ObjectWireId Id(bool blocked) => ReadId(Take(24), blocked);
    }
}

internal enum ObjectWirePresence { Null, Value, Empty, Array }
internal sealed record ObjectWireElement(int Key, IReadOnlyList<long> Words);
internal sealed record ObjectWireValue(ObjectWirePresence Presence, long? Number, IReadOnlyList<ObjectWireElement> Elements, string? Fingerprint = null)
{
    /// <summary>ARC-WORLD-101: a present string field's bytes up to its terminating zero (the stored length; an inner zero is kept); null
    /// for every other field.</summary>
    public IReadOnlyList<byte>? Text { get; init; }
}
internal readonly record struct ObjectEntityId(short Kind, long First, long Second, int Third);
internal sealed record ObjectWireId(short Kind, int? Number, string Sha256, ObjectEntityId? Entity);
internal sealed record ObjectWireRecord(int Type, ObjectWireId Parent, ObjectWireId Own, int Length, string Sha256, string SemanticSha256, IReadOnlyDictionary<int, ObjectWireValue> Fields);
