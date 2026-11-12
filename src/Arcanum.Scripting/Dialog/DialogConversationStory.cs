using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

/// <summary>
/// ARC-SCRIPT-1321 (ledger STY1–STY4): the talk of a Player <c>c:</c> reply, the original's story line. Picking the reply (state 0x18 of 0x414810, 0x41A3E0) runs its
/// action, then the NPC's line is the one the current story state holds (0x41A440: the NPC's own override record 10000 + state, else the story file's entry the NPC's
/// race + 100 × state) and the PC's class line 1000 is the only way on, with the reply's own response. The talk reuses the trainer's screens
/// (<see cref="TrainerTalk"/>): the original overlays the dialog state the same way.
/// </summary>
public sealed partial class DialogConversation
{
    // The story state the lines are numbered by: far beyond anything the story reaches, so the line numbers stay inside an int.
    private const int MaxStoryState = 1_000_000;

    private bool StoryAvailable => _ports.Story is not null && _ports.Greeting is not null;

    // The game's story state (0x445090); a throwing port or a state outside 0..MaxStoryState (the session never holds one) fails the conversation.
    private bool ReadStoryState(out int state)
    {
        state = 0;
        try
        {
            state = _ports.Story!.State();
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, _talk?.Source));
            return false;
        }
        if (state is < 0 or > MaxStoryState)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, _talk?.Source));
            return false;
        }
        return true;
    }

    /// <summary>The <c>c:</c> reply was picked and its action has run: the story line of the story state now is told (ledger STY2, STY3).</summary>
    [OriginalFunction(0x41A3E0, OriginalFunctionScope.Partial)]
    private void OpenStory(OfferPlan plan)
    {
        var record = plan.Reply.Record;
        var source = new DialogOperationSource(new DialogNodeId(record.Id), record.SerializedIndex, DlgFieldKind.Text, record.Text.PayloadOffset, 0, 2, 0);
        _talk = new TrainerTalk(plan.Reply, plan.Target, source);
        if (!ReadStoryState(out var state)) return;
        if (!GenerateTalkLine((facts, sources, port) => DialogGreeting.StoryTale(state, facts, sources, port, _request.Names, _request.Encoding, _options.MaxTextBytes),
                out var text, out var origin))
        {
            return;
        }
        // The only way on is the PC's class line 1000 (0x418390), which follows the reply's own response (0x417590).
        if (!PcTalkLine(1000, 0, classLine: true, out var answer)) return;
        Present(new DialogNpcLine(CurrentNode, text, origin), [answer with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) }]);
    }
}
