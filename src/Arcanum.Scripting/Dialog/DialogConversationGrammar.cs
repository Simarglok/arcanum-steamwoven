using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

internal abstract record ConversationOperation(DialogOperationSource Source);
/// <summary>LC (ARC-DEV-196): the NPC's dialog-point counter <c>Index</c> equals <c>Value</c> as a condition; as an action it takes the value's low eight bits.</summary>
internal sealed record ConversationCounter(DialogOperationSource Source, int Index, int Value) : ConversationOperation(Source);
/// <summary>SC as a condition (ARC-DEV-197): the PC's rank in a college against the threshold: above 0 it is a minimum, 0 or below a maximum of its negation (both inclusive).</summary>
internal sealed record ConversationCollegeGate(DialogOperationSource Source, int College, int Threshold) : ConversationOperation(Source);
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
internal sealed record ConversationTakeItem(DialogOperationSource Source, int ItemName) : ConversationOperation(Source);
internal sealed record ConversationMarkArea(DialogOperationSource Source, int Area) : ConversationOperation(Source);
internal sealed record ConversationJoin(DialogOperationSource Source, int Node) : ConversationOperation(Source);
internal sealed record ConversationFinalLine(DialogOperationSource Source, int Node) : ConversationOperation(Source);
/// <summary>FO and WT (ARC-DEV-195): the NPC's leader is the PC, or the NPC is jilted; <c>Value</c> is the asked state.</summary>
internal sealed record ConversationCompanionGate(DialogOperationSource Source, DialogCompanionFact Fact, bool Value) : ConversationOperation(Source);
/// <summary>WA, LV, SO and SC as actions (ARC-DEV-195): they read no operand.</summary>
internal sealed record ConversationCompanionAction(DialogOperationSource Source, DialogCompanionAction Action) : ConversationOperation(Source);
internal sealed record ConversationWaiting(DialogOperationSource Source, bool Value) : ConversationOperation(Source);
internal sealed record ConversationUnwait(DialogOperationSource Source, int Node) : ConversationOperation(Source);
/// <summary>PS, HA, SK (a level gate) and TR (a training gate): a non-negative threshold is a minimum, a negative one a maximum
/// of its absolute value (both inclusive).</summary>
internal sealed record ConversationSkillGate(DialogOperationSource Source, int Skill, int Threshold, bool Training) : ConversationOperation(Source);
internal sealed record ConversationSkillTraining(DialogOperationSource Source, int Skill, int Training) : ConversationOperation(Source);
internal sealed record ConversationExpertTeacher(DialogOperationSource Source, int Skill, int Node) : ConversationOperation(Source);
/// <summary>ARC-TECHNOLOGY-006: SU, the schematic window with the NPC as crafter (ledger NC1).</summary>
internal sealed record ConversationSchematics(DialogOperationSource Source) : ConversationOperation(Source);
/// <summary>Which rule a PC stat condition applies (ledger DCC2, DCC3): the signed rule on the stat, on the stat negated (<c>ta</c>), or the alignment's odd <c>na</c> form.</summary>
internal enum ConversationStatRule { Signed, Negated, AlignmentNegatedOperand }

/// <summary>CH, PE, LE, AL, MA, TA or NA as a condition (ARC-DEV-192, ledger DCC2–DCC3): the PC's stat <paramref name="Stat"/> (the original's stat number) against the operand.</summary>
internal sealed record ConversationStatGate(DialogOperationSource Source, int Stat, int Threshold, ConversationStatRule Rule) : ConversationOperation(Source);
/// <summary>ARC-DEV-194: <c>in</c> / <c>ni</c> (0x4150D0 cases 11 and 23): <see cref="Name"/> is the item name asked for, <see cref="NpcSide"/> when the operand was negative (the NPC and its substitute container,
/// otherwise the PC and its followers), <see cref="Negated"/> for <c>ni</c>.</summary>
internal sealed record ConversationItemGate(DialogOperationSource Source, int Name, bool NpcSide, bool Negated) : ConversationOperation(Source);
/// <summary>SS n as a condition (ARC-DEV-191, ledger DCC7, SS1): the signed rule on the game's story state, n ≥ 0 holds at state ≥ n, n &lt; 0 at state ≤ −n.</summary>
internal sealed record ConversationStoryGate(DialogOperationSource Source, int Threshold) : ConversationOperation(Source);
/// <summary>SS n as an action (ARC-DEV-191, DAC7, SS1): the story state rises to n when n is larger (the setter keeps the larger value).</summary>
internal sealed record ConversationStoryState(DialogOperationSource Source, int Value) : ConversationOperation(Source);
/// <summary>AR n as a condition (ARC-DEV-191, DCC7, WT2): area <paramref name="Area"/> (1..4095) is known to the PC (n &gt; 0) or is not known (n ≤ 0, area −n).</summary>
internal sealed record ConversationKnownAreaGate(DialogOperationSource Source, int Area, bool MustBeKnown) : ConversationOperation(Source);
/// <summary>IA n as a condition (ARC-DEV-191, DCC7, GA2): the area of the PC's map equals n (n &gt; 0) or differs from −n (n ≤ 0).</summary>
internal sealed record ConversationMapAreaGate(DialogOperationSource Source, int Area, bool MustEqual) : ConversationOperation(Source);
/// <summary>ARC-DEV-190: CO, start combat (ledger SU13, DAC1, CA1–CA2): it only sets a flag; after the whole action string the attack event runs with the PC as attacker on the NPC.</summary>
internal sealed record ConversationCombat(DialogOperationSource Source) : ConversationOperation(Source);
internal sealed record ConversationMetBefore(DialogOperationSource Source, bool Value) : ConversationOperation(Source);
/// <summary>How a quest log condition compares the PC's quest state with its operand (ledger DCC4).</summary>
internal enum ConversationQuestCompare { Equal, AtLeast, AtMost }

/// <summary>QU, QA or QB as a condition (ARC-DEV-180, ledger DCC4): the PC's state of the quest equals, is at least or is at most the state.</summary>
internal sealed record ConversationQuestGate(DialogOperationSource Source, int QuestId, int State, ConversationQuestCompare Compare) : ConversationOperation(Source);
/// <summary>RP n (ARC-WORLD-044): as a condition n > 0 holds n, n ≤ 0 lacks −n; as an action n > 0 adds n, n ≤ 0 removes −n.</summary>
/// <summary>$$ n as a condition (ARC-DEV-179, ledger DCC5): n ≥ 0 holds when the gold of the PC and its followers is at least n, n &lt; 0 when it is at most −n.</summary>
internal sealed record ConversationGoldGate(DialogOperationSource Source, int Threshold) : ConversationOperation(Source);
/// <summary>$$ n as an action (ARC-DEV-179, ledger DAC2): n &gt; 0 gives the PC n gold, n &lt; 0 makes the PC (and its followers) pay −n to the NPC.</summary>
internal sealed record ConversationGold(DialogOperationSource Source, int Amount) : ConversationOperation(Source);
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
/// with a gold port the money code $$ n (ARC-DEV-179) as a condition (the gold of the PC and its followers, signed rule) and as an action (n &gt; 0
/// gives the PC n gold, n &lt; 0 makes the PC and its followers pay −n to the NPC),
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
    // The session's quest numbers (SessionStoryLimits.FirstQuest, LastQuest; the global state table starts at 1000, 0x4C51A0).
    private const int FirstQuest = 1000, LastQuest = 1999;
    // The session's areas (SessionStoryLimits.LastArea; the PC's known-area table of the original has a byte per area with no range test).
    private const int LastArea = 4095;
    // The original's stat numbers (the getter 0x4B0490; ledger DCC2, DCC3, GS1).
    // The spell colleges of the original (SpellBookRules.Colleges; the rank getter 0x4B1AB0 indexes the PC's rank array).
    private const int Colleges = 16;
    private const int StatPerception = 5, StatCharisma = 7, StatAptitude = 16, StatLevel = 17, StatAlignment = 19;

    internal static IReadOnlyList<ConversationOperation> Parse(DlgRecord record, DlgField field, ConversationField role, DialogConversationOptions options,
        bool party, bool skills = false, bool fate = false, bool reputations = false, bool schematics = false, bool stores = false, bool experience = false, bool gold = false, bool quests = false, bool combat = false, bool story = false, bool areas = false, bool mapArea = false, bool stats = false, bool items = false, bool itemTake = false, bool companion = false, bool counters = false, bool colleges = false)
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
            // The money code (ARC-DEV-179): the source's scanner takes '$' as a code letter (DS1), so "$$" is a two-character code.
            var money = index + 2 <= bytes.Count && bytes[index] == (byte)'$' && bytes[index + 1] == (byte)'$';
            if (!money && (index + 2 > bytes.Count || !IsAsciiLetter(bytes[index]) || !IsAsciiLetter(bytes[index + 1])))
            {
                // A number where an operation should start (an extra operand, or "ha4, -4, lf1 1") is one the source's scanner
                // skips: outside this subset, not damage (ARC-SKILLS-004).
                var kind = bytes[index] is (byte)'$' or (byte)'&' or (byte)'|' or (byte)'!' or (byte)'~' or (byte)'-' or (byte)'+' or (>= (byte)'0' and <= (byte)'9')
                    ? DialogFailureKind.UnsupportedOperation
                    : DialogFailureKind.InvalidOperation;
                throw Failure(kind, record, field, start, 1, operations.Count);
            }
            var opcode = money ? "$$" : new string([(char)ToUpper(bytes[index]), (char)ToUpper(bytes[index + 1])]);
            index += 2;
            var admitted = role == ConversationField.PlayerCondition
                ? opcode is "LF" or "GF" or "RE" or "RA" or "ME" or "RU" or "RQ" || party && opcode == "WA" || skills && opcode is "PS" or "HA" or "SK" or "TR"
                    || reputations && opcode == "RP" || stores && opcode is "GV" or "PV" or "PF" || gold && opcode == "$$"
                    || quests && opcode is "QU" or "QA" or "QB" || story && opcode == "SS" || areas && opcode == "AR" || mapArea && opcode == "IA"
                    || stats && opcode is "CH" or "PE" or "LE" or "AL" or "MA" or "TA" or "NA" || items && opcode is "IN" or "NI" || companion && opcode is "FO" or "WT" || counters && opcode == "LC" || colleges && opcode == "SC"
                : opcode is "LF" or "GF" or "QU" or "RU" or "RQ" or "RE" or "AL" or "IN" or "MM" || skills && opcode == "TR" || reputations && opcode == "RP"
                    || fate && opcode == "FP" || experience && opcode == "XP" || gold && opcode == "$$" || stores && opcode is "GV" or "PV" or "PF"
                    || combat && opcode == "CO" || story && opcode == "SS" || companion && opcode is "WA" or "LV" or "SO" or "SC" || counters && opcode == "LC"
                    || role == ConversationField.PlayerAction && (opcode is "JO" or "FL" || party && opcode == "UW" || skills && opcode == "ET"
                        || schematics && opcode == "SU");
            if (!admitted)
            {
                throw Failure(DialogFailureKind.UnsupportedOperation, record, field, start, 2, operations.Count);
            }
            if (operations.Count > 0 && operations.Any(operation => operation is ConversationJoin or ConversationUnwait or ConversationFinalLine or ConversationExpertTeacher)
                // ARC-DEV-190: CO after an FL is the one tail the subset keeps ("fl 12, co": the attack event runs after the final line, CA2).
                && !(opcode == "CO" && operations.Last(operation => operation is ConversationJoin or ConversationUnwait or ConversationFinalLine or ConversationExpertTeacher) is ConversationFinalLine))
            {
                // The source keeps reading operations after JO/UW/FL, but JO/UW enter their node mid-field;
                // operations after them are outside this conservative subset.
                throw Failure(DialogFailureKind.UnsupportedOperation, record, field, start, 2, operations.Count);
            }
            if (opcode is "JO" or "UW" or "ET" && operations.Any(operation => operation is ConversationCombat))
            {
                // ARC-DEV-190: a co before a join, an unwait or an expert teacher is outside the subset (their node may be spoken inside the string, only FL was read).
                throw Failure(DialogFailureKind.UnsupportedOperation, record, field, start, 2, operations.Count);
            }
            if (opcode is "SU" or "CO")
            {
                // The source's scanner reads a number after every code and the SU and CO cases ignore it (0x415BA0).
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
                var operationSource = Source(record, field, start, index - start, operations.Count);
                operations.Add(opcode == "SU" ? new ConversationSchematics(operationSource) : new ConversationCombat(operationSource));
                if (operations.Count > options.MaxOperationsPerField)
                    throw new DialogOperationParseException(DialogFailureKind.OperationLimitExceeded, operations[^1].Source);
                SkipSeparators(bytes, ref index);
                continue;
            }
            if (opcode == "FP" || role != ConversationField.PlayerCondition && opcode is "WA" or "LV" or "SO" or "SC")
            {
                // No operand: a number after it is outside the subset (the stray-number rule below, or this check).
                if (index < bytes.Count && !IsSeparator(bytes[index]))
                    throw Failure(DialogFailureKind.UnsupportedOperation, record, field, start, index - start + 1, operations.Count);
                var noOperand = Source(record, field, start, 2, operations.Count);
                operations.Add(opcode switch
                {
                    "FP" => new ConversationFatePoint(noOperand),
                    "WA" => new ConversationCompanionAction(noOperand, DialogCompanionAction.Wait),
                    "LV" => new ConversationCompanionAction(noOperand, DialogCompanionAction.Leave),
                    "SO" => new ConversationCompanionAction(noOperand, DialogCompanionAction.SpreadOut),
                    _ => new ConversationCompanionAction(noOperand, DialogCompanionAction.SpreadClear),
                });
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
            if (opcode is "SK" or "TR" or "ET" || opcode is "QU" or "QA" or "QB" or "SC" && role == ConversationField.PlayerCondition)
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
            else if (opcode is "LF" or "LC" or "GF" or "QU" or "JO" or "UW" or "GV" or "PV" or "PF")
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
                // The college rank condition (0x4150D0 case 36, CK1): the college is one of the sixteen; a number beyond is valid data outside this subset. The second operand reads 0 when missing, as sk's does.
                "SC" when role == ConversationField.PlayerCondition && first is >= 0 and < Colleges && second != int.MinValue => new ConversationCollegeGate(source, first, second),
                "SC" when role == ConversationField.PlayerCondition && second != int.MinValue => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                // The NPC counters (0x4150D0 case 14, 0x415BA0 case 11, KC1): four byte counters; an index outside 0..3 wraps in the source and is valid data outside this subset.
                "LC" when first is >= 0 and <= 3 => new ConversationCounter(source, first, second),
                "LC" => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "GF" when first >= 0 && second is 0 or 1 => new ConversationGlobalFlag(source, first, second == 1),
                // The conditions (0x4152BB, 0x415720, 0x415831): the quest state getter indexes its tables from 1000 without a range test, so a
                // number outside the session's quests is valid data outside this subset.
                "QU" or "QA" or "QB" when role == ConversationField.PlayerCondition && first is < FirstQuest or > LastQuest => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "QU" or "QA" or "QB" when role == ConversationField.PlayerCondition => new ConversationQuestGate(source, first, second,
                    opcode == "QU" ? ConversationQuestCompare.Equal : opcode == "QA" ? ConversationQuestCompare.AtLeast : ConversationQuestCompare.AtMost),
                // The story state conditions and actions (0x4158FF, 0x4162D9): any number; int.MinValue has no negation (the source's neg keeps it) and is damage here.
                "SS" when first != int.MinValue && role == ConversationField.PlayerCondition => new ConversationStoryGate(source, first),
                "SS" when first != int.MinValue => new ConversationStoryState(source, first),
                // The known areas (0x4159FE → 0x4CAF50) index a byte per area without a range test: a number outside the session's areas 1..4095 (area 0 included) is valid data outside this subset.
                "AR" when role == ConversationField.PlayerCondition && Math.Abs((long)first) is >= 1 and <= LastArea => new ConversationKnownAreaGate(source, Math.Abs(first), first > 0),
                "AR" when role == ConversationField.PlayerCondition => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "IA" when role == ConversationField.PlayerCondition && first != int.MinValue => new ConversationMapAreaGate(source, Math.Abs(first), first > 0),
                // The PC stat conditions (0x4150D0 cases 1-5, 20, 32; stat getter 0x4B0490): int.MinValue has no negation (the source's neg keeps it) and is damage here.
                "CH" or "PE" or "LE" or "AL" or "MA" or "TA" or "NA" when role == ConversationField.PlayerCondition && first != int.MinValue && sign == '+'
                    => throw new DialogOperationParseException(DialogFailureKind.InvalidOperation, source),
                "CH" or "PE" or "LE" or "AL" or "MA" or "TA" or "NA" when role == ConversationField.PlayerCondition && first != int.MinValue => new ConversationStatGate(source,
                    opcode switch { "CH" => StatCharisma, "PE" => StatPerception, "LE" => StatLevel, "AL" or "NA" => StatAlignment, _ => StatAptitude }, first,
                    opcode == "TA" ? ConversationStatRule.Negated : opcode == "NA" ? ConversationStatRule.AlignmentNegatedOperand : ConversationStatRule.Signed),
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
                "AL" when role == ConversationField.PlayerCondition => throw new DialogOperationParseException(DialogFailureKind.InvalidOperation, source),
                "AL" when sign == '\0' => new ConversationAlignment(source, DialogReactionMode.Target, first),
                "AL" => new ConversationAlignment(source, DialogReactionMode.Adjust, first),
                "RA" when first != int.MinValue => new ConversationRaceGate(source, first),
                "RP" when first != int.MinValue => new ConversationReputation(source, first),
                // The money code (0x4150D0 case 10, 0x415BA0 case 0, DCC5, DAC2): any operand; int.MinValue has no negation (the source's neg
                // keeps it) and is damage here.
                "$$" when first != int.MinValue && role == ConversationField.PlayerCondition => new ConversationGoldGate(source, first),
                "$$" when first != int.MinValue => new ConversationGold(source, first),
                // The item conditions (0x4150D0 cases 11 and 23): int.MinValue has no negation (the source's neg keeps it) and is damage here, as a '+' sign is.
                "IN" or "NI" when role == ConversationField.PlayerCondition && (first == int.MinValue || sign == '+')
                    => throw new DialogOperationParseException(DialogFailureKind.InvalidOperation, source),
                "IN" or "NI" when role == ConversationField.PlayerCondition => new ConversationItemGate(source, Math.Abs(first), first < 0, opcode == "NI"),
                "IN" when first < 0 && first != int.MinValue => new ConversationGiveItem(source, -first),
                // The non-negative IN action (0x415BA0 case 9, DAC5): the PC hands the NPC an item by name.
                "IN" when first >= 0 && itemTake => new ConversationTakeItem(source, first),
                "IN" => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "MM" when first >= 0 => new ConversationMarkArea(source, first),
                // The source gives quest_get_xp of any operand (0x415BA0, DAC9); a level without an entry gives nothing.
                "XP" when first != int.MinValue => new ConversationExperience(source, first),
                "JO" when first == 0 && second > 0 => new ConversationJoin(source, second),
                "JO" when second > 0 => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "UW" when first == 0 && second > 0 => new ConversationUnwait(source, second),
                "UW" when second > 0 => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
                "WA" when first is 0 or 1 => new ConversationWaiting(source, first == 1),
                // FO and WT (0x4150D0 cases 19 and 29, DCC8): the source passes any other operand without a test (valid data outside this subset), as it does for ME.
                "FO" or "WT" when role == ConversationField.PlayerCondition && first is 0 or 1 => new ConversationCompanionGate(source, opcode == "FO" ? DialogCompanionFact.LedByPc : DialogCompanionFact.Jilted, first == 1),
                "FO" or "WT" when role == ConversationField.PlayerCondition => throw new DialogOperationParseException(DialogFailureKind.UnsupportedOperation, source),
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
