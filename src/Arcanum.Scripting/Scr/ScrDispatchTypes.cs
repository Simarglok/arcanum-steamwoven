using Arcanum.Data.Scripts;

namespace Arcanum.Scripting.Scr;

/// <summary>A session object a script event names: the runtime map (positive) and the stable entity code (1..128
/// ASCII letters, digits and <c>. _ : -</c>). Never a live handle.</summary>
public readonly record struct ScrObjectRef
{
    public ScrObjectRef(int map, string code)
    {
        if (map <= 0) throw new ArgumentOutOfRangeException(nameof(map), "A runtime map is positive.");
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length is 0 or > 128 || code.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-')))
            throw new ArgumentException("An entity code is 1..128 ASCII letters, digits and . _ : -.", nameof(code));
        Map = map; Code = code;
    }

    public int Map { get; }
    public string Code { get; }
}

/// <summary>One script attachment as an object, tile or sector stores it: the script number (0 = none), the 32 local
/// flags and the four byte counters (counter 0 in the low byte).</summary>
public readonly record struct ScrSlot(int Script, uint LocalFlags, uint Counters);

/// <summary>Where the attachment of a run lives, which decides how it is written back.</summary>
public enum ScrSource { Object, Tile, Sector }

/// <summary>The attachment points (0..35) the dispatcher treats by name.</summary>
public static class ScrPoint
{
    public const int Use = 1, Dialog = 9, DialogOverride = 31, Count = 36;
}

/// <summary>
/// An event on an object's attachment point: the attachee whose slot runs, the triggerer and the extra object (either
/// may be absent), and the line to start at (0 for every engine event; a dialog resumption starts elsewhere).
/// </summary>
public sealed record ScrObjectEvent(int Point, ScrObjectRef Attachee, ScrObjectRef? Triggerer, ScrObjectRef? Extra, int Line = 0);

/// <summary>A critter stepping onto a tile that holds <paramref name="Slot"/>: point use, line 0, no attachee.</summary>
public sealed record ScrTileEvent(ScrSlot Slot, ScrObjectRef Triggerer);

/// <summary>The player entering a sector whose script is <paramref name="Slot"/>: point use, line 0, no attachee.</summary>
public sealed record ScrSectorEvent(ScrSlot Slot, ScrObjectRef Player);

public enum ScrScriptStatus { Found, Missing, Unreadable }

/// <summary>A script the host found by number (a framed program), or why not.</summary>
public sealed record ScrScriptLookup(ScrScriptStatus Status, ScrProgram? Program)
{
    public static ScrScriptLookup Missing { get; } = new(ScrScriptStatus.Missing, null);
    public static ScrScriptLookup Unreadable { get; } = new(ScrScriptStatus.Unreadable, null);
    public static ScrScriptLookup Of(ScrProgram program) => new(ScrScriptStatus.Found, program ?? throw new ArgumentNullException(nameof(program)));
}

/// <summary>What the dialog gates of an NPC attachee see (ARC-RES-042 E6); any combination. The original tests dead,
/// then surrendered, then mind-controlled, each on its own.</summary>
[Flags]
public enum ScrDialogState { Normal = 0, Dead = 1, Surrendered = 2, MindControlled = 4 }

/// <summary>The trap spot attempt before a use event (E2): no unspotted trap on the attachee, the trap stayed hidden,
/// or the triggerer spotted it (the use is cancelled).</summary>
public enum ScrTrapCheck { NoTrap, NotSpotted, Spotted }

/// <summary>
/// What the dispatcher reads from the game while it decides and runs. It never writes: every change of a run comes
/// back in <see cref="ScrEffects"/>, which the caller applies as one step. Calls are synchronous; a host must answer
/// from one consistent state for the whole call.
/// </summary>
public interface IScrHost
{
    /// <summary>The attachee's slot for the point, or null when it has none.</summary>
    ScrSlot? Slot(ScrObjectRef attachee, int point);
    /// <summary>The script with this number; any number may be asked (a call script's operand is a value), and one with no file
    /// is <see cref="ScrScriptLookup.Missing"/>.</summary>
    ScrScriptLookup Script(int number);
    /// <summary>Global flag 0..3199.</summary>
    bool GlobalFlag(int index);
    int StoryState();
    /// <summary>An NPC that is a monster generator (its slots never run, E5).</summary>
    bool IsMonsterGenerator(ScrObjectRef attachee);
    /// <summary>Asked only for a dialog event; <see cref="ScrDialogState.Normal"/> for anything that is not an NPC; a
    /// combination of the three states otherwise.</summary>
    ScrDialogState DialogState(ScrObjectRef attachee);
    /// <summary>Asked only for a use event with a triggerer, before the script; the host owns the roll.</summary>
    ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee);
    /// <summary>A trap object (destroyed instead of written back when its script becomes 0, E3).</summary>
    bool IsTrapObject(ScrObjectRef attachee);
    /// <summary>The player character the player focus names, or null when there is none.</summary>
    ScrObjectRef? Player();
    /// <summary>The critter's leader, or null when it follows no one.</summary>
    ScrObjectRef? Leader(ScrObjectRef critter);
    /// <summary>An NPC with the jilted flag; false for anything that is not an NPC.</summary>
    bool IsJilted(ScrObjectRef critter);
    /// <summary>A player character (the dialog action starts a conversation only for one).</summary>
    bool IsPc(ScrObjectRef critter);
}

/// <summary>
/// ARC-SCRIPT-014: the object questions of the guard conditions of set <see cref="ScrAdmissionSet.S4"/> (ledger SG1). A host
/// whose admission set holds them implements this interface; the dispatcher asks only for the objects the conditions name.
/// </summary>
public interface IScrObjectQueries
{
    /// <summary>The state of the quest the condition compares (SG1): a PC's own state of it (6 when its entry is marked
    /// botched); for any other object the quest's global state, whatever the object.</summary>
    int QuestState(ScrObjectRef obj, int quest);
    /// <summary>Whether a direct inventory item of the object has this name number (SG2: a container's or a critter's
    /// inventory, no nested search); false for an object without an inventory.</summary>
    bool HoldsItemNamed(ScrObjectRef obj, int name);
    /// <summary>The object's name number (its name field).</summary>
    int NameOf(ScrObjectRef obj);
    /// <summary>Whether the object is mind-controlled (its spell flags); such an object is never "named".</summary>
    bool IsMindControlled(ScrObjectRef obj);
    /// <summary>The name number of the item the object wields in inventory location 1000..1008, or null for an empty one.</summary>
    int? WieldedItemName(ScrObjectRef obj, int location);
    /// <summary>Whether the object has the reputation (SG5: only a PC has reputations).</summary>
    bool HasReputation(ScrObjectRef obj, int reputation);
    /// <summary>ARC-SCRIPT-018: the object's type (its type field, OV3: 15 for a PC, 16 for an NPC), or null when the host
    /// does not know the object (the run then stops as for an absent object).</summary>
    int? ObjectType(ScrObjectRef obj);
}

/// <summary>
/// ARC-SKILLS-009: the skill question of get skill (action 47, ledger SX1, SX2). A host whose admission set holds the action implements
/// this interface; the interpreter asks only for skill numbers 0..15 and stores 0 for any other without asking.
/// </summary>
public interface IScrSkillQueries
{
    /// <summary>The object's effective skill level, as the original's level functions give it (0x4C5EB0 for a basic skill 0..11,
    /// 0x4C65E0 for a tech skill 12..15 as tech index <paramref name="skill"/> − 12): the base level from the points (a monstrous
    /// critter's melee 20) plus the effects' changes, clamped to 0..20 and capped by the key stat; never the points or the training.
    /// 0 for an object that is not a PC or an NPC.</summary>
    int SkillLevel(ScrObjectRef obj, int skill);
}

/// <summary>
/// ARC-SKILLS-009: a use-item-with-skill action (79, ledger SX3, SX4) as the run asks for it: the source, the item (absent for a use
/// without one), the target, the skill number and the modifier, as read. The original only queues the source's use-item-with-skill goal
/// (0x4352C0), which runs the shared skill check later with the modifier as the invocation modifier (counted twice, SC12) and no flags;
/// the script never sees the result. The run asks for nothing without a source or a target. Everything else (the gate, holding the item,
/// the check, the skill's handler) is the host's.
/// </summary>
public readonly record struct ScrSkillUse(ScrObjectRef Source, ScrObjectRef? Item, ScrObjectRef Target, int Skill, int Modifier);

/// <summary>A fade-and-teleport's extras (action 102, ledger TR2): the game seconds the move takes, the sound and the movie
/// numbers, as the action reads them. The original uses the seconds and the sound only when positive and the movie when it
/// is not zero (an unsigned test); the fade itself, the time advance, the sound and the movie are the host's.</summary>
public sealed record ScrTeleportFade(int Seconds, int Sound, int Movie);

/// <summary>
/// ARC-SCRIPT-014: a teleport the run asks for (actions 44 and 102, ledger TR1, TR2): the object, the catalogue map number as the
/// script gives it, and the tile. <see cref="RuntimeMap"/> is that number less 4,999, as the original computes it (32-bit).
/// The original keeps one pending teleport: a request while a teleport of the local PC is pending is refused, any other
/// pending request is replaced (ledger TR4); the host owns that rule.
/// </summary>
public sealed record ScrTeleport(ScrObjectRef Object, int CatalogueMap, int X, int Y, ScrTeleportFade? Fade = null)
{
    public int RuntimeMap => unchecked(CatalogueMap - 4999);
}

/// <summary>What the caller does after the event: its normal behaviour, not, or nothing ran because the script is
/// outside the admitted set (the caller states its own reaction to a refusal).</summary>
public enum ScrOutcome { RunDefault, SkipDefault, Refused }

/// <summary>Why a dispatch ended.</summary>
public enum ScrStop
{
    /// <summary>The slot holds script 0 or the attachee has no slot for the point (run default, E2).</summary>
    NoScript,
    /// <summary>The triggerer spotted a trap on the attachee before a use (skip default, E2).</summary>
    TrapSpotted,
    /// <summary>The script is a trap script (number 30000..30007 with a trap file flag), run by trap code, not
    /// interpreted (skip default, E4).</summary>
    TrapScript,
    /// <summary>The script file is missing (skip default, E4; run default for the mind-control script, X5).</summary>
    MissingScript,
    /// <summary>The script file could not be read or framed (as missing).</summary>
    UnreadableScript,
    /// <summary>The attachee is a monster generator (run default, E5).</summary>
    MonsterGenerator,
    /// <summary>A dead NPC's dialog script without the death-speech flag (run default, E6).</summary>
    DeadWithoutSpeech,
    /// <summary>A surrendered NPC's dialog script without the surrender-speech flag (run default, E6).</summary>
    SurrenderedWithoutSpeech,
    /// <summary>A return-and-skip-default action (E8).</summary>
    ReturnSkipDefault,
    /// <summary>A return-and-run-default action (E8).</summary>
    ReturnRunDefault,
    /// <summary>The last line continued past the end (skip default, auto-removal applies, E7).</summary>
    FellOffEnd,
    /// <summary>A goto to exactly the line count, or a start line outside the script: the line cannot be read
    /// (skip default, no auto-removal, E9).</summary>
    LineOutOfRange,
    /// <summary>The executed-line budget ran out (skip default, no auto-removal, E9).</summary>
    Budget,
    /// <summary>A dialog action ended the run (skip default; auto-removal applies). The conversation to start, if the
    /// triggerer is a PC, is in <see cref="ScrEffects.Dialog"/>.</summary>
    Dialog,
    /// <summary>An object operand names a role the event does not have (no triggerer, attachee, extra or player); the
    /// original would pass a null handle (both fixed), the run stops with skip default.</summary>
    MissingObject,
    /// <summary>A computed global flag number outside 0..3199, which the original would access outside its flag store
    /// (both fixed); the run stops with skip default.</summary>
    IndexOutOfRange,
    /// <summary>The script is outside the admitted set; nothing ran.</summary>
    Refused,
    /// <summary>The event itself is not valid (a point outside 0..35, the dialog-override slot, a negative line).</summary>
    InvalidEvent,
    /// <summary>ARC-SCRIPT-018: a call script would nest deeper than <see cref="ScrRunOptions.MaxCallDepth"/>, or the dispatch's
    /// runs together executed more than <see cref="ScrRunOptions.MaxCallDepth"/> × <see cref="ScrRunOptions.MaxExecutedLines"/>
    /// lines (the original has no bound but its stack; project bounds); the dispatch ends with skip default and applies nothing.</summary>
    CallDepth,
}

/// <summary>A slot to store after the run: the object's slot for the point, or the tile's or sector's attachment.</summary>
public sealed record ScrWriteback(ScrSource Source, ScrObjectRef? Attachee, int Point, ScrSlot Slot);

/// <summary>A conversation for the caller to start after it applied the writeback (the original's dialog action stores
/// the slot before its start callback, 0x4472F1): the DLG of <paramref name="Script"/> at <paramref name="Node"/>, between the PC
/// triggerer and the attachee. A DLG end of 0 resumes the attachee's dialog slot at <paramref name="Line"/> + 1, and −N at
/// line N (the original runs the NPC's SAP_DIALOG slot, dialog_ui.c 400–407): for a talk that is this script, for a
/// conversation a deferred call started it is whatever the NPC's dialog slot holds.</summary>
public sealed record ScrDialogStart(ScrObjectRef Pc, ScrObjectRef? Npc, int Script, int Line, int Node);

public enum ScrCallKind { Schedule, Remove }

/// <summary>
/// A deferred call the run asks for, in action order (ARC-SCRIPT-010): <see cref="ScrCallKind.Schedule"/> runs
/// <paramref name="Script"/> from <paramref name="Line"/> with the stored triggerer and attachee (either may be absent)
/// after <paramref name="DelayMs"/> game milliseconds; <see cref="ScrCallKind.Remove"/> removes the first pending call
/// of <paramref name="Script"/> whose attachee is <paramref name="Attachee"/>.
/// </summary>
public sealed record ScrCallRequest(ScrCallKind Kind, int Script, int Line, ScrObjectRef? Triggerer, ScrObjectRef? Attachee, long DelayMs);

/// <summary>A deferred call firing: the named script from its file header, at the stored line, as a use event with
/// the stored roles and no extra object; nothing is written back except the attachee's use slot when the run starts a
/// conversation (E1, ARC-RES-042 section 3; see <see cref="ScrDispatcher.RunCall"/>).</summary>
public sealed record ScrCallEvent(int Script, int Line, ScrObjectRef? Triggerer, ScrObjectRef? Attachee);

/// <summary>
/// A print-line action (ARC-SCRIPT-013, type 24): line <paramref name="Line"/> of the DLG of <paramref name="Script"/>, the
/// running attachment's script (the number the run executes, as the dialog action names it: 2188 while a mind-controlled
/// NPC's attachment runs it, E6; the called script for a deferred call), with the action's second
/// number <paramref name="Kind"/> (its meaning on screen is not read, ARC-RES-055 U1). The original builds the line through
/// the dialog's NPC-line step, which also runs the line's action field, and hands the text to the interface (ledger IS2);
/// the host reads the DLG and decides whether it can show it. A run without an attachee prints nothing, as the original.
/// </summary>
public readonly record struct ScrPrintLine(int Script, int Line, int Kind);

/// <summary>What a script's sound action asks for (ARC-SCRIPT-040).</summary>
public enum ScrSoundKind
{
    /// <summary>Play sound (82): the effect <see cref="ScrSound.Id"/> once, not positional (volume 127, centre).</summary>
    Effect,
    /// <summary>Play sound on (83): the effect <see cref="ScrSound.Id"/> once on <see cref="ScrSound.Object"/>'s tile.</summary>
    OnObject,
    /// <summary>Play sound scheme (87): the scheme pair <see cref="ScrSound.Id"/> (music) and <see cref="ScrSound.Ambient"/>.</summary>
    Scheme,
}

/// <summary>
/// A sound action of a run (ARC-SCRIPT-040; checked in the 1.0.7.4 executable, docs/research/audio-evidence.md SN28): play sound
/// and play sound on name an effect id, play sound scheme a music and an ambient scheme as the session's scheme request takes them.
/// The original plays them at once; here the host plays them when it applies the run.
/// </summary>
public readonly record struct ScrSound(ScrSoundKind Kind, int Id, int Ambient = 0, ScrObjectRef? Object = null);

/// <summary>
/// ARC-SCRIPT-016: a set-auto-level-scheme action (139) for one object of its object operand (ledger OL30): the original writes the
/// number into the auto-level scheme of a PC or an NPC (0x4A6D90), with no range check, ignores any other object, and buys nothing
/// until the object's next level step. The host decides what the object is.
/// </summary>
public readonly record struct ScrSchemeRequest(ScrObjectRef Object, int Scheme);

/// <summary>
/// ARC-TECHNOLOGY-005: a start-schematic-UI action (129) for the object of its object operand (ledger SW8). The original calls an
/// interface callback slot that is never filled (TC2, both fixed): a PC opens the window as crafter and viewer, an NPC with its PC
/// leader as viewer, an NPC without a leader nothing. The host decides what the object is.
/// </summary>
public readonly record struct ScrSchematicRequest(ScrObjectRef Object);

/// <summary>ARC-UI-049: what a reading action shows: a book (64), a picture (65) or a plaque (114).</summary>
public enum ScrReadingKind { Book, Picture, Plaque }

/// <summary>
/// ARC-UI-049: a reading action (64 show book, 65 show picture, 114 show plaque; ledger WR rows) with its number operand (the start
/// line, or the art of a picture) and its viewer object. The original opens the reading window only for the local player, with no
/// item and no dead check; the host decides what the viewer is.
/// </summary>
public readonly record struct ScrReadingRequest(ScrReadingKind Kind, int Number, ScrObjectRef Viewer);

/// <summary>The final value of a global flag the run changed.</summary>
public readonly record struct ScrFlagWrite(int Index, bool Value);

/// <summary>
/// Every change of one dispatch, for the caller to apply together: the slot writeback (or the trap object to destroy
/// instead), the global flags in ascending order and the new story state. Empty when nothing changed.
/// </summary>
public sealed class ScrEffects
{
    internal ScrEffects(ScrWriteback? writeback, ScrObjectRef? destroyTrapObject, IReadOnlyList<ScrFlagWrite> globalFlags, int? storyState, ScrDialogStart? dialog = null,
        IReadOnlyList<ScrCallRequest>? calls = null, IReadOnlyList<ScrPrintLine>? lines = null, IReadOnlyList<ScrSound>? sounds = null,
        IReadOnlyList<ScrTeleport>? teleports = null, IReadOnlyList<ScrSchemeRequest>? schemes = null, IReadOnlyList<ScrSchematicRequest>? schematics = null,
        IReadOnlyList<ScrReadingRequest>? readings = null, IReadOnlyList<ScrSkillUse>? skillUses = null)
    {
        Writeback = writeback; DestroyTrapObject = destroyTrapObject; GlobalFlags = globalFlags; StoryState = storyState; Dialog = dialog;
        Calls = calls ?? Array.Empty<ScrCallRequest>();
        Lines = lines ?? Array.Empty<ScrPrintLine>();
        Sounds = sounds ?? Array.Empty<ScrSound>();
        Teleports = teleports ?? Array.Empty<ScrTeleport>();
        Schemes = schemes ?? Array.Empty<ScrSchemeRequest>();
        Schematics = schematics ?? Array.Empty<ScrSchematicRequest>();
        Readings = readings ?? Array.Empty<ScrReadingRequest>();
        SkillUses = skillUses ?? Array.Empty<ScrSkillUse>();
    }

    public static ScrEffects None { get; } = new(null, null, Array.Empty<ScrFlagWrite>(), null);
    public ScrWriteback? Writeback { get; }
    public ScrObjectRef? DestroyTrapObject { get; }
    public IReadOnlyList<ScrFlagWrite> GlobalFlags { get; }
    public int? StoryState { get; }
    /// <summary>The conversation to start, applied after everything else.</summary>
    public ScrDialogStart? Dialog { get; }
    /// <summary>The deferred calls to schedule or remove, in action order (ARC-SCRIPT-010).</summary>
    public IReadOnlyList<ScrCallRequest> Calls { get; }
    /// <summary>The lines to print, in action order (ARC-SCRIPT-013).</summary>
    public IReadOnlyList<ScrPrintLine> Lines { get; }
    /// <summary>The sounds to play and scheme pairs to request, in action order (ARC-SCRIPT-040).</summary>
    public IReadOnlyList<ScrSound> Sounds { get; }
    /// <summary>The teleports to request, in action order (ARC-SCRIPT-014).</summary>
    public IReadOnlyList<ScrTeleport> Teleports { get; }
    /// <summary>The auto-level schemes to set, in action order, one per object (ARC-SCRIPT-016).</summary>
    public IReadOnlyList<ScrSchemeRequest> Schemes { get; }
    /// <summary>The schematic windows to open, in action order (ARC-TECHNOLOGY-005).</summary>
    public IReadOnlyList<ScrSchematicRequest> Schematics { get; }
    /// <summary>ARC-UI-049: the reading actions in action order.</summary>
    public IReadOnlyList<ScrReadingRequest> Readings { get; }
    /// <summary>ARC-SKILLS-009: the use-item-with-skill requests in action order.</summary>
    public IReadOnlyList<ScrSkillUse> SkillUses { get; }
    public bool IsEmpty => Writeback is null && DestroyTrapObject is null && GlobalFlags.Count == 0 && StoryState is null && Dialog is null && Calls.Count == 0
        && Lines.Count == 0 && Sounds.Count == 0 && Teleports.Count == 0 && Schemes.Count == 0 && Schematics.Count == 0 && Readings.Count == 0 && SkillUses.Count == 0;
}

/// <summary>One executed line: the condition, whether it held, the action of the branch taken and where the run went
/// next (null when the line ended the run).</summary>
public readonly record struct ScrTraceStep(int Line, int Condition, bool Held, int Action, int? Next);

/// <summary>The result of one dispatch. <see cref="Script"/> is the number that ran (2188 for a mind-controlled NPC's
/// dialog), or null when nothing ran.</summary>
public sealed class ScrRunResult
{
    internal ScrRunResult(ScrOutcome outcome, ScrStop stop, int? script, int? stopLine, ScrRefusal? refusal, IReadOnlyList<ScrTraceStep> trace, ScrEffects effects)
    {
        Outcome = outcome; Stop = stop; Script = script; StopLine = stopLine; Refusal = refusal; Trace = trace; Effects = effects;
    }

    public ScrOutcome Outcome { get; }
    public ScrStop Stop { get; }
    public int? Script { get; }
    /// <summary>The line that ended the run, when one did.</summary>
    public int? StopLine { get; }
    public ScrRefusal? Refusal { get; }
    public IReadOnlyList<ScrTraceStep> Trace { get; }
    public ScrEffects Effects { get; }
}

/// <summary>How a dispatch runs: the rule mode's script switches, the admitted set (default <see cref="ScrAdmissionSet.S5"/>,
/// at most <see cref="ScrAdmissionSet.S12"/>, admitted for the lines reachable from the event's line), the executed-line
/// budget and the call depth.</summary>
public sealed record ScrRunOptions
{
    /// <summary>The original stops a run after 1,000 executed lines.</summary>
    public const int OriginalLineBudget = 1000;

    public ScrRuleProfile Rules { get; init; } = ScrRuleProfile.Steamwoven;
    public ScrAdmissionSet Admission { get; init; } = ScrAdmissionSet.S5;
    /// <summary>1..1000; lower values are for tests and tools.</summary>
    public int MaxExecutedLines { get; init; } = OriginalLineBudget;
    /// <summary>ARC-SCRIPT-018: how deep call script (7) may nest, 1..64 (the original's recursion is bounded only by its
    /// stack, ledger OV1). Each called run has its own line budget, and the whole dispatch at most this many budgets.</summary>
    public int MaxCallDepth { get; init; } = 16;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Rules);
        ArgumentNullException.ThrowIfNull(Admission);
        if (MaxExecutedLines is <= 0 or > OriginalLineBudget) throw new ArgumentOutOfRangeException(nameof(MaxExecutedLines), "The line budget is 1..1000.");
        if (MaxCallDepth is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(MaxCallDepth), "The call depth is 1..64.");
        if (!Admission.IsSubsetOf(ScrAdmissionSet.S12)) throw new ArgumentException("The interpreter runs only the commands of ScrAdmissionSet.S12.", nameof(Admission));
    }

    // A set with a guard condition needs a host that answers it, checked before any event runs.
    internal void Validate(IScrHost host)
    {
        Validate();
        if (host is not IScrObjectQueries && (Admission.Conditions.Any(ScrAdmissionSet.ObjectQueryConditions.Contains) || Admission.Actions.Any(ScrAdmissionSet.ObjectQueryActions.Contains)))
            throw new ArgumentException("A set with a guard condition or get object type needs a host that implements IScrObjectQueries.", nameof(Admission));
        if (host is not IScrSkillQueries && Admission.Actions.Contains(ScrCatalogue.Action.GetSkill))
            throw new ArgumentException("A set with get skill needs a host that implements IScrSkillQueries.", nameof(Admission));
    }
}
