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
/// effects, except those a present <paramref name="Party"/> or <paramref name="Inventory"/> port takes. A null capability turns every operation that needs it into a static frontier. <paramref name="Party"/>
/// (ARC-WORLD-013) admits WA and UW and, when present, also takes JO instead of <paramref name="World"/>.
/// <paramref name="Inventory"/> (ARC-WORLD-014), when present, takes negative IN instead of <paramref name="World"/>.
/// <paramref name="Rumors"/> (ARC-UI-008) takes RQ; without it RQ is a static frontier.
/// <paramref name="Skills"/> (ARC-SKILLS-004) admits the skill gates PS, HA, SK and TR and the actions TR and ET; without it
/// they stay unadmitted (<see cref="DialogFailureKind.UnsupportedOperation"/>).
/// <paramref name="MetBefore"/> (ARC-WORLD-043) answers the ME condition; without it ME is a static frontier.
/// <paramref name="Reputations"/> (ARC-WORLD-044) admits RP as a condition and an action; without it RP stays unadmitted
/// (<see cref="DialogFailureKind.UnsupportedOperation"/>).
/// <paramref name="Greeting"/> (ARC-WORLD-058) resolves an NPC <c>g:</c> greeting; without it the node is a static frontier.
/// </summary>
public sealed record DialogConversationPorts(
    IDialogStateQueryPort LocalFlags,
    IDialogCommandPort Commands,
    IVersionedGlobalFlagOwner? GlobalFlags,
    IDialogWorldPort? World = null,
    IDialogPartyPort? Party = null,
    IDialogInventoryPort? Inventory = null,
    IDialogRumorPort? Rumors = null,
    IDialogSkillPort? Skills = null,
    IDialogMetBeforePort? MetBefore = null,
    IDialogFatePort? Fate = null,
    IDialogReputationPort? Reputations = null,
    IDialogGreetingPort? Greeting = null);

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

/// <summary>RQ: the rumor is quelled (proven false). The original sets a game-wide mark with no participant (ARC-UI-008,
/// ledger LB12).</summary>
public sealed record QuellRumorCommand(int RumorId) : DialogCommand;

/// <summary>JO 0 node: the NPC asks to follow the PC; on acceptance the dialog continues at the node.</summary>
public sealed record JoinCommand(DialogParticipantId Npc, DialogParticipantId PlayerCharacter) : DialogCommand;

/// <summary>UW 0 node: a waiting NPC asks to follow the PC again; on acceptance the dialog continues at the node.</summary>
public sealed record UnwaitCommand(DialogParticipantId Npc, DialogParticipantId PlayerCharacter) : DialogCommand;

public readonly record struct DialogPartyQuery(DialogParticipantId Npc, DialogParticipantId PlayerCharacter);

/// <summary>A skill of the PC by the source's number: 0..11 the basic skills, 12..15 the tech skills; any other number is a
/// skill the source reads as level 0 and training none.</summary>
public readonly record struct DialogSkillQuery(DialogParticipantId PlayerCharacter, int Skill);

/// <summary>TR skill tier (action): the PC's training in the skill is set through the source's training setter; a tier the
/// rules refuse (a skipped tier, a level below the tier's minimum, a skill or tier out of range) changes nothing and the
/// dialog goes on.</summary>
public sealed record SkillTrainingCommand(DialogParticipantId PlayerCharacter, int Skill, int Training) : DialogCommand;

/// <summary>FP (action, ARC-WORLD-054): the PC gets one fate point (the source's dialog action <c>fp</c>, 0x415BA0, ledger FT8); the
/// operand the source reads is not used.</summary>
public sealed record FatePointCommand(DialogParticipantId PlayerCharacter) : DialogCommand;

/// <summary>Fate seam (ARC-WORLD-054): the FP action. Applied covers a PC already at the maximum (the source's stat setter clamps);
/// Rejected means the owner could not run the command; a thrown exception is recorded as indeterminate.</summary>
public interface IDialogFatePort
{
    DialogCommandResult Execute(FatePointCommand command);
}

/// <summary>
/// Skill seam (ARC-SKILLS-004): the PC's effective skill level (0..20) and training (0 none .. 3 master) for the skill gates
/// and the expert-teacher gate, and the training action. Calls are synchronous and serialized by the host. Applied covers the
/// training rules' refusal (nothing written); Rejected means the owner could not run the command at all; a thrown exception is
/// recorded as indeterminate.
/// </summary>
public interface IDialogSkillPort
{
    int Level(DialogSkillQuery query);
    int Training(DialogSkillQuery query);
    DialogCommandResult Execute(SkillTrainingCommand command);
}

/// <summary>Result of JO and UW.</summary>
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

/// <summary>
/// Party seam (ARC-WORLD-013): WA reads, JO and UW. Calls are synchronous and serialized by the host. Rejected means the
/// command had no effect; Refused is the follower rules' refusal; a thrown exception is recorded as indeterminate.
/// </summary>
public interface IDialogPartyPort
{
    /// <summary>WA: whether the NPC is waiting for this PC (the source's wait flag).</summary>
    bool IsWaiting(DialogPartyQuery query);
    DialogJoinResult Execute(JoinCommand command);
    DialogJoinResult Execute(UnwaitCommand command);
}

/// <summary>
/// Inventory seam (ARC-WORLD-014): negative IN. Calls are synchronous and serialized by the host. Applied covers the
/// source's no-effect case (no item with that name); Rejected means the command had no effect and the owner refused it; a
/// thrown exception is recorded as indeterminate.
/// </summary>
public interface IDialogInventoryPort
{
    DialogCommandResult Execute(GiveItemCommand command);
}

/// <summary>
/// Rumor seam (ARC-UI-008): RQ. Calls are synchronous and serialized by the host. Rejected means the command had no effect
/// and the owner refused it; a thrown exception is recorded as indeterminate.
/// </summary>
public interface IDialogRumorPort
{
    DialogCommandResult Execute(QuellRumorCommand command);
}

/// <summary>
/// "Met before" seam (ARC-WORLD-043): the ME condition. True when the NPC remembers the PC from an earlier conversation or
/// reaction change; the original excludes the conversation in progress, so it is still false during the first talk
/// (0x4C0C40). Calls are synchronous and serialized by the host; a thrown exception fails the conversation.
/// </summary>
public interface IDialogMetBeforePort
{
    bool MetBefore(DialogReactionQuery query);
}

/// <summary>A reputation of the PC by number (ARC-WORLD-044).</summary>
public readonly record struct DialogReputationQuery(DialogParticipantId PlayerCharacter, int Reputation);

/// <summary>RP: <see cref="Add"/> gives the PC the reputation, otherwise it loses it (the source's add and remove).</summary>
public sealed record ReputationCommand(DialogParticipantId PlayerCharacter, int Reputation, bool Add) : DialogCommand;

/// <summary>What the reputation owner did with an RP action: applied (also a reputation already held or not held, which changes
/// nothing), skipped (a number the owner does not admit, the dialog goes on), or rejected (the dialog fails).</summary>
public enum DialogReputationResult { Applied, Skipped, Rejected }

/// <summary>
/// Reputation seam (ARC-WORLD-044): RP conditions and actions. Calls are synchronous and serialized by the host; a thrown
/// exception fails the conversation (a query) or is recorded as indeterminate (an action).
/// </summary>
public interface IDialogReputationPort
{
    bool HasReputation(DialogReputationQuery query);
    DialogReputationResult Execute(ReputationCommand command);
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
    /// <summary>The reply action ends with JO (or UW): the NPC joins (or stops waiting) and the dialog continues at <see cref="DialogOption.Target"/>.</summary>
    Join,
    /// <summary>The reply action ends with FL: the NPC speaks <see cref="DialogOption.Target"/> and the dialog ends.</summary>
    FinalLine,
    /// <summary>The reply action ends with ET (ARC-SKILLS-004): when the PC's skill is at least level 9 and trained, the dialog
    /// continues at <see cref="DialogOption.Target"/>; otherwise the source speaks a generated refusal (a frontier here).</summary>
    ExpertTeacher,
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

/// <param name="Origin">ARC-WORLD-058: for a greeting line (a <c>g:</c> node), where its text came from; null for a record's
/// own text.</param>
public sealed record DialogNpcLine(DialogNodeId Node, string Text, DialogLineOrigin? Origin = null);

public enum DialogEndKind
{
    /// <summary>Exit or negative response: the caller SCR continues at <see cref="DialogConversationEnd.ScriptLine"/>.</summary>
    ReturnToScript,
    /// <summary>FL, or a greeting that ends the talk (ARC-WORLD-058: slots 1–4, a floating slot node): the NPC spoke its final
    /// line (<see cref="DialogConversation.NpcLine"/>); no SCR continuation follows.</summary>
    NpcFinalLine,
    /// <summary>The host closed an awaiting dialog without choosing a reply. No SCR continuation is implied.</summary>
    ClosedByHost,
    /// <summary>ARC-WORLD-043: an insult (a Player <c>i:</c> reply) left the NPC's reaction at 20 or below, so the NPC
    /// answered with a generated line (not loaded) and ended the talk, as after FL: no SCR continuation follows.</summary>
    Insulted,
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
    /// <summary>ARC-WORLD-058: the greeting port gave no facts for the NPC.</summary>
    GreetingFactsUnknown,
    JoinRefused,
    /// <summary>ET refused (ARC-SKILLS-004): the PC's skill is below level 9 or untrained; the source then speaks a generated
    /// "won't teach" line this runtime cannot produce.</summary>
    ExpertTeacherRefused,
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
