namespace Arcanum.Data.Messages;

/// <summary>Why a MES payload was or was not accepted. Every refusal names a shape whose
/// original result is a silent truncation, a warning-only guess or platform-dependent, so it
/// is refused instead of modelled (docs/formats/mes-reader.md).</summary>
public enum MesParseStatus
{
    Parsed,
    TooLarge,
    TooManyMessages,
    /// <summary>A NUL byte; the original's line reader treats it as the end of the line or file.</summary>
    NulByte,
    /// <summary>A 0xFF byte; the original's character reader returns a plain <c>char</c>, so
    /// where <c>char</c> is signed this byte compares equal to end-of-file.</summary>
    EndOfFileByte,
    /// <summary>A field opened with <c>{</c> that reaches the end of the payload.</summary>
    UnterminatedField,
    /// <summary>A number field with no text field after it.</summary>
    MissingText,
    /// <summary>A number field that is not 1..10 ASCII digits within the int32 range.</summary>
    UnsupportedNumber,
    /// <summary>A <c>{</c> inside a field; the original only warns and keeps the brace.</summary>
    BraceInField,
    /// <summary>CR or LF inside a text field; whether CR survives depends on how the file was opened.</summary>
    LineBreakInText,
    TextTooLong,
    /// <summary>Two messages with one number; the original's lookup would return either.</summary>
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

/// <param name="Line">1-based line of the number field's opening brace.</param>
/// <param name="Bytes">The text field exactly as stored: no decoding, trimming or translation.</param>
public sealed record MesMessage(int Number, int Line, ReadOnlyMemory<byte> Bytes);

/// <param name="Line">Where a refusal was found (1-based), when it has a position.</param>
/// <param name="Number">The message number involved in <see cref="MesParseStatus.DuplicateNumber"/>.</param>
public sealed record MesParseResult(MesParseStatus Status, MesFile? File, int? Line = null, int? Number = null);

/// <summary>An immutable, byte-preserving MES message table: <c>{number}{text}</c> pairs in
/// any order, with anything outside braces ignored, as the original reads it. Messages are
/// ordered by number for lookup. Text is never decoded here; see <see cref="MesText"/>.</summary>
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
        var bytes = payload.ToArray();
        var line = 1;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0) return new(MesParseStatus.NulByte, null, line);
            if (bytes[i] == 0xFF) return new(MesParseStatus.EndOfFileByte, null, line);
            if (bytes[i] == (byte)'\n') line++;
        }

        var messages = new List<MesMessage>();
        var at = 0;
        line = 1;
        while (true)
        {
            if (!SeekOpen(bytes, ref at, ref line)) break;
            var numberLine = line;
            var status = ReadField(bytes, ref at, ref line, out var numberStart, out var numberLength);
            if (status != MesParseStatus.Parsed) return new(status, null, line);
            if (!TryNumber(bytes.AsSpan(numberStart, numberLength), out var number)) return new(MesParseStatus.UnsupportedNumber, null, numberLine);
            if (!SeekOpen(bytes, ref at, ref line)) return new(MesParseStatus.MissingText, null, line);
            var textLine = line;
            status = ReadField(bytes, ref at, ref line, out var textStart, out var textLength);
            if (status != MesParseStatus.Parsed) return new(status, null, line);
            var text = bytes.AsMemory(textStart, textLength);
            if (text.Span.IndexOfAny((byte)'\r', (byte)'\n') >= 0) return new(MesParseStatus.LineBreakInText, null, textLine);
            if (textLength > options.MaxTextBytes) return new(MesParseStatus.TextTooLong, null, textLine);
            if (messages.Count == options.MaxMessages) return new(MesParseStatus.TooManyMessages, null, numberLine);
            messages.Add(new(number, numberLine, text));
        }

        var ordered = messages.OrderBy(m => m.Number).ToArray();
        for (var i = 1; i < ordered.Length; i++)
            if (ordered[i].Number == ordered[i - 1].Number) return new(MesParseStatus.DuplicateNumber, null, ordered[i].Line, ordered[i].Number);
        return new(MesParseStatus.Parsed, new MesFile(ordered));
    }

    /// <summary>Skips to the next <c>{</c>; everything before it, a stray <c>}</c> included, is ignored.</summary>
    private static bool SeekOpen(byte[] bytes, ref int at, ref int line)
    {
        while (at < bytes.Length && bytes[at] != (byte)'{')
        {
            if (bytes[at] == (byte)'\n') line++;
            at++;
        }
        return at < bytes.Length;
    }

    /// <summary>Reads from the <c>{</c> at <paramref name="at"/> to the matching <c>}</c>.</summary>
    private static MesParseStatus ReadField(byte[] bytes, ref int at, ref int line, out int start, out int length)
    {
        start = ++at;
        while (at < bytes.Length && bytes[at] != (byte)'}')
        {
            if (bytes[at] == (byte)'{') { length = 0; return MesParseStatus.BraceInField; }
            if (bytes[at] == (byte)'\n') line++;
            at++;
        }
        length = at - start;
        if (at == bytes.Length) return MesParseStatus.UnterminatedField;
        at++;
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
