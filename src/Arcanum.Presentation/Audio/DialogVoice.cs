namespace Arcanum.Presentation.Audio;

public enum DialogVoiceCommandKind
{
    /// <summary>Stop the voice that plays, if any.</summary>
    Stop,
    /// <summary>Stop the voice that plays, then play the line's voice once (<see cref="DialogVoiceCommand.Identity"/>).</summary>
    Play,
}

public sealed record DialogVoiceCommand(DialogVoiceCommandKind Kind, int Identity = -1);

/// <summary>
/// ARC-UI-043: when the dialogue voice starts and stops, as the original does (docs/audio/dialogue-voice.md; checked in the
/// 1.0.7.4 executable, docs/research/audio-evidence.md SN22). Each NPC line shown plays its voice once; a line without a
/// voice (identity −1) leaves the previous voice playing; every reply the PC picks stops the voice, whatever it does to the
/// dialog (<see cref="Picked"/>). Closing the dialog does not stop it. The host calls <see cref="Update"/> every frame with
/// the open conversation, its turn and the shown line's voice identity. Engine-free.
/// </summary>
public sealed class DialogVoicePlayer
{
    private object? _conversation;
    private int _turn;
    private bool _spoken;

    /// <summary>The PC picked a reply: the original stops the voice before it processes the reply, for every kind of reply
    /// (another line, the end of the dialog, a return to the script).</summary>
    [OriginalFunction(0x567E30, OriginalFunctionScope.Partial)]
    public static DialogVoiceCommand Picked() => new(DialogVoiceCommandKind.Stop);

    /// <param name="conversation">The open conversation (any reference that changes with a new conversation).</param>
    /// <param name="turn">The conversation's turn: a new turn shows a new NPC line.</param>
    /// <param name="identity">The voice identity of the NPC line shown in this turn (−1: none).</param>
    public IReadOnlyList<DialogVoiceCommand> Update(object conversation, int turn, int identity)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (!ReferenceEquals(conversation, _conversation)) { _conversation = conversation; _turn = turn; _spoken = false; }
        else if (turn != _turn) { _turn = turn; _spoken = false; }
        if (_spoken) return [];
        _spoken = true;
        return identity == -1 ? [] : [new(DialogVoiceCommandKind.Play, identity)];
    }

    /// <summary>Forgets the conversation (the dialog closed); a voice still playing is left alone.</summary>
    public void Release() { _conversation = null; _spoken = false; }
}
