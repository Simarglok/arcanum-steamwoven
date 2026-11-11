using System.Buffers.Binary;

namespace Arcanum.Data.Scripts;

/// <summary>Why an SCR payload was or was not framed (docs/scripting/scr-program-admission.md).</summary>
public enum ScrReadStatus
{
    Read,
    /// <summary>The payload exceeds <see cref="ScrReaderOptions.MaxBytes"/>.</summary>
    TooLarge,
    /// <summary>Shorter than the 64-byte header.</summary>
    Truncated,
    /// <summary>A negative line count.</summary>
    NegativeCount,
    /// <summary>The stored capacity is below the line count.</summary>
    CapacityBelowCount,
    /// <summary>The payload is not exactly the header plus <c>count × 132</c> bytes.</summary>
    LengthMismatch,
    /// <summary>More lines than <see cref="ScrReaderOptions.MaxLines"/>.</summary>
    TooManyLines,
}

public sealed class ScrReaderOptions
{
    /// <summary>The largest installed script has 221 lines (29,236 bytes, ARC-RES-045).</summary>
    public int MaxBytes { get; init; } = 1024 * 1024;
    public int MaxLines { get; init; } = 4096;

    internal void Validate()
    {
        if (MaxBytes < ScrProgram.HeaderBytes || MaxLines <= 0)
            throw new ArgumentOutOfRangeException(nameof(ScrReaderOptions), "SCR limits must be positive and allow the header.");
    }
}

/// <summary>One condition or action: its type number and eight operands, each a kind byte and a
/// 32-bit value. Which operands a type reads is its signature (<see cref="ScrCatalogue"/>); the
/// others keep whatever the editor left there (ARC-RES-045: nearly every clause) and mean nothing.</summary>
public sealed class ScrClause : IEquatable<ScrClause>
{
    public const int Operands = 8;
    private readonly byte[] _kinds;
    private readonly int[] _values;

    public ScrClause(int type, ReadOnlySpan<byte> kinds, ReadOnlySpan<int> values)
    {
        if (kinds.Length != Operands || values.Length != Operands) throw new ArgumentException("A clause has exactly eight operands.");
        Type = type; _kinds = kinds.ToArray(); _values = values.ToArray();
        Kinds = Array.AsReadOnly(_kinds); Values = Array.AsReadOnly(_values);
    }

    public int Type { get; }
    public IReadOnlyList<byte> Kinds { get; }
    public IReadOnlyList<int> Values { get; }
    public byte Kind(int slot) => _kinds[slot];
    public int Value(int slot) => _values[slot];

    public bool Equals(ScrClause? other) => other is not null && Type == other.Type && _kinds.AsSpan().SequenceEqual(other._kinds) && _values.AsSpan().SequenceEqual(other._values);
    public override bool Equals(object? obj) => Equals(obj as ScrClause);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Type); hash.AddBytes(_kinds);
        foreach (var value in _values) hash.Add(value);
        return hash.ToHashCode();
    }
}

/// <summary>One line: a condition, the action run when it holds and the action run when it does not.</summary>
public sealed record ScrLine(int Number, ScrClause Condition, ScrClause OnTrue, ScrClause OnFalse);

/// <summary>The status, and the program exactly when the status is <see cref="ScrReadStatus.Read"/>.</summary>
public sealed class ScrReadResult
{
    internal ScrReadResult(ScrReadStatus status, ScrProgram? program) { Status = status; Program = program; }
    public ScrReadStatus Status { get; }
    public ScrProgram? Program { get; }
}

/// <summary>An immutable SCR script as stored: the attachment header the file supplies (initial
/// local flags and counters), the file flags, the stored capacity and the lines in order. The
/// 40-byte description and the stored pointer word are not kept.</summary>
public sealed class ScrProgram
{
    public const int HeaderBytes = 64, LineBytes = 132, ClauseBytes = 44;
    private readonly IReadOnlyList<ScrLine> _lines;

    private ScrProgram(uint initialLocalFlags, uint initialCounters, uint fileFlags, int capacity, ScrLine[] lines)
    {
        InitialLocalFlags = initialLocalFlags; InitialCounters = initialCounters; FileFlags = fileFlags; Capacity = capacity; _lines = Array.AsReadOnly(lines);
    }

    /// <summary>The 32 local flags a run starts from when no attachment supplies its own.</summary>
    public uint InitialLocalFlags { get; }
    /// <summary>The four byte counters, counter 0 in the low byte.</summary>
    public uint InitialCounters { get; }
    /// <summary>Trap kinds, auto-removal, death and surrender speech, trap radius, teleport trigger.</summary>
    public uint FileFlags { get; }
    public int Capacity { get; }
    public IReadOnlyList<ScrLine> Lines => _lines;
    public int Count => _lines.Count;

    /// <summary>Frames the whole payload strictly: the header, a non-negative line count, a
    /// capacity not below it, and exactly <c>count</c> lines with nothing after them. Counts are
    /// checked before the line limit, so a malformed header never reads as a budget refusal.
    /// Invalid options throw <see cref="ArgumentOutOfRangeException"/>; a payload never throws.</summary>
    [OriginalFunction(0x44C730, OriginalFunctionScope.Partial)]
    public static ScrReadResult Read(ReadOnlySpan<byte> payload, ScrReaderOptions? options = null)
    {
        options ??= new();
        options.Validate();
        if (payload.Length > options.MaxBytes) return new(ScrReadStatus.TooLarge, null);
        if (payload.Length < HeaderBytes) return new(ScrReadStatus.Truncated, null);
        var count = BinaryPrimitives.ReadInt32LittleEndian(payload[52..]);
        var capacity = BinaryPrimitives.ReadInt32LittleEndian(payload[56..]);
        if (count < 0) return new(ScrReadStatus.NegativeCount, null);
        if (capacity < count) return new(ScrReadStatus.CapacityBelowCount, null);
        if (payload.Length != HeaderBytes + (long)count * LineBytes) return new(ScrReadStatus.LengthMismatch, null);
        if (count > options.MaxLines) return new(ScrReadStatus.TooManyLines, null);
        var lines = new ScrLine[count];
        for (var line = 0; line < count; line++)
        {
            var row = payload.Slice(HeaderBytes + line * LineBytes, LineBytes);
            lines[line] = new(line, Clause(row[..ClauseBytes]), Clause(row.Slice(ClauseBytes, ClauseBytes)), Clause(row.Slice(2 * ClauseBytes, ClauseBytes)));
        }
        return new(ScrReadStatus.Read, new(BinaryPrimitives.ReadUInt32LittleEndian(payload), BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[48..]), capacity, lines));

        static ScrClause Clause(ReadOnlySpan<byte> slot)
        {
            Span<int> values = stackalloc int[ScrClause.Operands];
            for (var i = 0; i < ScrClause.Operands; i++) values[i] = BinaryPrimitives.ReadInt32LittleEndian(slot[(12 + 4 * i)..]);
            return new(BinaryPrimitives.ReadInt32LittleEndian(slot), slot.Slice(4, ScrClause.Operands), values);
        }
    }
}
