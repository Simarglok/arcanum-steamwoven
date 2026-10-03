using System.Buffers.Binary;

namespace Arcanum.CrashSiteProbe;

internal static partial class Program
{
    private static (int X, int Y) RebaseSectorLocation(long raw, int sectorX, int sectorY) =>
        (checked(sectorX * 64 + (int)(raw & 63)), checked(sectorY * 64 + (int)((raw >> 32) & 63)));

    private static void AreaContentOracles()
    {
        // Literal slot/mask layout for scenery's common overlay slot5. This
        // encoder never calls FieldsFor, K, SetBit, or the parser's fixture code.
        static byte[] ArrayRecord(int handle, uint[] words, int[] data, bool present = true)
        {
            var bytes = new byte[70 + (present ? 17 + 4 * data.Length + 4 * words.Length : 1)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 119);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 7001);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(36), 17);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), 3);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(56), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(58), 32);
            if (!present) return bytes;
            bytes[70] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(71), 4);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(75), data.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(79), handle);
            for (var i = 0; i < data.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(83 + i * 4), data[i]);
            var p = 83 + data.Length * 4;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(p), words.Length);
            for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(p + 4 + i * 4), words[i]);
            return bytes;
        }
        static string Content(byte[] bytes)
        {
            var v = ObjectWire.ParseNext(bytes, out var consumed);
            if (consumed != bytes.Length) throw new InvalidOperationException("LITERAL_EOF");
            return SemanticHash(bytes, v);
        }
        static void Equal(byte[] a, byte[] b, bool expected)
        {
            if ((Content(a) == Content(b)) != expected) throw new InvalidOperationException("LITERAL_CONTENT_ORACLE");
        }
        var one = ArrayRecord(17, [1], [0x11223344]);
        Equal(one, ArrayRecord(99, [1], [0x11223344]), true);
        Equal(one, ArrayRecord(99, [1, 0, 0], [0x11223344]), true);
        Equal(one, ArrayRecord(17, [2], [0x11223344]), false);
        Equal(one, ArrayRecord(17, [1], [0x55667788]), false);
        Equal(ArrayRecord(17, [0], []), ArrayRecord(99, [0, 0], []), true);
        Equal(ArrayRecord(17, [0], []), ArrayRecord(17, [], [], false), false);
        Equal(ArrayRecord(17, [1, 0, 1], [11, 22]), ArrayRecord(17, [1, 1], [11, 22]), false);
        Equal(ArrayRecord(17, [1, 1], [11, 22]), ArrayRecord(17, [1, 1], [22, 11]), false);
        AssertRejected(() => Content(ArrayRecord(17, [], [])), "zero words FILE incompatible");
        AssertRejected(() => Content(ArrayRecord(17, [3], [11])), "excess population");
        AssertRejected(() => Content(ArrayRecord(17, [1], [11, 22])), "deficient population");
        foreach (var offset in new[] { 12, 30, 36, 44 })
        {
            var changed = one.ToArray(); changed[offset] ^= 1; Equal(one, changed, false);
        }
        for (var n = 70; n < one.Length; n++) AssertRejected(() => Content(one[..n]), "array truncation");
        foreach (var offset in new[] { 71, 75, 87 })
        {
            var bad = one.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(offset), -1);
            AssertRejected(() => Content(bad), "negative width/count/words");
        }
        var next = one.ToArray(); next[36] = 42;
        byte[] stream = [..new byte[16], ..ArrayRecord(99, [1, 0, 0], [0x11223344]), ..next];
        var framed = FrameAggregate(stream, stream.Length);
        if (framed.Count != 2 || framed[1].Offset != 119 || framed[1].Instance.OwnId.AidValue() != 42) throw new InvalidOperationException("CROSS_RECORD_LITERAL");
        var raw = unchecked((long)0x765432207654321eUL);
        if (RebaseSectorLocation(raw, 1452, 1290) != (92958, 82592)) throw new InvalidOperationException("HIGH_BITS_REBASE");
        if (AreaBin(92864L | (82496L << 32)) != "core" || AreaBin(93056L | (82496L << 32)) != "context" || AreaBin(93120L | (82496L << 32)) != "outside") throw new InvalidOperationException("HALF_OPEN_LITERAL");
        // Absent script slot inherits; explicitly present-null overrides. Literal
        // common slot31 is bitmap bit31, distinct from script-array SAP index9.
        var scriptNull = ArrayRecord(0, [], [], false);
        BinaryPrimitives.WriteUInt32LittleEndian(scriptNull.AsSpan(58), 0x80000000);
        var nullInstance = ObjectWire.ParseNext(scriptNull, out _);
        var absent = scriptNull[..70];
        BinaryPrimitives.WriteInt16LittleEndian(absent.AsSpan(56), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(absent.AsSpan(58), 0);
        var absentInstance = ObjectWire.ParseNext(absent, out _);
        var prototype = new PrototypeInfo(absentInstance.PrototypeId, 3, 0, 0)
        {
            Scripts = [new ScriptAttachment(9, 0, 0, 5001)],
            NumericFields = new Dictionary<int, int> { [0] = 123, [2] = -7, [3] = 11, [18] = 4 }
        };
        if (EffectiveScripts(scriptNull, nullInstance, prototype).Count != 0 || EffectiveScripts(absent, absentInstance, prototype).Single().sap != 9)
            throw new InvalidOperationException("LITERAL_SCRIPT_INHERITANCE");
        var overridden = absentInstance with { NumericFields = new Dictionary<int, int> { [2] = 0, [18] = 0 } };
        var numeric = EffectiveNumeric(overridden, prototype);
        if (numeric[0] != 123 || numeric[2] != 0 || numeric[3] != 11 || numeric[18] != 0) throw new InvalidOperationException("LITERAL_ZERO_OVERRIDES");
        Console.WriteLine("area-content-oracles: PASS (literal handles/capacity/keys/elements/null/EOF/high-bit/boundaries)");
    }
}
