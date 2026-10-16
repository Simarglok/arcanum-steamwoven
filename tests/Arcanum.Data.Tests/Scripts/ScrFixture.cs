using System.Buffers.Binary;

namespace Arcanum.Data.Tests.Scripts;

/// <summary>Synthetic SCR payloads: a 64-byte header and 132-byte lines of three clauses.</summary>
internal static class ScrFixture
{
    internal readonly record struct Clause(int Type, (byte Kind, int Value)[] Operands)
    {
        public static Clause Of(int type, params (byte Kind, int Value)[] operands) => new(type, operands);
    }

    internal static (byte, int) N(int value) => (3, value);

    internal static byte[] Script(params (Clause Condition, Clause OnTrue, Clause OnFalse)[] lines) => Script(0, 0, 0, lines.Length, lines);

    internal static byte[] Script(uint localFlags, uint counters, uint fileFlags, int capacity, params (Clause Condition, Clause OnTrue, Clause OnFalse)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, localFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), counters);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), fileFlags);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), capacity);
        for (var line = 0; line < lines.Length; line++)
        {
            var at = 64 + line * 132;
            Write(bytes.AsSpan(at, 44), lines[line].Condition);
            Write(bytes.AsSpan(at + 44, 44), lines[line].OnTrue);
            Write(bytes.AsSpan(at + 88, 44), lines[line].OnFalse);
        }
        return bytes;
    }

    private static void Write(Span<byte> slot, Clause clause)
    {
        BinaryPrimitives.WriteInt32LittleEndian(slot, clause.Type);
        for (var i = 0; i < clause.Operands.Length; i++)
        {
            slot[4 + i] = clause.Operands[i].Kind;
            BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], clause.Operands[i].Value);
        }
    }
}
