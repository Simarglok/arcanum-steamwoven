using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

/// <summary>
/// ARC-SCRIPT-1532 (ledger DQR1–DQR5): the Player reply <c>q:N</c>, the quest-state reply. The reply builder (0x416C10, prefix <c>q:</c> either case) reads the number after the
/// marker as the quest, asks the chooser 0x4C4C00 for a DLG record id and builds the option from that record (a recursive call of the builder: its text, response and action; the <c>q:</c>
/// record itself only carries the gender, IQ and condition gates the source tested before). The chooser reads the PC's quest state 0..6 (0x4C4CB0, the botched mark counting 6) and
/// the quest's row of <c>rules\gamequest.mes</c> (0x4C4860): field 16 + state when the PC's Intelligence is below 5, else 9 + state when the NPC's reaction toward the PC is below 20, else 2 +
/// state. A chosen id of -1 drops the reply (the builder returns 0); a chosen record that cannot be built leaves the option blocked, as for any reply whose parts are outside the subset.
/// The choice depends on the PC's state at the moment, so it is made when the offers are built, not when the step is planned.
/// </summary>
public sealed partial class DialogConversation
{
    private const int QuestReplyNormalGroup = 2, QuestReplyLowReactionGroup = 9, QuestReplyLowIntelligenceGroup = 16, QuestReplyStates = 7;
    private const int MaxQuestChain = 8;
    private const int QuestReplyReactionLimit = 20, QuestReplyIntelligenceLimit = 5, FirstQuestNumber = 1000, LastQuestNumber = 1999;

    private bool QuestReplyAvailable => _ports.Quests is not null && _ports.QuestReplies is not null && _ports.World is not null;

    /// <summary>The quest of a <c>q:</c> reply text: digits only (spaces around allowed) within 1000..1999, the range the table filler reads; anything else is not admitted.</summary>
    private static int? ParseQuestReply(string text)
    {
        if (text.Length < 3) return null;
        var number = text[2..].Trim(' ');
        if (number.Length is < 1 or > 4 || !number.All(char.IsAsciiDigit)) return null;
        var quest = int.Parse(number, System.Globalization.CultureInfo.InvariantCulture);
        return quest is >= FirstQuestNumber and <= LastQuestNumber ? quest : null;
    }

    /// <summary>
    /// Chooses the record of a <c>q:</c> reply and plans it as the reply: <paramref name="resolved"/> is the reply plan to offer (kept under the <c>q:</c> record's identity), null
    /// when the original drops the reply. A chosen record that is itself a <c>q:</c> reply is chosen through in turn, as the builder's recursion does (its own gates are not tested; a
    /// loop or a chain beyond <see cref="MaxQuestChain"/> leaves the option blocked). False when the conversation stopped or failed (a broken port result, an unknown reaction).
    /// </summary>
    [OriginalFunction(0x4C4C00, OriginalFunctionScope.Partial)]
    private bool ResolveQuestReply(ReplyPlan planned, StepPlan step, out ReplyPlan? resolved)
    {
        resolved = null;
        var quest = planned.QuestReply!.Value;
        var record = planned.Record;
        var source = new DialogOperationSource(new DialogNodeId(record.Id), record.SerializedIndex, DlgFieldKind.Text, record.Text.PayloadOffset, 0, 2, 0);
        var visited = new HashSet<int> { record.Id };
        for (var depth = 0; depth < MaxQuestChain; depth++)
        {
            int state;
            try
            {
                state = _ports.Quests!.State(new DialogQuestQuery(_request.Participants.PlayerCharacter, quest));
            }
            catch (Exception)
            {
                Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, source));
                return false;
            }
            if (state is < 0 or >= QuestReplyStates)
            {
                Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, source));
                return false;
            }
            // The Intelligence is asked first and the reaction only for a PC that is not dull (0x4C4C00).
            int group;
            if (_request.Pc.Intelligence < QuestReplyIntelligenceLimit)
            {
                group = QuestReplyLowIntelligenceGroup;
            }
            else
            {
                if (ReadReactionValue(source) is not { } reaction)
                {
                    return false;
                }
                group = reaction < QuestReplyReactionLimit ? QuestReplyLowReactionGroup : QuestReplyNormalGroup;
            }
            int? chosen;
            try
            {
                chosen = _ports.QuestReplies!.Field(quest, group + state);
            }
            catch (Exception)
            {
                Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, source));
                return false;
            }
            if (chosen is not { } id)
            {
                resolved = BlockedQuestReply(planned, DialogOptionBlockKind.UnsupportedInteraction, source);
                return true;
            }
            if (id == -1)
            {
                return true;
            }
            if (!_document.TryGetRecord(id, out var target) || target is null || target.Role != DlgRecordRole.Player || !visited.Add(id))
            {
                resolved = BlockedQuestReply(planned, DialogOptionBlockKind.TargetInvalid, source);
                return true;
            }
            if (DialogText.Decode(target.Text.Bytes, _request.Encoding, _options.MaxTextBytes, out var targetText) == DialogTextFailureKind.None
                && DialogText.Marker(targetText, out _) is { Letter: 'q' } && ParseQuestReply(targetText) is { } next)
            {
                quest = next;
                continue;
            }
            var plan = PlanReply(target, new DialogNodeId(step.Npc.Id), out var frontier, out var failure, gates: record, nestedQuest: true);
            resolved = plan is null
                ? BlockedQuestReply(planned, frontier is not null ? DialogOptionBlockKind.UnsupportedInteraction : DialogOptionBlockKind.InvalidOperation, frontier?.Source ?? failure?.Source ?? source)
                : plan with { Record = record };
            return true;
        }
        resolved = BlockedQuestReply(planned, DialogOptionBlockKind.TargetInvalid, source);
        return true;
    }

    private static ReplyPlan BlockedQuestReply(ReplyPlan planned, DialogOptionBlockKind kind, DialogOperationSource? source) =>
        planned with
        {
            TextKind = DialogOptionTextKind.Generated,
            Marker = new DialogGeneratedMarker('q'),
            Transition = DialogOptionTransition.Unsupported,
            Block = new DialogOptionBlock(kind, source),
            QuestReply = null,
        };
}
