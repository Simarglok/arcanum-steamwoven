using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

internal abstract record ConversationOperation(DialogOperationSource Source);
internal sealed record ConversationLocalFlag(DialogOperationSource Source, int Index, int Value) : ConversationOperation(Source);
internal sealed record ConversationGlobalFlag(DialogOperationSource Source, int Flag, bool Value) : ConversationOperation(Source);
internal sealed record ConversationQuest(DialogOperationSource Source, int QuestId, int State) : ConversationOperation(Source);
internal sealed record ConversationRumor(DialogOperationSource Source, int RumorId) : ConversationOperation(Source);
internal sealed record ConversationReactionGate(DialogOperationSource Source, int Threshold) : ConversationOperation(Source);
internal sealed record ConversationRaceGate(DialogOperationSource Source, int Selector) : ConversationOperation(Source);
internal sealed record ConversationReaction(DialogOperationSource Source, DialogReactionMode Mode, int Value) : ConversationOperation(Source);
internal sealed record ConversationAlignment(DialogOperationSource Source, DialogReactionMode Mode, int Value) : ConversationOperation(Source);
internal sealed record ConversationGiveItem(DialogOperationSource Source, int ItemName) : ConversationOperation(Source);
internal sealed record ConversationMarkArea(DialogOperationSource Source, int Area) : ConversationOperation(Source);
internal sealed record ConversationJoin(DialogOperationSource Source, int Node) : ConversationOperation(Source);
internal sealed record ConversationFinalLine(DialogOperationSource Source, int Node) : ConversationOperation(Source);

internal enum ConversationField
{
    PlayerCondition,
    NpcAction,
    PlayerAction,
}

/// <summary>
/// conversation-grammar-v1: the bounded DLG operation subset of ARC-SCRIPT-005.
/// <list type="bullet">
/// <item>Player condition: ordered conjunction of LF, GF, RE (signed reaction threshold) and RA (signed race selector).</item>
/// <item>Actions: LF, GF, QU, RU, RE and AL (unsigned target or lexically signed delta), negative IN (NPC gives
/// an item), MM; a Player action may end with one JO 0 node or FL node.</item>
/// </list>
/// A field holding only separators is empty. This is project admission policy, not CE's permissive scanner.
/// </summary>
internal static class DialogConversationGrammar
{
    internal static IReadOnlyList<ConversationOperation> Parse(DlgRecord record, DlgField field, ConversationField role, DialogConversationOptions options)
    {
        if (field.Length > options.MaxOperationFieldBytes)
        {
            throw Failure(DialogFailureKind.SemanticInputLimitExceeded, record, field, 0, field.Length, 0);
        }
        var bytes = field.Bytes;
        var index = 0;
        var operations = new List<ConversationOperation>();
        SkipSeparators(bytes, ref index);
        while (index < bytes.Count)
        {
            var start = index;
            if (index + 2 > bytes.Count || !IsAsciiLetter(bytes[index]) || !IsAsciiLetter(bytes[index + 1]))
            {
                var kind = bytes[index] is (byte)'$' or (byte)'&' or (byte)'|' or (byte)'!' or (byte)'~'
                    ? DialogFailureKind.UnsupportedOperation
                    : DialogFailureKind.InvalidOperation;
                throw Failure(kind, record, field, start, 1, operations.Count);
            }
            var opcode = new string([(char)ToUpper(bytes[index]), (char)ToUpper(bytes[index + 1])]);
            index += 2;
            var admitted = role == ConversationField.PlayerCondition
                ? opcode is "LF" or "GF" or "RE" or "RA"
                : opcode is "LF" or "GF" or "QU" or "RU" or "RE" or "AL" or "IN" or "MM"
                    || role == ConversationField.PlayerAction && opcode is "JO" or "FL";
            if (!admitted)
            {
                throw Failure(DialogFailureKind.UnsupportedOperation, record, field, start, 2, operations.Count);
            }
            if (operations.Count > 0 && operations[^1] is ConversationJoin or ConversationFinalLine)
            {
                // JO/FL transfer control at once in the source; anything after them is outside this subset.
                throw Failure(DialogFailureKind.UnsupportedOperation, record, field, start, 2, operations.Count);
            }
            SkipSeparators(bytes, ref index);
            if (opcode is "RE" or "AL" && index < bytes.Count && bytes[index] is (byte)'>' or (byte)'<')
            {
                // Source tests these markers after atoi already consumed the value; their intent is not established.
                throw Failure(DialogFailureKind.UnsupportedOperation, record, field, start, index - start + 1, operations.Count);
            }
            var (first, sign) = ParseInteger(bytes, ref index, record, field, start, operations.Count);
            var second = 0;
            if (opcode is "LF" or "GF" or "QU" or "JO")
            {
                RequireSeparators(bytes, ref index, record, field, start, operations.Count);
                (second, var secondSign) = ParseInteger(bytes, ref index, record, field, start, operations.Count);
                if (secondSign == '+')
                {
                    throw Failure(DialogFailureKind.InvalidOperation, record, field, start, index - start, operations.Count);
                }
            }
            if (index < bytes.Count && !IsSeparator(bytes[index]))
            {
                throw Failure(DialogFailureKind.InvalidOperation, record, field, start, index - start, operations.Count);
            }
            var source = Source(record, field, start, index - start, operations.Count);
            var reactionAction = opcode == "RE" && role != ConversationField.PlayerCondition;
            if (sign == '+' && !reactionAction && opcode != "AL")
            {
                throw new DialogOperationParseException(DialogFailureKind.InvalidOperation, source);
            }
            ConversationOperation operation = opcode switch
            {
                "LF" when first is >= 0 and <= 31 && second is 0 or 1 => new ConversationLocalFlag(source, first, second),
                "GF" when first >= 0 && second is 0 or 1 => new ConversationGlobalFlag(source, first, second == 1),
                "QU" when second is >= 0 and <= 6 => new ConversationQuest(source, first, second),
                "RU" when first >= 1000 => new ConversationRumor(source, first),
                "RE" when !reactionAction && first != int.MinValue => new ConversationReactionGate(source, first),
                "RE" when reactionAction && sign == '\0' => new ConversationReaction(source, DialogReactionMode.Target, first),
                "RE" when reactionAction => new ConversationReaction(source, DialogReactionMode.Adjust, first),
                "AL" when sign == '\0' => new ConversationAlignment(source, DialogReactionMode.Target, first),
                "AL" => new ConversationAlignment(source, DialogReactionMode.Adjust, first),
                "RA" when first != int.MinValue => new ConversationRaceGate(source, first),
                "IN" when first < 0 && first != int.MinValue => new ConversationGiveItem(source, -first),
                "IN" => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "MM" when first >= 0 => new ConversationMarkArea(source, first),
                "JO" when first == 0 && second > 0 => new ConversationJoin(source, second),
                "JO" when second > 0 => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "FL" when first > 0 => new ConversationFinalLine(source, first),
                _ => throw new DialogOperationParseException(DialogFailureKind.InvalidOperation, source),
            };
            operations.Add(operation);
            if (operations.Count > options.MaxOperationsPerField)
            {
                throw new DialogOperationParseException(DialogFailureKind.OperationLimitExceeded, source);
            }
            SkipSeparators(bytes, ref index);
        }
        return operations.AsReadOnly();
    }

    /// <summary>Signed ASCII decimal; the returned sign is the lexical one ('\0' when absent) so RE can tell target from delta.</summary>
    private static (int Value, char Sign) ParseInteger(IReadOnlyList<byte> bytes, ref int index, DlgRecord record, DlgField field, int start, int ordinal)
    {
        var sign = index < bytes.Count && bytes[index] is (byte)'-' or (byte)'+' ? (char)bytes[index] : '\0';
        if (sign != '\0')
        {
            index++;
        }
        var digits = index;
        long magnitude = 0;
        var maximum = sign == '-' ? 2_147_483_648L : int.MaxValue;
        while (index < bytes.Count && bytes[index] is >= (byte)'0' and <= (byte)'9')
        {
            magnitude = (magnitude * 10) + bytes[index] - (byte)'0';
            if (magnitude > maximum)
            {
                throw Failure(DialogFailureKind.InvalidOperation, record, field, start, index - start + 1, ordinal);
            }
            index++;
        }
        if (digits == index)
        {
            throw Failure(DialogFailureKind.InvalidOperation, record, field, start, Math.Max(1, index - start), ordinal);
        }
        var value = sign == '-' ? magnitude == 2_147_483_648L ? int.MinValue : checked(-(int)magnitude) : (int)magnitude;
        return (value, sign);
    }

    private static void RequireSeparators(IReadOnlyList<byte> bytes, ref int index, DlgRecord record, DlgField field, int start, int ordinal)
    {
        var before = index;
        SkipSeparators(bytes, ref index);
        if (before == index)
        {
            throw Failure(DialogFailureKind.InvalidOperation, record, field, start, Math.Max(1, index - start), ordinal);
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
    private static bool IsAsciiLetter(byte value) => value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';
    private static byte ToUpper(byte value) => value is >= (byte)'a' and <= (byte)'z' ? (byte)(value - 32) : value;

    private static DialogOperationSource Source(DlgRecord record, DlgField field, int offset, int length, int ordinal) =>
        new(new DialogNodeId(record.Id), record.SerializedIndex, field.Kind, field.PayloadOffset, offset, length, ordinal);

    private static DialogOperationParseException Failure(DialogFailureKind kind, DlgRecord record, DlgField field, int offset, int length, int ordinal) =>
        new(kind, Source(record, field, offset, length, ordinal));
}
