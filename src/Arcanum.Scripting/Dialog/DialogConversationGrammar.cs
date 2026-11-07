using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

internal abstract record ConversationOperation(DialogOperationSource Source);
internal sealed record ConversationLocalFlag(DialogOperationSource Source, int Index, int Value) : ConversationOperation(Source);
internal sealed record ConversationGlobalFlag(DialogOperationSource Source, int Flag, bool Value) : ConversationOperation(Source);
internal sealed record ConversationQuest(DialogOperationSource Source, int QuestId, int State) : ConversationOperation(Source);
internal sealed record ConversationRumor(DialogOperationSource Source, int RumorId) : ConversationOperation(Source);
internal sealed record ConversationQuellRumor(DialogOperationSource Source, int RumorId) : ConversationOperation(Source);
/// <summary>RU n as a condition (ARC-DEV-171, ledger RU1–RU2): n ≥ 1000 holds when the PC knows rumor n, n ≤ -1000 when it does not know rumor −n.</summary>
internal sealed record ConversationRumorGate(DialogOperationSource Source, int Selector) : ConversationOperation(Source);
/// <summary>RQ n as a condition (ARC-DEV-171, ledger RU1–RU2): n ≥ 1000 holds when rumor n is quelled, n ≤ -1000 when rumor −n is not.</summary>
internal sealed record ConversationQuellGate(DialogOperationSource Source, int Selector) : ConversationOperation(Source);
internal sealed record ConversationReactionGate(DialogOperationSource Source, int Threshold) : ConversationOperation(Source);
internal sealed record ConversationRaceGate(DialogOperationSource Source, int Selector) : ConversationOperation(Source);
internal sealed record ConversationReaction(DialogOperationSource Source, DialogReactionMode Mode, int Value) : ConversationOperation(Source);
internal sealed record ConversationAlignment(DialogOperationSource Source, DialogReactionMode Mode, int Value) : ConversationOperation(Source);
internal sealed record ConversationGiveItem(DialogOperationSource Source, int ItemName) : ConversationOperation(Source);
internal sealed record ConversationMarkArea(DialogOperationSource Source, int Area) : ConversationOperation(Source);
internal sealed record ConversationJoin(DialogOperationSource Source, int Node) : ConversationOperation(Source);
internal sealed record ConversationFinalLine(DialogOperationSource Source, int Node) : ConversationOperation(Source);
internal sealed record ConversationWaiting(DialogOperationSource Source, bool Value) : ConversationOperation(Source);
internal sealed record ConversationUnwait(DialogOperationSource Source, int Node) : ConversationOperation(Source);
/// <summary>PS, HA, SK (a level gate) and TR (a training gate): a non-negative threshold is a minimum, a negative one a maximum
/// of its absolute value (both inclusive).</summary>
internal sealed record ConversationSkillGate(DialogOperationSource Source, int Skill, int Threshold, bool Training) : ConversationOperation(Source);
internal sealed record ConversationSkillTraining(DialogOperationSource Source, int Skill, int Training) : ConversationOperation(Source);
internal sealed record ConversationExpertTeacher(DialogOperationSource Source, int Skill, int Node) : ConversationOperation(Source);
/// <summary>ARC-TECHNOLOGY-006: SU, the schematic window with the NPC as crafter (ledger NC1).</summary>
internal sealed record ConversationSchematics(DialogOperationSource Source) : ConversationOperation(Source);
internal sealed record ConversationMetBefore(DialogOperationSource Source, bool Value) : ConversationOperation(Source);
/// <summary>RP n (ARC-WORLD-044): as a condition n > 0 holds n, n ≤ 0 lacks −n; as an action n > 0 adds n, n ≤ 0 removes −n.</summary>
internal sealed record ConversationReputation(DialogOperationSource Source, int Value) : ConversationOperation(Source);
internal sealed record ConversationFatePoint(DialogOperationSource Source) : ConversationOperation(Source);
/// <summary>XP n as an action (ARC-DEV-178, ledger DAC9): the PC gets the experience of quest experience level n.</summary>
internal sealed record ConversationExperience(DialogOperationSource Source, int Level) : ConversationOperation(Source);
/// <summary>gv, pv, pf (ARC-DEV-152, ledger DG1–DG4): as a condition the cell equals <paramref name="Value"/>; as an action it takes it.</summary>
internal sealed record ConversationScriptStore(DialogOperationSource Source, DialogScriptStore Store, int Index, int Value) : ConversationOperation(Source);

internal enum ConversationField
{
    PlayerCondition,
    NpcAction,
    PlayerAction,
}

/// <summary>
/// conversation-grammar-v1: the bounded DLG operation subset of ARC-SCRIPT-005.
/// <list type="bullet">
/// <item>Player condition: ordered conjunction of LF, GF, RE (signed reaction threshold), RA (signed race selector) and
/// ME 0/1 (the NPC has not / has met the PC before, ARC-WORLD-043); with a party port also WA 0/1 (the NPC is not / is
/// waiting for the PC); with a skill port also PS n and HA n (persuasion and haggle level gates), SK skill n (a level gate)
/// and TR skill n (a training gate), ARC-SKILLS-004.</item>
/// <item>Actions: LF, GF, QU, RU, RQ (quell a rumor, ARC-UI-008), RE and AL (unsigned target or lexically signed delta),
/// negative IN (NPC gives an item), MM; a Player action may end with one JO 0 node or FL node, and with a party port with
/// one UW 0 node; with a skill port TR skill tier (also in an NPC action), and a Player action may end with one ET skill node;
/// with an experience port XP n (the experience of quest experience level n, ARC-DEV-178) in NPC and Player actions,
/// with a fate port FP (one fate point for the PC, ARC-WORLD-054) in NPC and Player actions, without an operand, as every installed
/// one is written (the source reads an operand it does not use; one is outside this subset).</item>
/// </list>
/// With a reputation port (ARC-WORLD-044) RP n is admitted as a condition and as an action of either side.
/// With a script store port (ARC-DEV-152) GV i v, PV i v and PF i v are admitted as conditions (the cell equals v) and as actions of
/// either side (the cell takes v): a global variable 0..1999 (another index is invalid: the source reads past its 2000-word array), a
/// PC variable or flag 0..65535 (the session's stores; a negative index is invalid, a larger one is outside this subset: the source's
/// PC arrays are sparse object fields), any value but a '+'-signed one, and for PF only 0 or 1 (the source's condition never passes
/// another value and its action spreads it over the flag's 32-flag word, ledger GV4; outside this subset).
/// SK, TR and ET read their second operand as the source does (0x4167C0, ledger DS3): spaces before it are optional
/// ("tr4-1") and a missing one is 0; a negative first operand is refused (the source would read it as the second too).
/// Without a party port WA and UW stay unadmitted (<see cref="DialogFailureKind.UnsupportedOperation"/>), as before
/// ARC-WORLD-013.
/// Operations are separated by SP, HTAB, ',', ';', ':', '(' or ')'; inside one operation only SP/HTAB may
/// precede an operand. A field holding only separators is empty. This is project admission policy, not the
/// source's permissive scanner.
/// </summary>
internal static class DialogConversationGrammar
{
    // The source's basic skill numbers of the PS and HA shortcuts.
    private const int HaggleSkill = 9, PersuasionSkill = 11;
    // The PC stores' bound (ARC-DEV-098: the session's SessionScriptLimits.PcIndexes, SA14; the SCR operand packs the index in 16 bits).
    private const int PcIndexes = 1 << 16;
    // The session's rumor numbers (SessionStoryLimits.FirstRumor, LastRumor; the original's sets start at 1000).
    private const int FirstRumor = 1000, LastRumor = 2999;

    internal static IReadOnlyList<ConversationOperation> Parse(DlgRecord record, DlgField field, ConversationField role, DialogConversationOptions options,
        bool party, bool skills = false, bool fate = false, bool reputations = false, bool schematics = false, bool stores = false, bool experience = false)
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
                // A number where an operation should start (an extra operand, or "ha4, -4, lf1 1") is one the source's scanner
                // skips: outside this subset, not damage (ARC-SKILLS-004).
                var kind = bytes[index] is (byte)'$' or (byte)'&' or (byte)'|' or (byte)'!' or (byte)'~' or (byte)'-' or (byte)'+' or (>= (byte)'0' and <= (byte)'9')
                    ? DialogFailureKind.UnsupportedOperation
                    : DialogFailureKind.InvalidOperation;
                throw Failure(kind, record, field, start, 1, operations.Count);
            }
            var opcode = new string([(char)ToUpper(bytes[index]), (char)ToUpper(bytes[index + 1])]);
            index += 2;
            var admitted = role == ConversationField.PlayerCondition
                ? opcode is "LF" or "GF" or "RE" or "RA" or "ME" or "RU" or "RQ" || party && opcode == "WA" || skills && opcode is "PS" or "HA" or "SK" or "TR"
                    || reputations && opcode == "RP" || stores && opcode is "GV" or "PV" or "PF"
                : opcode is "LF" or "GF" or "QU" or "RU" or "RQ" or "RE" or "AL" or "IN" or "MM" || skills && opcode == "TR" || reputations && opcode == "RP"
                    || fate && opcode == "FP" || experience && opcode == "XP" || stores && opcode is "GV" or "PV" or "PF"
                    || role == ConversationField.PlayerAction && (opcode is "JO" or "FL" || party && opcode == "UW" || skills && opcode == "ET"
                        || schematics && opcode == "SU");
            if (!admitted)
            {
                throw Failure(DialogFailureKind.UnsupportedOperation, record, field, start, 2, operations.Count);
            }
            if (operations.Count > 0 && operations[^1] is ConversationJoin or ConversationUnwait or ConversationFinalLine or ConversationExpertTeacher)
            {
                // The source keeps reading operations after JO/UW/FL, but JO/UW enter their node mid-field;
                // operations after them are outside this conservative subset.
                throw Failure(DialogFailureKind.UnsupportedOperation, record, field, start, 2, operations.Count);
            }
            if (opcode == "SU")
            {
                // The source's scanner reads a number after every code and the SU case ignores it (0x415BA0).
                var afterCode = index;
                while (index < bytes.Count && bytes[index] is (byte)' ' or (byte)'\t') index++;
                // atoi: an optional sign and any digits (none reads 0, an overflow is ignored like every value); nothing is checked.
                if (index < bytes.Count && bytes[index] is (byte)'-' or (byte)'+' or (>= (byte)'0' and <= (byte)'9'))
                {
                    if (bytes[index] is (byte)'-' or (byte)'+') index++;
                    while (index < bytes.Count && bytes[index] is >= (byte)'0' and <= (byte)'9') index++;
                }
                else
                    index = afterCode;
                if (index < bytes.Count && !IsSeparator(bytes[index]))
                    throw Failure(DialogFailureKind.InvalidOperation, record, field, start, index - start, operations.Count);
                operations.Add(new ConversationSchematics(Source(record, field, start, index - start, operations.Count)));
                if (operations.Count > options.MaxOperationsPerField)
                    throw new DialogOperationParseException(DialogFailureKind.OperationLimitExceeded, operations[^1].Source);
                SkipSeparators(bytes, ref index);
                continue;
            }
            if (opcode == "FP")
            {
                // No operand: a number after it is outside the subset (the stray-number rule below, or this check).
                if (index < bytes.Count && !IsSeparator(bytes[index]))
                    throw Failure(DialogFailureKind.UnsupportedOperation, record, field, start, index - start + 1, operations.Count);
                operations.Add(new ConversationFatePoint(Source(record, field, start, 2, operations.Count)));
                if (operations.Count > options.MaxOperationsPerField)
                    throw new DialogOperationParseException(DialogFailureKind.OperationLimitExceeded, operations[^1].Source);
                SkipSeparators(bytes, ref index);
                continue;
            }
            SkipOperandSpace(bytes, ref index, record, field, start, operations.Count, required: false);
            if (opcode is "RE" or "AL" && index < bytes.Count && bytes[index] is (byte)'>' or (byte)'<')
            {
                // Source tests these markers after atoi already consumed the value; their intent is not established.
                throw Failure(DialogFailureKind.UnsupportedOperation, record, field, start, index - start + 1, operations.Count);
            }
            var (first, sign) = ParseInteger(bytes, ref index, record, field, start, operations.Count);
            var second = 0;
            if (opcode is "SK" or "TR" or "ET")
            {
                if (sign == '-') throw Failure(DialogFailureKind.InvalidOperation, record, field, start, index - start, operations.Count);
                // The source's second operand (0x4167C0): atoi after the first operand's digits, so the spaces are optional and
                // a missing operand reads 0.
                var afterFirst = index;
                while (index < bytes.Count && bytes[index] is (byte)' ' or (byte)'\t') index++;
                if (!(index < bytes.Count && bytes[index] is (byte)'-' or (byte)'+' or (>= (byte)'0' and <= (byte)'9')))
                {
                    // No second operand: the spaces belong to the separator before the next operation.
                    index = afterFirst;
                }
                else
                {
                    (second, var secondSign) = ParseInteger(bytes, ref index, record, field, start, operations.Count);
                    if (secondSign == '+') throw Failure(DialogFailureKind.InvalidOperation, record, field, start, index - start, operations.Count);
                }
            }
            else if (opcode is "LF" or "GF" or "QU" or "JO" or "UW" or "GV" or "PV" or "PF")
            {
                SkipOperandSpace(bytes, ref index, record, field, start, operations.Count, required: true);
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
                "QU" when first > 0 && second is >= 0 and <= 6 => new ConversationQuest(source, first, second),
                // The conditions (0x4150D0, cases 17 and 18): the original indexes its rumor sets from 1000 without a range test, so a
                // number outside 1000..2999 (the session's rumors) is valid data outside this subset.
                "RU" or "RQ" when role == ConversationField.PlayerCondition && !(first is >= FirstRumor and <= LastRumor or <= -FirstRumor and >= -LastRumor)
                    => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "RU" when role == ConversationField.PlayerCondition => new ConversationRumorGate(source, first),
                "RQ" when role == ConversationField.PlayerCondition => new ConversationQuellGate(source, first),
                "RU" when first >= 1000 => new ConversationRumor(source, first),
                "RQ" when first >= 1000 => new ConversationQuellRumor(source, first),
                "RE" when !reactionAction && first != int.MinValue => new ConversationReactionGate(source, first),
                "RE" when reactionAction && sign == '\0' => new ConversationReaction(source, DialogReactionMode.Target, first),
                "RE" when reactionAction => new ConversationReaction(source, DialogReactionMode.Adjust, first),
                "AL" when sign == '\0' => new ConversationAlignment(source, DialogReactionMode.Target, first),
                "AL" => new ConversationAlignment(source, DialogReactionMode.Adjust, first),
                "RA" when first != int.MinValue => new ConversationRaceGate(source, first),
                "RP" when first != int.MinValue => new ConversationReputation(source, first),
                "IN" when first < 0 && first != int.MinValue => new ConversationGiveItem(source, -first),
                "IN" => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "MM" when first >= 0 => new ConversationMarkArea(source, first),
                // The source gives quest_get_xp of any operand (0x415BA0, DAC9); a level without an entry gives nothing.
                "XP" when first != int.MinValue => new ConversationExperience(source, first),
                "JO" when first == 0 && second > 0 => new ConversationJoin(source, second),
                "JO" when second > 0 => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "UW" when first == 0 && second > 0 => new ConversationUnwait(source, second),
                "UW" when second > 0 => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "WA" when first is 0 or 1 => new ConversationWaiting(source, first == 1),
                // The original passes any other ME operand without a test (0x4150D0): valid data outside this subset.
                "ME" when first is 0 or 1 => new ConversationMetBefore(source, first == 1),
                "ME" => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "FL" when first > 0 => new ConversationFinalLine(source, first),
                "GV" when first is >= 0 and < Arcanum.Data.Scripts.ScrCatalogue.GlobalVariables => new ConversationScriptStore(source, DialogScriptStore.GlobalVariable, first, second),
                "PV" when first is >= 0 and < PcIndexes => new ConversationScriptStore(source, DialogScriptStore.PcVariable, first, second),
                "PF" when first is >= 0 and < PcIndexes && second is 0 or 1 => new ConversationScriptStore(source, DialogScriptStore.PcFlag, first, second),
                "PF" when first is >= 0 and < PcIndexes => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                // A PC index beyond the session's 16-bit stores is valid data outside this subset (the source's PC arrays are sparse fields).
                "PV" or "PF" when first >= PcIndexes => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "PS" when first != int.MinValue => new ConversationSkillGate(source, PersuasionSkill, first, false),
                "HA" when first != int.MinValue => new ConversationSkillGate(source, HaggleSkill, first, false),
                "SK" when second != int.MinValue => new ConversationSkillGate(source, first, second, false),
                "TR" when role == ConversationField.PlayerCondition && second != int.MinValue => new ConversationSkillGate(source, first, second, true),
                "TR" when role != ConversationField.PlayerCondition => new ConversationSkillTraining(source, first, second),
                "ET" when second > 0 => new ConversationExpertTeacher(source, first, second),
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

    /// <summary>
    /// Inside one operation only SP/HTAB may precede an operand: the source scanner reads each operand with
    /// whitespace-skipping atoi, so `GF 5,1` would read 0 there. Such forms are unsupported, never reinterpreted.
    /// </summary>
    private static void SkipOperandSpace(IReadOnlyList<byte> bytes, ref int index, DlgRecord record, DlgField field, int start, int ordinal, bool required)
    {
        var before = index;
        while (index < bytes.Count && bytes[index] is (byte)' ' or (byte)'\t')
        {
            index++;
        }
        if (index < bytes.Count && IsSeparator(bytes[index]))
        {
            throw Failure(DialogFailureKind.UnsupportedOperation, record, field, start, index - start + 1, ordinal);
        }
        if (required && before == index)
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

    internal static bool IsSeparator(byte value) => value is (byte)' ' or (byte)'\t' or (byte)',' or (byte)';' or (byte)':' or (byte)'(' or (byte)')';
    private static bool IsAsciiLetter(byte value) => value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';
    private static byte ToUpper(byte value) => value is >= (byte)'a' and <= (byte)'z' ? (byte)(value - 32) : value;

    private static DialogOperationSource Source(DlgRecord record, DlgField field, int offset, int length, int ordinal) =>
        new(new DialogNodeId(record.Id), record.SerializedIndex, field.Kind, field.PayloadOffset, offset, length, ordinal);

    private static DialogOperationParseException Failure(DialogFailureKind kind, DlgRecord record, DlgField field, int offset, int length, int ordinal) =>
        new(kind, Source(record, field, offset, length, ordinal));
}
