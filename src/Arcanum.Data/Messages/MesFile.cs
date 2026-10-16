namespace Arcanum.Data.Messages;

/// <summary>Why a MES payload was or was not accepted. Refusals cover shapes the original
/// truncates or silently stops on, reads differently by platform or file origin, keeps only
/// after a warning, or resolves arbitrarily (docs/formats/mes-reader.md).</summary>
public enum MesParseStatus
{
    Parsed,
    TooLarge,
    TooManyMessages,
    /// <summary>A NUL byte; the original's line reader treats it as the end of the line or file.</summary>
    NulByte,
    /// <summary>A 0xFF byte, which the original's signed <c>char</c> reader compares equal to
    /// end of file, or a 0x1A byte, end of file for a loose file read in Windows text mode.</summary>
    EndOfFileByte,
    /// <summary>A CR not followed by LF; the original's archive text mode drops it and repeats
    /// the next character.</summary>
    LoneCarriageReturn,
    /// <summary>A field opened with <c>{</c> that reaches the end of the payload.</summary>
    UnterminatedField,
    /// <summary>A number field with no text field after it.</summary>
    MissingText,
    /// <summary>A number field that is not 1..10 ASCII digits within the int32 range.</summary>
    UnsupportedNumber,
    /// <summary>A <c>{</c> inside a field; the original only warns and keeps the brace.</summary>
    BraceInField,
    TextTooLong,
    /// <summary>Two messages with one number; the original only warns and its lookup may return either.</summary>
    DuplicateNumber,
}

public sealed class MesReaderOptions
{
    /// <summary>Upper bound on the payload; the largest Crash Site description file is 39,124 bytes.</summary>
    public int MaxBytes { get; init; } = 4 * 1024 * 1024;
    public int MaxMessages { get; init; } = 65_536;
    /// <summary>The original copies a field into a 2,000-byte buffer and terminates it at the
    /// field length, so 1,999 is the longest text it holds without truncation or overflow.</summary>
    public int MaxTextBytes { get; init; } = MesFile.OriginalMaxTextBytes;

    internal void Validate()
    {
        if (MaxBytes <= 0 || MaxMessages <= 0 || MaxTextBytes <= 0 || MaxTextBytes > MesFile.OriginalMaxTextBytes)
            throw new ArgumentOutOfRangeException(nameof(MesReaderOptions), "MES limits must be positive and the text limit at most the original's.");
    }
}

/// <summary>One message. Equality compares number, line and text bytes.</summary>
/// <param name="Line">1-based line of the number field's opening brace.</param>
/// <param name="Bytes">The text as the original reads it: the stored bytes with each CR LF
/// read as LF (text mode); no decoding, trimming or other change.</param>
public sealed record MesMessage(int Number, int Line, ReadOnlyMemory<byte> Bytes)
{
    public bool Equals(MesMessage? other) =>
        other is not null && Number == other.Number && Line == other.Line && Bytes.Span.SequenceEqual(other.Bytes.Span);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Number); hash.Add(Line); hash.AddBytes(Bytes.Span);
        return hash.ToHashCode();
    }
}

/// <param name="Line">Where a refusal was found (1-based), when it has a position.</param>
/// <param name="Number">The message number involved in <see cref="MesParseStatus.DuplicateNumber"/>.</param>
public sealed record MesParseResult(MesParseStatus Status, MesFile? File, int? Line = null, int? Number = null);

/// <summary>An immutable MES message table: <c>{number}{text}</c> pairs in any order, with
/// anything outside braces ignored, as the original reads it. Messages are ordered by number
/// for lookup. Text is never decoded here; see <see cref="MesText"/>.</summary>
public sealed class MesFile
{
    public const int OriginalMaxTextBytes = 1999;
    private readonly MesMessage[] _messages;
    private readonly int[] _numbers;

    private MesFile(MesMessage[] messages)
    {
        _messages = messages;
        _numbers = messages.Select(m => m.Number).ToArray();
    }

    /// <summary>Ordered by number.</summary>
    public IReadOnlyList<MesMessage> Messages => Array.AsReadOnly(_messages);
    public int Count => _messages.Length;
    /// <summary>The highest number, or null for a file without messages.</summary>
    public int? LastNumber => _messages.Length == 0 ? null : _messages[^1].Number;

    public MesMessage? Find(int number)
    {
        var index = Array.BinarySearch(_numbers, number);
        return index >= 0 ? _messages[index] : null;
    }

    public static MesParseResult Parse(ReadOnlySpan<byte> payload, MesReaderOptions? options = null)
    {
        options ??= new();
        options.Validate();
        if (payload.Length > options.MaxBytes) return new(MesParseStatus.TooLarge, null);
        var line = 1;
        for (var i = 0; i < payload.Length; i++)
        {
            if (payload[i] == 0) return new(MesParseStatus.NulByte, null, line);
            if (payload[i] is 0xFF or 0x1A) return new(MesParseStatus.EndOfFileByte, null, line);
            if (payload[i] == (byte)'\r' && (i + 1 == payload.Length || payload[i + 1] != (byte)'\n')) return new(MesParseStatus.LoneCarriageReturn, null, line);
            if (payload[i] == (byte)'\n') line++;
        }

        var messages = new List<MesMessage>();
        var at = 0;
        line = 1;
        while (true)
        {
            if (!SeekOpen(payload, ref at, ref line)) break;
            var numberLine = line;
            var status = ReadField(payload, ref at, ref line, options.MaxTextBytes, out var numberBytes);
            if (status != MesParseStatus.Parsed) return new(status == MesParseStatus.TextTooLong ? MesParseStatus.UnsupportedNumber : status, null, line);
            if (!TryNumber(numberBytes, out var number)) return new(MesParseStatus.UnsupportedNumber, null, numberLine);
            if (!SeekOpen(payload, ref at, ref line)) return new(MesParseStatus.MissingText, null, line);
            var textLine = line;
            status = ReadField(payload, ref at, ref line, options.MaxTextBytes, out var text);
            if (status != MesParseStatus.Parsed) return new(status, null, status == MesParseStatus.TextTooLong ? textLine : line);
            if (messages.Count == options.MaxMessages) return new(MesParseStatus.TooManyMessages, null, numberLine);
            messages.Add(new(number, numberLine, text));
        }

        var ordered = messages.OrderBy(m => m.Number).ToArray();
        for (var i = 1; i < ordered.Length; i++)
            if (ordered[i].Number == ordered[i - 1].Number) return new(MesParseStatus.DuplicateNumber, null, ordered[i].Line, ordered[i].Number);
        return new(MesParseStatus.Parsed, new MesFile(ordered));
    }

    /// <summary>Skips to the next <c>{</c>; everything before it, a stray <c>}</c> included, is ignored.</summary>
    private static bool SeekOpen(ReadOnlySpan<byte> bytes, ref int at, ref int line)
    {
        while (at < bytes.Length && bytes[at] != (byte)'{')
        {
            if (bytes[at] == (byte)'\n') line++;
            at++;
        }
        return at < bytes.Length;
    }

    /// <summary>Reads from the <c>{</c> at <paramref name="at"/> to the matching <c>}</c>, reading
    /// each CR LF as LF (every CR was checked to precede an LF).</summary>
    private static MesParseStatus ReadField(ReadOnlySpan<byte> bytes, ref int at, ref int line, int maxBytes, out byte[] field)
    {
        field = [];
        var value = new List<byte>();
        at++;
        while (at < bytes.Length && bytes[at] != (byte)'}')
        {
            var b = bytes[at++];
            if (b == (byte)'{') return MesParseStatus.BraceInField;
            if (b == (byte)'\r') continue;
            if (b == (byte)'\n') line++;
            if (value.Count == maxBytes) return MesParseStatus.TextTooLong;
            value.Add(b);
        }
        if (at == bytes.Length) return MesParseStatus.UnterminatedField;
        at++;
        field = value.ToArray();
        return MesParseStatus.Parsed;
    }

    private static bool TryNumber(ReadOnlySpan<byte> field, out int number)
    {
        number = 0;
        if (field.Length is 0 or > 10) return false;
        long value = 0;
        foreach (var b in field)
        {
            if (b is < (byte)'0' or > (byte)'9') return false;
            value = value * 10 + (b - '0');
        }
        if (value > int.MaxValue) return false;
        number = (int)value;
        return true;
    }
}
