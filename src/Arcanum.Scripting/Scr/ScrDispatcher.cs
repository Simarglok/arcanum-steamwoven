using Arcanum.Data.Scripts;

namespace Arcanum.Scripting.Scr;

/// <summary>
/// The SCR dispatcher core (ARC-SCRIPT-008, slice S2 of docs/research/scr-event-dispatcher.md): one entry point per
/// attachment source, each deciding, running and reporting one event. It reads the game through <see cref="IScrHost"/>
/// and changes nothing: the result carries every change, for the caller to apply as one step. A run executes only an
/// admitted program, so it never stops half-way on an unsupported command. Contract:
/// docs/scripting/scr-dispatcher-runtime.md.
/// </summary>
public static class ScrDispatcher
{
    /// <summary>The script a mind-controlled NPC's dialog event runs instead of its own (E6).</summary>
    public const int MindControlScript = 2188;

    /// <summary>The trap scripts the trap code runs instead of the interpreter: these numbers with a trap file flag
    /// (original <c>trap_script_execute</c> 0x4BCB70).</summary>
    public const int FirstTrapScript = 30000, LastTrapScript = 30007;

    /// <summary>
    /// An event on an object's slot (E2, E3, E4–E6): no slot or script 0 runs default; a use event first gives the
    /// triggerer a trap spot; then the script runs and a changed slot is written back, or a trap object whose script
    /// became 0 is destroyed instead.
    /// </summary>
    public static ScrRunResult RunObject(ScrObjectEvent e, IScrHost host, ScrRunOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(host);
        options ??= new();
        options.Validate();
        if (e.Point is < 0 or >= ScrPoint.Count || e.Point == ScrPoint.DialogOverride || e.Line < 0 || e.Attachee.Code is null || e.Triggerer is { Code: null } || e.Extra is { Code: null })
            return Ended(ScrOutcome.SkipDefault, ScrStop.InvalidEvent);
        var slot = host.Slot(e.Attachee, e.Point);
        if (slot is not { Script: not 0 } own) return Ended(ScrOutcome.RunDefault, ScrStop.NoScript);
        if (e.Point == ScrPoint.Use && e.Triggerer is { } user && host.CheckTrapBeforeUse(user, e.Attachee) == ScrTrapCheck.Spotted)
            return Ended(ScrOutcome.SkipDefault, ScrStop.TrapSpotted);

        var lookup = host.Script(own.Script);
        if (Unavailable(lookup) is { } missing) return Ended(ScrOutcome.SkipDefault, missing);
        var program = lookup.Program!;
        if (IsTrapScript(own.Script, program)) return Ended(ScrOutcome.SkipDefault, ScrStop.TrapScript);
        if (host.IsMonsterGenerator(e.Attachee)) return Ended(ScrOutcome.RunDefault, ScrStop.MonsterGenerator);

        var number = own.Script;
        // The auto-removal flag is read from the attachment's own script, as in the original.
        var ownFlags = program.FileFlags;
        if (e.Point == ScrPoint.Dialog)
        {
            var state = host.DialogState(e.Attachee);
            if ((state & ScrDialogState.Dead) != 0 && (program.FileFlags & ScrCatalogue.FileFlag.DeathSpeech) == 0)
                return Ended(ScrOutcome.RunDefault, ScrStop.DeadWithoutSpeech);
            if ((state & ScrDialogState.Surrendered) != 0 && (program.FileFlags & ScrCatalogue.FileFlag.SurrenderSpeech) == 0)
                return Ended(ScrOutcome.RunDefault, ScrStop.SurrenderedWithoutSpeech);
            if ((state & ScrDialogState.MindControlled) != 0)
            {
                number = MindControlScript;
                var swapped = host.Script(number);
                // The original checks the NPC's own file first, then opens the swapped one; a missing swap runs default (X5).
                if (Unavailable(swapped) is { } absent) return Ended(ScrOutcome.RunDefault, absent);
                program = swapped.Program!;
            }
        }

        var run = Execute(program, number, own, e.Line, ownFlags, host, options);
        if (run.Refusal is not null) return run.Result;
        var after = run.Slot;
        // A mind-controlled run keeps the NPC's own number whatever 2188 did to its own.
        if (number != own.Script) after = after with { Script = own.Script };
        ScrWriteback? writeback = null; ScrObjectRef? destroy = null;
        if (after != own)
        {
            if (after.Script == 0 && host.IsTrapObject(e.Attachee)) destroy = e.Attachee;
            else writeback = new(ScrSource.Object, e.Attachee, e.Point, after);
        }
        return run.With(writeback, destroy);
    }

    /// <summary>A tile script (E3 tile, X3): run for the entering critter; the changed attachment is written back, or
    /// discarded under <see cref="ScrRuleProfile.TileScriptChangesDiscarded"/>.</summary>
    public static ScrRunResult RunTile(ScrTileEvent e, IScrHost host, ScrRunOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        options ??= new();
        options.Validate();
        if (e.Triggerer.Code is null) return Ended(ScrOutcome.SkipDefault, ScrStop.InvalidEvent);
        return RunPlaced(ScrSource.Tile, e.Slot, host, options, discard: options.Rules.TileScriptChangesDiscarded);
    }

    /// <summary>A sector script on the player's entry (X3 sector): the changed number, flags and counters are written
    /// back in both modes.</summary>
    public static ScrRunResult RunSector(ScrSectorEvent e, IScrHost host, ScrRunOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        options ??= new();
        options.Validate();
        if (e.Player.Code is null) return Ended(ScrOutcome.SkipDefault, ScrStop.InvalidEvent);
        return RunPlaced(ScrSource.Sector, e.Slot, host, options, discard: false);
    }

    private static ScrRunResult RunPlaced(ScrSource source, ScrSlot slot, IScrHost host, ScrRunOptions options, bool discard)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (slot.Script == 0) return Ended(ScrOutcome.RunDefault, ScrStop.NoScript);
        var lookup = host.Script(slot.Script);
        if (Unavailable(lookup) is { } missing) return Ended(ScrOutcome.SkipDefault, missing);
        var program = lookup.Program!;
        if (IsTrapScript(slot.Script, program)) return Ended(ScrOutcome.SkipDefault, ScrStop.TrapScript);
        var run = Execute(program, slot.Script, slot, 0, program.FileFlags, host, options);
        if (run.Refusal is not null) return run.Result;
        return run.With(!discard && run.Slot != slot ? new ScrWriteback(source, null, ScrPoint.Use, run.Slot) : null, null);
    }

    private static bool IsTrapScript(int number, ScrProgram program) => number is >= FirstTrapScript and <= LastTrapScript &&
        (program.FileFlags & (ScrCatalogue.FileFlag.NonmagicalTrap | ScrCatalogue.FileFlag.MagicalTrap)) != 0;

    private static ScrStop? Unavailable(ScrScriptLookup lookup) => lookup?.Status switch
    {
        ScrScriptStatus.Found when lookup.Program is not null => null,
        ScrScriptStatus.Unreadable => ScrStop.UnreadableScript,
        ScrScriptStatus.Found => ScrStop.UnreadableScript,
        _ => ScrStop.MissingScript,
    };

    private static ScrRunResult Ended(ScrOutcome outcome, ScrStop stop) => new(outcome, stop, null, null, null, Array.Empty<ScrTraceStep>(), ScrEffects.None);

    private static Run Execute(ScrProgram program, int number, ScrSlot slot, int line, uint ownFileFlags, IScrHost host, ScrRunOptions options)
    {
        var admission = ScrAdmission.Admit(program, options.Admission);
        if (!admission.IsAdmitted)
            return new(new(ScrOutcome.Refused, ScrStop.Refused, number, null, admission.Refusal, Array.Empty<ScrTraceStep>(), ScrEffects.None), slot, admission.Refusal);
        return new ScrInterpreter(admission.Admitted!, number, slot, ownFileFlags, host, options).Run(line);
    }

    /// <summary>A finished run before its writeback is decided.</summary>
    internal sealed record Run(ScrRunResult Result, ScrSlot Slot, ScrRefusal? Refusal, IReadOnlyList<ScrFlagWrite>? Flags = null, int? Story = null)
    {
        internal ScrRunResult With(ScrWriteback? writeback, ScrObjectRef? destroy) => new(Result.Outcome, Result.Stop, Result.Script, Result.StopLine, null, Result.Trace,
            writeback is null && destroy is null && (Flags?.Count ?? 0) == 0 && Story is null ? ScrEffects.None : new ScrEffects(writeback, destroy, Array.AsReadOnly((Flags ?? Array.Empty<ScrFlagWrite>()).ToArray()), Story));
    }
}
