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
/// <paramref name="Rumors"/> (ARC-UI-008, ARC-DEV-171) takes the RQ action and answers the RU and RQ conditions; without it they are a
/// static frontier.
/// <paramref name="Skills"/> (ARC-SKILLS-004) admits the skill gates PS, HA, SK and TR and the actions TR and ET; without it
/// they stay unadmitted (<see cref="DialogFailureKind.UnsupportedOperation"/>).
/// <paramref name="MetBefore"/> (ARC-WORLD-043) answers the ME condition; without it ME is a static frontier.
/// <paramref name="Reputations"/> (ARC-WORLD-044) admits RP as a condition and an action; without it RP stays unadmitted
/// (<see cref="DialogFailureKind.UnsupportedOperation"/>).
/// <paramref name="Stores"/> (ARC-DEV-152) admits gv, pv and pf as conditions and actions; without it they stay unadmitted
/// (<see cref="DialogFailureKind.UnsupportedOperation"/>).
/// <paramref name="Gold"/> (ARC-DEV-179) admits the money code <c>$$</c> as a condition and an action; without it it stays unadmitted
/// (<see cref="DialogFailureKind.UnsupportedOperation"/>).
/// <paramref name="Quests"/> (ARC-DEV-180) admits the quest log conditions <c>qu</c>, <c>qa</c> and <c>qb</c>; without it they stay unadmitted
/// (<see cref="DialogFailureKind.UnsupportedOperation"/>).
/// <paramref name="Combat"/> (ARC-DEV-190) admits the combat action <c>co</c>; without it it stays unadmitted
/// (<see cref="DialogFailureKind.UnsupportedOperation"/>).
/// <paramref name="Story"/>, <paramref name="Areas"/> and <paramref name="MapArea"/> (ARC-DEV-191) admit the story state code <c>ss</c> (condition and action), the
/// known-area condition <c>ar</c> and the map-area condition <c>ia</c>; without them they stay unadmitted (<see cref="DialogFailureKind.UnsupportedOperation"/>).
/// <paramref name="Stats"/> (ARC-DEV-192) admits the PC stat conditions <c>ch</c>, <c>pe</c>, <c>le</c>, <c>al</c>, <c>ma</c>, <c>ta</c> and <c>na</c>; without it they stay
/// unadmitted (<see cref="DialogFailureKind.UnsupportedOperation"/>).
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
    IDialogSchematicPort? Schematics = null,
    IDialogGreetingPort? Greeting = null,
    IDialogBarterPort? Barter = null,
    IDialogScriptStorePort? Stores = null,
    IDialogExperiencePort? Experience = null,
    IDialogGoldPort? Gold = null,
    IDialogQuestPort? Quests = null,
    IDialogCombatPort? Combat = null,
    IDialogStoryPort? Story = null,
    IDialogAreaPort? Areas = null,
    IDialogMapAreaPort? MapArea = null,
    IDialogStatPort? Stats = null,
    IDialogHostilePort? Hostile = null,
    IDialogItemHoldPort? ItemHolds = null,
    IDialogItemTakePort? ItemTake = null,
    IDialogCompanionPort? Companion = null,
    IDialogCounterPort? Counters = null,
    IDialogCollegePort? Colleges = null);

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

/// <summary>ARC-DEV-199: the action <c>IN n</c> with n ≥ 0 (0x415BA0 case 9, ledger DAC5): the PC, failing that its first follower holding it, hands the NPC an item whose name is <see cref="ItemName"/>.</summary>
public sealed record TakeItemCommand(DialogParticipantId Npc, DialogParticipantId PlayerCharacter, int ItemName) : DialogCommand;

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

/// <summary>XP n (action, ARC-DEV-178, ledger DAC9, XP11): the PC gets the experience of quest experience level n (the source's dialog action
/// <c>xp</c>, 0x415BA0, calls <c>quest_get_xp</c>, 0x4C53C0, then <c>critter_give_xp</c>, 0x45F110). A level without a table entry gives 0 and changes
/// nothing.</summary>
public sealed record QuestExperienceCommand(DialogParticipantId PlayerCharacter, int Level) : DialogCommand;

/// <summary>Experience seam (ARC-DEV-178): the XP action. Applied covers a level that gives nothing; Rejected means the owner could not run the
/// command (no hero, no experience tables, a PC that is not the session's); a thrown exception is recorded as indeterminate.</summary>
public interface IDialogExperiencePort
{
    DialogCommandResult Execute(QuestExperienceCommand command);
}

/// <summary>The gold the PC and its direct followers hold together (ARC-DEV-179, ledger DCC5): one question of the <c>$$</c> condition.</summary>
public readonly record struct DialogGoldQuery(DialogParticipantId PlayerCharacter);

/// <summary>
/// <c>$$ n</c> as an action (ARC-DEV-179, ledger DAC2, GM1–GM4; 0x415BA0 case 0): n &gt; 0 gives the PC n gold from nothing; n &lt; 0 makes the
/// PC pay −n to the NPC, the followers paying what the PC's own gold did not cover; n = 0 changes nothing.
/// </summary>
public sealed record GoldCommand(DialogParticipantId PlayerCharacter, DialogParticipantId Npc, int Amount) : DialogCommand;

/// <summary>
/// Gold seam (ARC-DEV-179): the <c>$$</c> condition and action. <see cref="Total"/> is the signed 32-bit sum of the PC's gold and each direct
/// follower's (0x464780 over the PC, then over the follower array, 0x4411C0), wrapping like the source's. <see cref="Execute"/> returns Applied
/// when the payment ran as the source runs it (a payer that cannot pay is not an error: the source's transfer then does nothing, GM3) and
/// Rejected when the owner cannot run it (no hero, a PC other than the session's, an NPC it does not know, a budget or revision refusal); a
/// thrown exception is recorded as indeterminate.
/// </summary>
public interface IDialogGoldPort
{
    int Total(DialogGoldQuery query);
    DialogCommandResult Execute(GoldCommand command);
}

/// <summary>One question of the quest log conditions (ARC-DEV-180, ledger DCC4): the state of a quest for the PC.</summary>
public readonly record struct DialogQuestQuery(DialogParticipantId PlayerCharacter, int Quest);

/// <summary>
/// Quest log seam (ARC-DEV-180): the <c>qu</c>, <c>qa</c> and <c>qb</c> conditions. <see cref="State"/> is the PC's state of the quest as the
/// original's quest state getter (0x4C4CB0) answers it: 0 Unknown (a quest the PC never changed), 1 Mentioned, 2 Accepted, 3 Achieved,
/// 4 Completed, 5 OtherCompleted, 6 Botched (also for a quest botched over a kept stored state). The same log the <c>QU</c> action writes. The
/// quest number is 1000..1999. A thrown exception fails the conversation.
/// </summary>
public interface IDialogQuestPort
{
    int State(DialogQuestQuery query);
}

/// <summary>One question of the PC stat conditions (ARC-DEV-192): the PC's stat by the original's number (5 perception, 7 charisma, 16 magick-technology aptitude, 17 level, 19 alignment).</summary>
public readonly record struct DialogStatQuery(DialogParticipantId PlayerCharacter, int Stat);

/// <summary>
/// PC stat seam (ARC-DEV-192): the <c>ch</c>, <c>pe</c>, <c>le</c>, <c>al</c>, <c>ma</c>, <c>ta</c> and <c>na</c> conditions (ledger DCC2, DCC3), the values of the original's
/// stat getter (0x4B0490). Null means the owner does not hold that stat (the conversation then fails with <see cref="DialogConversationFailureKind.InvalidQueryResult"/>; an
/// owner that has no PC stats does not offer the port). A thrown exception fails the conversation.
/// </summary>
public interface IDialogStatPort
{
    int? Stat(DialogStatQuery query);
}

/// <summary>A spell college of the PC the <c>SC</c> condition asks about (ARC-DEV-197): the PC and the college (0..15).</summary>
public readonly record struct DialogCollegeQuery(DialogParticipantId PlayerCharacter, int College);

/// <summary>
/// Spell college seam (ARC-DEV-197, ledger CK1–CK3): the <c>SC</c> condition reads the PC's rank in a college (the original's getter 0x4B1AB0: 0..5, the number of that college's spells it knows). Null
/// means the owner does not hold it (the conversation then fails with <see cref="DialogConversationFailureKind.InvalidQueryResult"/>; an owner with no PC spell book does not offer the port). A thrown
/// exception fails the conversation (<see cref="DialogConversationFailureKind.QueryException"/>). Without the port <c>SC</c> in a condition stays <see cref="DialogFrontierKind.UnsupportedOperation"/>.
/// </summary>
public interface IDialogCollegePort
{
    int? Rank(DialogCollegeQuery query);
}

/// <summary>The <c>ss</c> action (ARC-DEV-191, ledger DAC7, SS1): the game's story state rises to <paramref name="Value"/> when it is larger.</summary>
public sealed record StoryStateCommand(DialogParticipantId PlayerCharacter, int Value) : DialogCommand;

/// <summary>
/// Story state seam (ARC-DEV-191): the <c>ss</c> condition and action. <see cref="State"/> is the game's story state (0x445090, 0 at a new game); <c>Execute</c> is the
/// setter 0x4450A0, which keeps the larger of the old and the new value (a value at or below the state is a successful no-change). Rejected means the owner could not
/// run it; a thrown exception fails the conversation (a query) or is recorded as indeterminate (an action).
/// </summary>
public interface IDialogStoryPort
{
    int State();
    DialogCommandResult Execute(StoryStateCommand command);
}

/// <summary>One question of the <c>ar</c> condition (ARC-DEV-191): whether the PC knows a world-map area (1..4095).</summary>
public readonly record struct DialogAreaQuery(DialogParticipantId PlayerCharacter, int Area);

/// <summary>Known areas seam (ARC-DEV-191): the <c>ar</c> condition (ledger DCC7, WT2), the areas the <c>mm</c> action marks. A thrown exception fails the conversation.</summary>
public interface IDialogAreaPort
{
    bool Known(DialogAreaQuery query);
}

/// <summary>
/// Map area seam (ARC-DEV-191): the <c>ia</c> condition (ledger DCC7, GA2). <see cref="Area"/> is the area of the PC's map (0x4CB6A0); null when the owner cannot
/// answer (the conversation then fails with <see cref="DialogConversationFailureKind.InvalidQueryResult"/>; an owner that knows this in advance does not offer the port).
/// A thrown exception fails the conversation.
/// </summary>
public interface IDialogMapAreaPort
{
    int? Area(DialogParticipantId playerCharacter);
}

/// <summary>ARC-DEV-190: the <c>co</c> action (ledger SU13, CA2): the attack event of the PC on the NPC, loudness 1, no flags.</summary>
public sealed record StartCombatCommand(DialogParticipantId Npc, DialogParticipantId PlayerCharacter) : DialogCommand;

/// <summary>
/// Combat seam (ARC-DEV-190): the <c>co</c> action. The conversation asks for the command once, after the whole action string has run (the
/// original sets a flag at <c>co</c> and runs the attack event at the string's end, 0x416703–0x416717); the owner runs the attack event of the
/// PC on the NPC (0x4A9650: loudness 1, flags 0). Rejected means the owner could not run it (no combat host, an unknown NPC or a PC that is
/// not its own) and fails the conversation; a thrown exception is recorded as indeterminate.
/// </summary>
public interface IDialogCombatPort
{
    DialogCommandResult Execute(StartCombatCommand command);
}

/// <summary>ARC-DEV-194: an item name asked about for the dialog's PC and NPC (the item's name field, which <c>in</c> and <c>ni</c> compare).</summary>
public readonly record struct DialogItemQuery(DialogParticipantId Npc, DialogParticipantId PlayerCharacter, int ItemName);

/// <summary>
/// Item-holding seam (ARC-DEV-194): the <c>in</c> and <c>ni</c> conditions (0x4150D0 cases 11 and 23, ledger DCC6, IH1–IH4). The owner answers with the item search by name
/// (0x462480): an object holds an item when its own inventory list has one whose name field equals the number. Calls are synchronous and serialized by the host; a thrown
/// exception fails the conversation (<see cref="DialogConversationFailureKind.QueryException"/>). Without the port both codes stay <see cref="DialogFrontierKind.UnsupportedOperation"/>.
/// </summary>
public interface IDialogItemHoldPort
{
    /// <summary><c>in n</c> with n ≥ 0: the PC holds item n or, failing that, one of its followers does.</summary>
    bool PartyHolds(DialogItemQuery query);

    /// <summary><c>in −n</c>: the NPC holds item n or, failing that, its substitute container does (0x45F650); an NPC without a substitute container holds only its own.</summary>
    bool NpcHolds(DialogItemQuery query);
}

/// <summary>The NPC's dialog-point script counter the <c>LC</c> condition reads (ARC-DEV-196): the NPC and the counter's index (0..3).</summary>
public readonly record struct DialogCounterQuery(DialogParticipantId Npc, int Index);

/// <summary>An <c>LC</c> action: the NPC's dialog-point script counter <c>Index</c> (0..3) takes <c>Value</c>, the second operand's low eight bits (0..255), as the source's setter stores it.</summary>
public sealed record CounterWriteCommand(DialogParticipantId Npc, int Index, int Value) : DialogCommand;

/// <summary>
/// NPC counter seam (ARC-DEV-196, ledger KC1–KC4): the <c>LC</c> condition and action over the byte counters of the NPC's dialog-point script attachment (the ones <c>LF</c> reads the flags of).
/// Calls are synchronous and serialized by the host. A query answers 0..255 (anything else is <see cref="DialogConversationFailureKind.InvalidQueryResult"/>) and a throwing query fails the
/// conversation (<see cref="DialogConversationFailureKind.QueryException"/>); for <see cref="Execute"/>, Rejected means the owner refused and the conversation fails on the command and a thrown exception is
/// recorded as indeterminate. Without the port <c>LC</c> stays <see cref="DialogFrontierKind.UnsupportedOperation"/>.
/// </summary>
public interface IDialogCounterPort
{
    int Read(DialogCounterQuery query);
    DialogCommandResult Execute(CounterWriteCommand command);
}

/// <summary>The companion state the <c>FO</c> and <c>WT</c> conditions ask about (ARC-DEV-195, ledger CF1).</summary>
public enum DialogCompanionFact { LedByPc, Jilted }

/// <summary>The NPC flag actions: <c>WA</c> (the NPC waits for the PC), <c>LV</c> (it is forced to leave the party), <c>SO</c> and <c>SC</c> (set and clear its spread-out flag).</summary>
public enum DialogCompanionAction { Wait, Leave, SpreadOut, SpreadClear }

/// <summary>A companion action on the NPC (ARC-DEV-195): applied as the source applies it, with no operand.</summary>
public sealed record CompanionCommand(DialogParticipantId Npc, DialogParticipantId PlayerCharacter, DialogCompanionAction Action) : DialogCommand;

/// <summary>
/// Companion seam (ARC-DEV-195, ledger CF1–CF7): the <c>FO</c> and <c>WT</c> conditions and the <c>WA</c>, <c>LV</c>, <c>SO</c> and <c>SC</c> actions. Calls are synchronous and serialized by the
/// host. A query that throws fails the conversation (<see cref="DialogConversationFailureKind.QueryException"/>). Applied covers the source's no-effect cases (leaving while not following,
/// waiting while already waiting, a flag already in the asked state); Rejected means the owner refused or does not model the change and the conversation fails on the command; a thrown exception is
/// recorded as indeterminate. Without the port the codes stay <see cref="DialogFrontierKind.UnsupportedOperation"/>.
/// </summary>
public interface IDialogCompanionPort
{
    /// <summary>FO: the NPC's leader is the PC (true while it waits too).</summary>
    bool LedByPc(DialogPartyQuery query);

    /// <summary>WT: the NPC's jilted flag is set (it was left or timed out waiting and has not followed since).</summary>
    bool Jilted(DialogPartyQuery query);

    DialogCommandResult Execute(CompanionCommand command);
}

/// <summary>
/// Item hand-over seam (ARC-DEV-199): the non-negative <c>IN</c> action. Applied means the item moved to the NPC or, as in the source, that nobody held an item with that name and
/// nothing happened; Rejected means the owner refused or does not model the move (the conversation then fails on the command); a thrown exception is recorded as indeterminate. Without the port the
/// non-negative <c>IN</c> stays <see cref="DialogFrontierKind.UnsupportedOperation"/>.
/// </summary>
public interface IDialogItemTakePort
{
    DialogCommandResult Execute(TakeItemCommand command);
}

/// <summary>ARC-DEV-193: whether the NPC is hostile toward the PC now: the NPC is alive and the AI turns kill-on-sight toward the PC.</summary>
public readonly record struct DialogHostileQuery(DialogParticipantId Npc, DialogParticipantId PlayerCharacter);

/// <summary>
/// Hostility seam (ARC-DEV-193): the check the original's reply step runs after a picked reply's action string returned 1 and before the
/// reply's transition (0x413130, ledger HR1–HR3). The owner answers whether the NPC is alive and kill-on-sight toward the PC now (0x45D8D0,
/// 0x4AE120, the will-kos script included). A thrown exception fails the conversation (<see cref="DialogConversationFailureKind.QueryException"/>).
/// Without the port the check is not made (as before).
/// </summary>
public interface IDialogHostilePort
{
    bool Hostile(DialogHostileQuery query);
}

/// <summary>ARC-TECHNOLOGY-006: the SU action, the schematic window with the NPC as crafter and the PC as viewer (ledger NC1).</summary>
public sealed record OpenSchematicsCommand(DialogParticipantId Npc, DialogParticipantId PlayerCharacter) : DialogCommand;

/// <summary>
/// Schematic seam (ARC-TECHNOLOGY-006): SU. Applied means the host will open the window with the NPC as crafter once the
/// conversation has ended (<see cref="DialogEndKind.SchematicWindow"/>); Rejected (a dead NPC or PC, as the original's open refuses)
/// fails the conversation with no effect of this action; a thrown exception is recorded as indeterminate.
/// </summary>
public interface IDialogSchematicPort
{
    DialogCommandResult Execute(OpenSchematicsCommand command);
}

/// <summary>ARC-UI-050: a Player <c>b:</c> reply, the barter window with the NPC as merchant and the PC as buyer (ledger BT7).</summary>
public sealed record OpenBarterCommand(DialogParticipantId Npc, DialogParticipantId PlayerCharacter) : DialogCommand;

/// <summary>
/// Barter seam (ARC-UI-050). Without it a <c>b:</c> reply stays blocked as an unsupported interaction. Applied means the host will open
/// the barter window with the NPC once the conversation has ended (<see cref="DialogEndKind.BarterWindow"/>); Rejected (the original's
/// open refuses only a dead PC) fails the conversation; a thrown exception is recorded as indeterminate.
/// </summary>
public interface IDialogBarterPort
{
    DialogCommandResult Execute(OpenBarterCommand command);
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

/// <summary>A rumor the PC may know (ARC-DEV-171): the PC and the rumor number (1000..2999).</summary>
public readonly record struct DialogRumorQuery(DialogParticipantId PlayerCharacter, int Rumor);

/// <summary>
/// Rumor seam (ARC-UI-008, ARC-DEV-171): the RQ action and the RU and RQ conditions. Calls are synchronous and serialized by the
/// host. For <see cref="Execute"/>, Rejected means the command had no effect and the owner refused it and a thrown exception is
/// recorded as indeterminate; a thrown exception from a query fails the conversation.
/// </summary>
public interface IDialogRumorPort
{
    DialogCommandResult Execute(QuellRumorCommand command);

    /// <summary>The RU condition (0x4C58D0, ledger RU1–RU2): whether the PC has learned the rumor.</summary>
    bool IsKnown(DialogRumorQuery query);

    /// <summary>The RQ condition (0x4C5650, ledger RU1–RU2): whether the rumor is quelled (a game-wide set, not the PC's).</summary>
    bool IsQuelled(int rumor);
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

/// <summary>ARC-DEV-152: the script store a gv, pv or pf operation reads or writes (the global variables, the PC's variables and the
/// PC's flags of ARC-DEV-098, SA13, SA14).</summary>
public enum DialogScriptStore { GlobalVariable, PcVariable, PcFlag }

/// <summary>ARC-DEV-152: one cell of a script store; the PC's stores are those of <paramref name="PlayerCharacter"/>, the global one
/// ignores it.</summary>
public readonly record struct DialogScriptStoreQuery(DialogParticipantId PlayerCharacter, DialogScriptStore Store, int Index);

/// <summary>ARC-DEV-152: gv, pv or pf as an action (0x415BA0): the cell <paramref name="Index"/> of <paramref name="Store"/> takes
/// <paramref name="Value"/> (a PC flag 0 or 1, the only values the conversation grammar admits).</summary>
public sealed record ScriptStoreCommand(DialogParticipantId PlayerCharacter, DialogScriptStore Store, int Index, int Value) : DialogCommand;

/// <summary>
/// Script store seam (ARC-DEV-152, ledger DG1–DG4): the gv, pv and pf conditions and actions. <see cref="Read"/> answers a cell (a PC
/// flag 0 or 1); <see cref="Execute"/> writes one (Applied also when the value was already there). Calls are synchronous and serialized
/// by the host; a thrown exception fails the conversation (a query) or is recorded as indeterminate (an action).
/// </summary>
public interface IDialogScriptStorePort
{
    int Read(DialogScriptStoreQuery query);
    DialogCommandResult Execute(ScriptStoreCommand command);
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
    /// <summary>ARC-TECHNOLOGY-006: the reply action holds SU: every action of the field runs, the reply's target node is not entered
    /// and the dialog ends for the schematic window (<see cref="DialogEndKind.SchematicWindow"/>).</summary>
    Schematics,
    /// <summary>ARC-UI-050: a Player <c>b:</c> reply with a barter port: the reply's action runs, its response value is not followed,
    /// and the dialog ends for the barter window (<see cref="DialogEndKind.BarterWindow"/>).</summary>
    Barter,
    /// <summary>Generated interaction (barter, training, …) this runtime does not perform.</summary>
    Unsupported,
}

public enum DialogOptionTextKind
{
    /// <summary>Decoded record text with @pcname@/@npcname@ substituted.</summary>
    Record,
    /// <summary>Generated phrase marker. With a greeting port (ARC-SCRIPT-043) the text is the phrase picked from the generated
    /// dialog files (<see cref="DialogOption.TextOrigin"/>); otherwise, and for a prefix marker, it is empty.</summary>
    Generated,
    /// <summary>No reply qualified: the single generated farewell that ends the dialog (its phrase as for a generated reply).</summary>
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
    DialogOptionBlock? Block,
    DialogLineOrigin? TextOrigin = null);

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
    /// <summary>ARC-DEV-193: after the reply's action the NPC turned hostile toward the PC (alive and kill-on-sight, 0x413130, ledger HR1–HR3), so
    /// it answered with a generated line (group 1000, not loaded) and ended the talk, as after FL: the reply's own transition (the next node, the
    /// exit, the barter window) did not follow and no SCR continuation follows.</summary>
    HostileRefusal,
    /// <summary>ARC-TECHNOLOGY-006: SU: the dialog ended without another NPC line and the host opens the schematic window with the NPC
    /// as crafter (the original's interface-mode change ends the dialog, NC2); no SCR continuation follows and the talk does not
    /// resume when the window closes.</summary>
    SchematicWindow,
    /// <summary>ARC-UI-050: a <c>b:</c> reply: the dialog ended without another NPC line and the host opens the barter window with the NPC
    /// as merchant (the original's interface-mode change ends the dialog, BT7); no SCR continuation follows and the talk does not resume
    /// when the window closes.</summary>
    BarterWindow,
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
