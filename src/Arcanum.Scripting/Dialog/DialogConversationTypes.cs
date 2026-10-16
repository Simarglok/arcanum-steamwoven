using Arcanum.Scripting.GlobalFlags;

namespace Arcanum.Scripting.Dialog;

/// <summary>PC gender in the DLG selector domain: Player gender field 0 is female, 1 is male, empty is any.</summary>
public enum DialogPcGender
{
    Female = 0,
    Male = 1,
}

/// <summary>
/// Caller-owned PC facts for reply eligibility. They are not read from a character owner by this runtime.
/// <paramref name="IsDumb"/> lowers an intelligence above the low-intelligence threshold (4) to 1.
/// <paramref name="Race"/> is the zero-based source race index; DLG RA selectors are one-based.
/// </summary>
public sealed record DialogPcFacts(DialogPcGender Gender, int Intelligence, bool IsDumb, int Race);

/// <summary>SCR line of the caller that opened the dialog; a plain Exit continues at the next line.</summary>
public readonly record struct DialogScriptLine(int Value);

public sealed record DialogConversationRequest(
    DialogNodeId Entry,
    DialogParticipants Participants,
    DialogScriptLine CallerLine,
    DialogPcFacts Pc,
    DialogSpeakerNames Names,
    DialogTextEncoding Encoding = DialogTextEncoding.Windows1252);

/// <summary>
/// Typed state ports. Local flags, quest and rumor commands reuse the existing narrow ports. Global flags
/// are a production versioned owner (a <see cref="GlobalFlagOwner"/> or a session's flag view). <paramref name="World"/> owns reaction, inventory, map and party
/// effects. A null capability turns every operation that needs it into a static frontier.
/// </summary>
public sealed record DialogConversationPorts(
    IDialogStateQueryPort LocalFlags,
    IDialogCommandPort Commands,
    IVersionedGlobalFlagOwner? GlobalFlags,
    IDialogWorldPort? World = null);

public enum DialogReactionMode
{
    /// <summary>Unsigned RE n: request the NPC→PC reaction to become n (the owner applies n minus current).</summary>
    Target,
    /// <summary>Lexically signed RE +n / RE -n: relative adjustment.</summary>
    Adjust,
}

public readonly record struct DialogReactionQuery(DialogParticipantId Npc, DialogParticipantId PlayerCharacter);

/// <summary>Effective NPC→PC reaction. Unknown is a frontier, never a neutral default.</summary>
public readonly record struct DialogReactionReadResult(bool Known, int Value)
{
    public static DialogReactionReadResult Of(int value) => new(true, value);
    public static DialogReactionReadResult Unknown() => new(false, 0);
}

public sealed record ReactionCommand(DialogParticipantId Npc, DialogParticipantId PlayerCharacter, DialogReactionMode Mode, int Value) : DialogCommand;

/// <summary>Negative IN: the NPC (or its substitute inventory) gives the item with this name ID to the PC.</summary>
public sealed record GiveItemCommand(DialogParticipantId Npc, DialogParticipantId PlayerCharacter, int ItemName) : DialogCommand;

/// <summary>AL: unsigned sets the PC alignment, lexically signed adjusts it.</summary>
public sealed record AlignmentCommand(DialogParticipantId PlayerCharacter, DialogReactionMode Mode, int Value) : DialogCommand;

public sealed record MarkAreaCommand(DialogParticipantId PlayerCharacter, int Area) : DialogCommand;

/// <summary>JO 0 node: the NPC asks to follow the PC; on acceptance the dialog continues at the node.</summary>
public sealed record JoinCommand(DialogParticipantId Npc, DialogParticipantId PlayerCharacter) : DialogCommand;

public enum DialogJoinResult
{
    Accepted,
    /// <summary>The follower rules refused; the source then speaks a generated refusal this runtime cannot produce.</summary>
    Refused,
    Rejected,
}

/// <summary>
/// Reaction, alignment, inventory, map and party seam. Calls are synchronous and serialized by the host. Rejected means
/// the command had no effect; a thrown exception is recorded as indeterminate.
/// </summary>
public interface IDialogWorldPort
{
    DialogReactionReadResult ReadReaction(DialogReactionQuery query);
    DialogCommandResult Execute(ReactionCommand command);
    DialogCommandResult Execute(GiveItemCommand command);
    DialogCommandResult Execute(MarkAreaCommand command);
    DialogCommandResult Execute(AlignmentCommand command);
    DialogJoinResult Execute(JoinCommand command);
}

public sealed class DialogConversationOptions
{
    public const int HardMaxStructuralReplies = 64;
    public const int HardMaxTurns = 1024;
    public const int HardMaxEffects = 4096;
    public const int HardMaxTextBytes = 4096;

    public int MaxStructuralReplies { get; init; } = HardMaxStructuralReplies;
    public int MaxTurns { get; init; } = 256;
    public int MaxEffects { get; init; } = HardMaxEffects;
    public int MaxTextBytes { get; init; } = HardMaxTextBytes;
    public int MaxOperationFieldBytes { get; init; } = 4096;
    public int MaxOperationsPerField { get; init; } = 32;

    internal void Validate()
    {
        if (MaxStructuralReplies is <= 0 or > HardMaxStructuralReplies) throw new ArgumentOutOfRangeException(nameof(MaxStructuralReplies));
        if (MaxTurns is <= 0 or > HardMaxTurns) throw new ArgumentOutOfRangeException(nameof(MaxTurns));
        if (MaxEffects is <= 0 or > HardMaxEffects) throw new ArgumentOutOfRangeException(nameof(MaxEffects));
        if (MaxTextBytes is <= 0 or > HardMaxTextBytes) throw new ArgumentOutOfRangeException(nameof(MaxTextBytes));
        if (MaxOperationFieldBytes is <= 0 or > 4096) throw new ArgumentOutOfRangeException(nameof(MaxOperationFieldBytes));
        if (MaxOperationsPerField is <= 0 or > 32) throw new ArgumentOutOfRangeException(nameof(MaxOperationsPerField));
    }
}

public enum DialogConversationPhase
{
    AwaitingSelection,
    Ended,
    AtFrontier,
    Failed,
}

/// <summary>How the selected reply leaves the dialog.</summary>
public enum DialogOptionTransition
{
    /// <summary>Positive response: continue at the checked NPC node.</summary>
    Direct,
    /// <summary>Zero/empty response: end and continue the caller SCR at the next line.</summary>
    Exit,
    /// <summary>Negative response: end and continue the caller SCR at the given line.</summary>
    ScriptLine,
    /// <summary>The reply action ends with JO: the NPC joins and the dialog continues at <see cref="DialogOption.Target"/>.</summary>
    Join,
    /// <summary>The reply action ends with FL: the NPC speaks <see cref="DialogOption.Target"/> and the dialog ends.</summary>
    FinalLine,
    /// <summary>Generated interaction (barter, training, …) this runtime does not perform.</summary>
    Unsupported,
}

public enum DialogOptionTextKind
{
    /// <summary>Decoded record text with @pcname@/@npcname@ substituted.</summary>
    Record,
    /// <summary>Generated phrase marker; the phrase itself lives in generic resources that are not loaded.</summary>
    Generated,
    /// <summary>No reply qualified: the single generated farewell that ends the dialog.</summary>
    Fallback,
}

public readonly record struct DialogOptionToken(Guid Conversation, int Turn, int Generation, DialogResponseId Response);

/// <summary>Why an offered reply cannot be executed by this runtime. The reply is still shown, as the data offers it.</summary>
public enum DialogOptionBlockKind
{
    UnsupportedInteraction,
    UnsupportedOperation,
    InvalidOperation,
    TargetUnsupported,
    TargetInvalid,
}

public sealed record DialogOptionBlock(DialogOptionBlockKind Kind, DialogOperationSource? Source = null);

public sealed record DialogOption(
    int Number,
    DialogResponseId ResponseId,
    int SerializedIndex,
    DialogOptionTextKind TextKind,
    string Text,
    DialogGeneratedMarker? Marker,
    DialogOptionTransition Transition,
    DialogNodeId? Target,
    DialogScriptLine? ScriptLine,
    DialogOptionToken Token,
    DialogOptionBlock? Block);

public sealed record DialogNpcLine(DialogNodeId Node, string Text);

public enum DialogEndKind
{
    /// <summary>Exit or negative response: the caller SCR continues at <see cref="DialogConversationEnd.ScriptLine"/>.</summary>
    ReturnToScript,
    /// <summary>FL: the NPC spoke its final line (<see cref="DialogConversation.NpcLine"/>); no SCR continuation follows.</summary>
    NpcFinalLine,
    /// <summary>The host closed an awaiting dialog without choosing a reply. No SCR continuation is implied.</summary>
    ClosedByHost,
}

public sealed record DialogConversationEnd(DialogEndKind Kind, DialogScriptLine? ScriptLine);

public enum DialogFrontierKind
{
    UnsupportedOperation,
    UnsupportedGeneratedText,
    UnsupportedTextToken,
    GroupOrderMismatch,
    CapabilityUnavailable,
    GlobalFlagUnknown,
    ReactionUnknown,
    JoinRefused,
    TurnLimit,
    EffectLimit,
}

public sealed record DialogConversationFrontier(
    DialogFrontierKind Kind,
    DialogNodeId Node,
    DialogOperationSource? Source = null,
    DialogResponseId? Response = null);

public enum DialogConversationFailureKind
{
    InvalidEntry,
    WrongEntryRole,
    MissingTarget,
    WrongTargetRole,
    StructuralReplyLimit,
    InvalidOperation,
    OperationLimit,
    InvalidText,
    InvalidQueryResult,
    QueryException,
    GlobalFlagFailed,
    CommandRejected,
    CommandIndeterminate,
}

public sealed record DialogConversationFailure(
    DialogConversationFailureKind Kind,
    DialogNodeId Node,
    DialogOperationSource? Source = null,
    DialogResponseId? Response = null,
    DialogTextFailureKind TextFailure = DialogTextFailureKind.None,
    GlobalFlagReason GlobalFlagReason = GlobalFlagReason.None);

/// <summary>Global-flag write in the ordered effect ledger.</summary>
public sealed record GlobalFlagWriteCommand(int Flag, bool Value) : DialogCommand;

public sealed record DialogEffectReceipt(
    int Ordinal,
    int Turn,
    DialogCommand Command,
    DialogOperationSource Source,
    DialogCommandAttemptOutcome Outcome,
    GlobalFlagWriteReceipt? GlobalFlag = null);

public enum DialogConversationSelectStatus
{
    Continued,
    Ended,
    AtFrontier,
    StaleSelection,
    NotOffered,
    UnsupportedTransition,
    WrongPhase,
    Failed,
}

public sealed record DialogConversationStepResult(DialogConversationSelectStatus Status);
