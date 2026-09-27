using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

internal abstract record DialogOperationPlan(DialogOperationSource Source);
internal sealed record LocalFlagOperationPlan(DialogOperationSource Source, int Index, int Value) : DialogOperationPlan(Source);
internal sealed record QuestOperationPlan(DialogOperationSource Source, int QuestId, int State) : DialogOperationPlan(Source);
internal sealed record RumorOperationPlan(DialogOperationSource Source, int RumorId) : DialogOperationPlan(Source);

internal sealed record ParsedField(IReadOnlyList<DialogOperationPlan> Operations, int SemanticBytes);

internal sealed class DialogOperationParseException : Exception
{
    internal DialogOperationParseException(DialogFailureKind kind, DialogOperationSource source)
    {
        Kind = kind;
        OperationSource = source;
    }

    internal DialogFailureKind Kind { get; }
    internal DialogOperationSource OperationSource { get; }
}

internal static class DialogOperationParser
{
    internal static ParsedField Parse(
        DlgRecord record,
        DlgField field,
        bool condition,
        DialogRuntimeOptions options,
        Action<DialogOperationSource>? operationAccepted = null)
    {
        if (field.Length > options.MaxOperationFieldBytes)
        {
            throw Failure(DialogFailureKind.SemanticInputLimitExceeded, record, field, 0, field.Length, 0);
        }

        var bytes = field.Bytes;
        var index = 0;
        SkipSeparators(bytes, ref index);
        if (index == bytes.Count)
        {
            throw Failure(DialogFailureKind.InvalidOperation, record, field, 0, field.Length, 0);
        }
        var operations = new List<DialogOperationPlan>();
        while (index < bytes.Count)
        {
            var operationStart = index;
            if (IsCombinatorStart(bytes[index]))
            {
                throw Failure(DialogFailureKind.UnsupportedOperation, record, field, operationStart, 1, operations.Count);
            }
            if (index + 2 > bytes.Count || !IsAsciiLetter(bytes[index]) || !IsAsciiLetter(bytes[index + 1]))
            {
                throw Failure(DialogFailureKind.InvalidOperation, record, field, operationStart, 1, operations.Count);
            }

            var opcode = new string([(char)ToUpper(bytes[index]), (char)ToUpper(bytes[index + 1])]);
            index += 2;
            if (opcode is not ("LF" or "QU" or "RU"))
            {
                throw Failure(DialogFailureKind.UnsupportedOperation, record, field, operationStart, 2, operations.Count);
            }
            // The first argument may be attached to an admitted opcode. This is a
            // deliberately bounded project policy for LF/QU/RU, not a general
            // expression grammar; later argument and operation boundaries remain
            // separator-required.
            SkipSeparators(bytes, ref index);
            var first = ParseInteger(bytes, ref index, record, field, operationStart, operations.Count);
            var secondRequired = opcode is "LF" or "QU";
            int second = 0;
            if (secondRequired)
            {
                RequireSeparators(bytes, ref index, record, field, operationStart, operations.Count);
                second = ParseInteger(bytes, ref index, record, field, operationStart, operations.Count);
            }

            if (index < bytes.Count && !IsSeparator(bytes[index]))
            {
                throw Failure(DialogFailureKind.InvalidOperation, record, field, operationStart, index - operationStart, operations.Count);
            }

            var source = Source(record, field, operationStart, index - operationStart, operations.Count);
            DialogOperationPlan operation = opcode switch
            {
                "LF" when first is >= 0 and <= 31 && second is >= 0 and <= 1 => new LocalFlagOperationPlan(source, first, second),
                "QU" when second is >= 0 and <= 6 => new QuestOperationPlan(source, first, second),
                "RU" when first is >= 1000 => new RumorOperationPlan(source, first),
                _ => throw new DialogOperationParseException(DialogFailureKind.InvalidOperation, source),
            };
            operations.Add(operation);
            if (operations.Count > options.MaxOperationsPerField)
            {
                throw new DialogOperationParseException(DialogFailureKind.OperationLimitExceeded, source);
            }
            operationAccepted?.Invoke(source);
            SkipSeparators(bytes, ref index);
        }

        if (condition && operations.Count != 1)
        {
            var source = operations.Count == 0
                ? Source(record, field, 0, 0, 0)
                : operations[1].Source;
            throw new DialogOperationParseException(
                operations.Count > 1 ? DialogFailureKind.UnsupportedOperation : DialogFailureKind.InvalidOperation,
                source);
        }
        if (condition && operations[0] is not LocalFlagOperationPlan)
        {
            throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, operations[0].Source);
        }

        return new ParsedField(operations.AsReadOnly(), field.Length);
    }

    private static int ParseInteger(IReadOnlyList<byte> bytes, ref int index, DlgRecord record, DlgField field, int operationStart, int ordinal)
    {
        var start = index;
        var negative = index < bytes.Count && bytes[index] == (byte)'-';
        if (negative)
        {
            index++;
        }
        var digitStart = index;
        long magnitude = 0;
        var maximum = negative ? 2_147_483_648L : int.MaxValue;
        while (index < bytes.Count && bytes[index] is >= (byte)'0' and <= (byte)'9')
        {
            magnitude = (magnitude * 10) + bytes[index] - (byte)'0';
            if (magnitude > maximum)
            {
                throw Failure(DialogFailureKind.InvalidOperation, record, field, operationStart, index - operationStart + 1, ordinal);
            }
            index++;
        }
        if (digitStart == index)
        {
            throw Failure(DialogFailureKind.InvalidOperation, record, field, operationStart, Math.Max(1, index - operationStart), ordinal);
        }
        return negative
            ? magnitude == 2_147_483_648L ? int.MinValue : checked(-(int)magnitude)
            : (int)magnitude;
    }

    private static void RequireSeparators(IReadOnlyList<byte> bytes, ref int index, DlgRecord record, DlgField field, int operationStart, int ordinal)
    {
        var before = index;
        SkipSeparators(bytes, ref index);
        if (before == index)
        {
            throw Failure(DialogFailureKind.InvalidOperation, record, field, operationStart, Math.Max(1, index - operationStart), ordinal);
        }
    }

    private static void SkipSeparators(IReadOnlyList<byte> bytes, ref int index)
    {
        while (index < bytes.Count && IsSeparator(bytes[index]))
        {
            index++;
        }
    }

    private static bool IsSeparator(byte value) => value is (byte)' ' or (byte)'\t' or (byte)',' or (byte)';' or (byte)':' or (byte)'(' or (byte)')';
    private static bool IsCombinatorStart(byte value) => value is (byte)'&' or (byte)'|' or (byte)'!' or (byte)'~';
    private static bool IsAsciiLetter(byte value) => value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';
    private static byte ToUpper(byte value) => value is >= (byte)'a' and <= (byte)'z' ? (byte)(value - 32) : value;

    private static DialogOperationSource Source(DlgRecord record, DlgField field, int offset, int length, int ordinal) =>
        new(new DialogNodeId(record.Id), record.SerializedIndex, field.Kind, field.PayloadOffset, offset, length, ordinal);

    private static DialogOperationParseException Failure(DialogFailureKind kind, DlgRecord record, DlgField field, int offset, int length, int ordinal) =>
        new(kind, Source(record, field, offset, length, ordinal));
}
