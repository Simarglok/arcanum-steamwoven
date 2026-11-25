using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Session;

namespace Arcanum.World;

/// <summary>
/// ARC-SCRIPT-2965 (FR-SCRIPT-002; ledger FL4, FL6, FL7): a float line's DLG record whose action field holds a real operation. The original's NPC-line step
/// runs the action string between the PC and the floated object with no dialog open, then builds the text; the line floats whatever the
/// string did. <see cref="AreaScriptHost"/> plans the step before anything of the run applies (<see cref="DialogConversation.PrepareNpcLine"/>, no port
/// call) and runs the strings with the run's other effects. The operations a string may hold here are those that need no open conversation
/// (<c>lf</c>, <c>lc</c>, <c>gf</c>, <c>ru</c>, <c>qr</c>, <c>gv</c>/<c>pv</c>/<c>pf</c>, <c>ss</c>); any other leaves the run unapplied.
/// </summary>
/// <param name="Speaker">The floated object (the dialog's NPC); its dialog slot is the one <c>lf</c> and <c>lc</c> write.</param>
/// <param name="Conversation">The planned step.</param>
/// <param name="Text">The line's text.</param>
internal sealed record AreaScriptFloatAction(AreaSpeaker Speaker, DialogConversation Conversation, string Text);

/// <summary>
/// The ports a float line's action string runs on: the dialog state of the speakers (<see cref="AreaConversationState"/>) with
/// <c>lf</c> and <c>lc</c> written only where the NPC has a dialog-point (9) script attached, as the original does (they do nothing without one; ledger FL6).
/// </summary>
internal sealed class AreaFloatActionPorts(AreaConversationState state, Func<DialogParticipantId, bool> hasDialogScript) : IDialogCommandPort, IDialogCounterPort
{
    public DialogCommandResult Execute(LocalFlagWriteCommand command) => hasDialogScript(command.Npc) ? state.Execute(command) : DialogCommandResult.Applied;

    public DialogCommandResult Execute(QuestStateCommand command) => state.Execute(command);

    public DialogCommandResult Execute(RumorLogCommand command) => state.Execute(command);

    public int Read(DialogCounterQuery query) => state.Read(query);

    public DialogCommandResult Execute(CounterWriteCommand command) => hasDialogScript(command.Npc) ? state.Execute(command) : DialogCommandResult.Applied;
}
