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

/// <summary>The final value of a global flag the run changed.</summary>
public readonly record struct ScrFlagWrite(int Index, bool Value);

/// <summary>
/// Every change of one dispatch, for the caller to apply together: the slot writeback (or the trap object to destroy
/// instead), the global flags in ascending order and the new story state. Empty when nothing changed.
/// </summary>
public sealed class ScrEffects
{
    internal ScrEffects(ScrWriteback? writeback, ScrObjectRef? destroyTrapObject, IReadOnlyList<ScrFlagWrite> globalFlags, int? storyState, ScrDialogStart? dialog = null,
        IReadOnlyList<ScrCallRequest>? calls = null)
    {
        Writeback = writeback; DestroyTrapObject = destroyTrapObject; GlobalFlags = globalFlags; StoryState = storyState; Dialog = dialog;
        Calls = calls ?? Array.Empty<ScrCallRequest>();
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
    public bool IsEmpty => Writeback is null && DestroyTrapObject is null && GlobalFlags.Count == 0 && StoryState is null && Dialog is null && Calls.Count == 0;
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
/// admitted for the lines reachable from the event's line) and the executed-line budget.</summary>
public sealed record ScrRunOptions
{
    /// <summary>The original stops a run after 1,000 executed lines.</summary>
    public const int OriginalLineBudget = 1000;

    public ScrRuleProfile Rules { get; init; } = ScrRuleProfile.Steamwoven;
    public ScrAdmissionSet Admission { get; init; } = ScrAdmissionSet.S5;
    /// <summary>1..1000; lower values are for tests and tools.</summary>
    public int MaxExecutedLines { get; init; } = OriginalLineBudget;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Rules);
        ArgumentNullException.ThrowIfNull(Admission);
        if (MaxExecutedLines is <= 0 or > OriginalLineBudget) throw new ArgumentOutOfRangeException(nameof(MaxExecutedLines), "The line budget is 1..1000.");
        if (!Admission.IsSubsetOf(ScrAdmissionSet.S5)) throw new ArgumentException("The interpreter runs only the commands of ScrAdmissionSet.S5.", nameof(Admission));
    }
}
