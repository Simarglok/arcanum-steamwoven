using System.Collections.Frozen;
using Arcanum.Data.Scripts;

namespace Arcanum.Scripting.Scr;

/// <summary>Why a script was refused before it could run (docs/scripting/scr-program-admission.md).</summary>
public enum ScrRefusalCode
{
    /// <summary>A condition type outside the catalogue (0..52).</summary>
    UnknownCondition,
    /// <summary>An action type outside the catalogue (0..141).</summary>
    UnknownAction,
    /// <summary>A known condition type the admitted set does not run.</summary>
    ConditionNotAdmitted,
    /// <summary>A known action type the admitted set does not run.</summary>
    ActionNotAdmitted,
    /// <summary>A number operand whose kind byte is not one of the seven value kinds.</summary>
    UnknownValueKind,
    /// <summary>A known value kind the admitted set does not read.</summary>
    ValueKindNotAdmitted,
    /// <summary>An object operand whose kind byte is not one of the 24 focus kinds.</summary>
    UnknownFocusKind,
    /// <summary>A known focus kind the admitted set does not resolve.</summary>
    FocusKindNotAdmitted,
    /// <summary>A literal index outside its store: local flag 0..31, counter 0..3, local variable 0..9,
    /// global flag 0..3199, global variable 0..1999.</summary>
    IndexOutOfRange,
    /// <summary>An action stores into a number literal.</summary>
    WriteToNumber,
    /// <summary>A goto whose target is not a number literal.</summary>
    ComputedGoto,
    /// <summary>A literal script number for change this script outside 0..99999 (the original stores it as is).</summary>
    ScriptNumberOutOfRange,
    /// <summary>A literal goto below line 0 (a source reading takes −1..−4 as control codes, anomaly X6).</summary>
    NegativeGoto,
    /// <summary>A literal goto past the line after the last one.</summary>
    GotoBeyondEnd,
}

/// <summary>The first thing that keeps a script from being admitted: where it is and what it is.</summary>
/// <param name="Slot">The operand slot, for a refusal of one operand.</param>
/// <param name="Type">The clause's type number.</param>
/// <param name="Kind">The operand's kind byte, for an operand refusal.</param>
/// <param name="Value">The operand's value, for an index or goto refusal.</param>
public sealed record ScrRefusal(int Line, ScrClauseSide Side, ScrRefusalCode Code, int Type, int? Slot = null, byte? Kind = null, int? Value = null);

/// <summary>The condition, action, value and focus kinds an interpreter version runs. A set is
/// data: a later slice widens it by adding types, never by changing what admission checks.</summary>
public sealed class ScrAdmissionSet
{
    /// <param name="counterIndexWraps">ARC-WORLD-081: whether a counter operand may name any index, read as the original's counter
    /// reader takes it (PG6: index mod 4); otherwise an index outside 0..3 refuses the script.</param>
    public ScrAdmissionSet(string id, IEnumerable<int> conditions, IEnumerable<int> actions, IEnumerable<ScrValueKind> valueKinds, IEnumerable<ScrFocusKind> focusKinds,
        bool counterIndexWraps = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id; CounterIndexWraps = counterIndexWraps;
        Conditions = Freeze(conditions, ScrCatalogue.IsKnownCondition);
        Actions = Freeze(actions, ScrCatalogue.IsKnownAction);
        ValueKinds = Freeze(valueKinds, k => Enum.IsDefined(k));
        FocusKinds = Freeze(focusKinds, k => Enum.IsDefined(k));
    }

    private static IReadOnlySet<T> Freeze<T>(IEnumerable<T> values, Func<T, bool> known)
    {
        ArgumentNullException.ThrowIfNull(values);
        var set = values.ToHashSet();
        if (set.Any(v => !known(v))) throw new ArgumentOutOfRangeException(nameof(values), "An admitted type must be in the SCR catalogue.");
        return set.ToFrozenSet();
    }

    public string Id { get; }
    public IReadOnlySet<int> Conditions { get; }
    public IReadOnlySet<int> Actions { get; }
    public IReadOnlySet<ScrValueKind> ValueKinds { get; }
    public IReadOnlySet<ScrFocusKind> FocusKinds { get; }
    /// <summary>ARC-WORLD-081: a counter operand with an index outside 0..3 is admitted and reads counter (index mod 4), as the original's
    /// counter reader does (ledger PG6; the plant generator 30285 names counter 2,012,780,696, which reads counter 0).</summary>
    public bool CounterIndexWraps { get; }

    /// <summary>The first admitted set (ARC-SCRIPT-007, from the ARC-RES-045 census): control flow
    /// and the two returns, goto, local flags and counters, local variables, arithmetic, global
    /// flags and story state. No object operand.</summary>
    public static ScrAdmissionSet S1 { get; } = new("S1",
        [ScrCatalogue.Condition.True, ScrCatalogue.Condition.LocalFlag, ScrCatalogue.Condition.Equal, ScrCatalogue.Condition.LessOrEqual, ScrCatalogue.Condition.GlobalFlag],
        [ScrCatalogue.Action.DoNothing, ScrCatalogue.Action.ReturnSkipDefault, ScrCatalogue.Action.ReturnRunDefault, ScrCatalogue.Action.Goto,
         ScrCatalogue.Action.SetLocalFlag, ScrCatalogue.Action.ClearLocalFlag, ScrCatalogue.Action.Assign, ScrCatalogue.Action.Add,
         ScrCatalogue.Action.Subtract, ScrCatalogue.Action.Multiply, ScrCatalogue.Action.Divide, ScrCatalogue.Action.GetStoryState,
         ScrCatalogue.Action.SetStoryState, ScrCatalogue.Action.SetGlobalFlag, ScrCatalogue.Action.ClearGlobalFlag],
        [ScrValueKind.Counter, ScrValueKind.LocalVariable, ScrValueKind.Number, ScrValueKind.GlobalFlag], []);

    /// <summary>The set the dispatcher core runs (ARC-SCRIPT-008): <see cref="S1"/> plus remove this script and change
    /// this script, whose new number takes effect after the run.</summary>
    public static ScrAdmissionSet S2 { get; } = new("S2", S1.Conditions,
        S1.Actions.Append(ScrCatalogue.Action.RemoveThisScript).Append(ScrCatalogue.Action.ChangeThisScript), S1.ValueKinds, S1.FocusKinds);

    /// <summary>The dialogue set (ARC-SCRIPT-009): <see cref="S2"/> plus the follower and jilted conditions and the
    /// dialog action, with the single-object focus kinds triggerer, attachee, extra object and player.</summary>
    public static ScrAdmissionSet S3 { get; } = new("S3", S2.Conditions.Append(ScrCatalogue.Condition.FollowingPc).Append(ScrCatalogue.Condition.Jilted),
        S2.Actions.Append(ScrCatalogue.Action.Dialog), S2.ValueKinds,
        [ScrFocusKind.Triggerer, ScrFocusKind.Attachee, ScrFocusKind.ExtraObject, ScrFocusKind.Player]);

    /// <summary>The deferred-call set (ARC-SCRIPT-010): <see cref="S3"/> plus call script in (54) and remove script call
    /// (125). Call script at (55) stays outside: one installed line uses it, none a run reaches (ARC-RES-045), and its
    /// time-of-day arithmetic (TE2) is not settled.</summary>
    public static ScrAdmissionSet S5 { get; } = new("S5", S3.Conditions,
        S3.Actions.Append(ScrCatalogue.Action.CallScriptIn).Append(ScrCatalogue.Action.RemoveScriptCall), S3.ValueKinds, S3.FocusKinds);

    /// <summary>The print-line set (ARC-SCRIPT-013): <see cref="S5"/> plus print line (24), which hands a line of the running
    /// script's DLG and a number to the host as a <see cref="ScrPrintLine"/> effect (ledger IS1–IS3,
    /// docs/research/item-insert-script.md).</summary>
    public static ScrAdmissionSet S6 { get; } = new("S6", S5.Conditions, S5.Actions.Append(ScrCatalogue.Action.PrintLine), S5.ValueKinds, S5.FocusKinds);

    /// <summary>The sound set (ARC-SCRIPT-040): <see cref="S6"/> plus play sound (82), play sound on (83) and play sound scheme
    /// (87), which hand <see cref="ScrSound"/> effects to the host (audio study SN28).</summary>
    public static ScrAdmissionSet S7 { get; } = new("S7", S6.Conditions,
        S6.Actions.Append(ScrCatalogue.Action.PlaySound).Append(ScrCatalogue.Action.PlaySoundOn).Append(ScrCatalogue.Action.PlaySoundScheme), S6.ValueKinds, S6.FocusKinds);

    /// <summary>
    /// The teleport set (ARC-SCRIPT-014, the dispatcher part of plan slice S4): <see cref="S7"/> plus teleport (44) and
    /// fade-and-teleport (102), which hand <see cref="ScrTeleport"/> effects to the host, and the guard conditions of the
    /// conditional map transitions (ARC-RES-035): PC quest state (6), holds an item named (17), is named (20), wields an
    /// item named (21) and has a reputation (33), which the host answers through <see cref="IScrObjectQueries"/> (ledger
    /// SG1; the teleports TR1, TR2). Focus stays one object, so the conditions over focus groups are not admitted yet.
    /// </summary>
    public static ScrAdmissionSet S4 { get; } = new("S4", S7.Conditions.Concat([ScrCatalogue.Condition.PcQuestState, ScrCatalogue.Condition.HasItemNamed,
            ScrCatalogue.Condition.IsNamed, ScrCatalogue.Condition.IsWieldingItem, ScrCatalogue.Condition.HasReputation]),
        S7.Actions.Append(ScrCatalogue.Action.Teleport).Append(ScrCatalogue.Action.FadeAndTeleport), S7.ValueKinds, S7.FocusKinds);

    /// <summary>
    /// The scheme set (ARC-SCRIPT-016): <see cref="S4"/> plus set auto-level scheme (139), which hands one
    /// <see cref="ScrSchemeRequest"/> per object of its object operand to the host (ledger OL30). Its focus kinds stay the four
    /// single objects of <see cref="S3"/>; focus groups are not admitted yet.
    /// </summary>
    public static ScrAdmissionSet S8 { get; } = new("S8", S4.Conditions, S4.Actions.Append(ScrCatalogue.Action.SetAutoLevelScheme), S4.ValueKinds, S4.FocusKinds);

    /// <summary>
    /// The schematic set (ARC-TECHNOLOGY-005): <see cref="S8"/> plus start schematic UI (129), which hands one
    /// <see cref="ScrSchematicRequest"/> for its object operand to the host (ledger SW8; TC2, both fixed).
    /// </summary>
    public static ScrAdmissionSet S9 { get; } = new("S9", S8.Conditions, S8.Actions.Append(ScrCatalogue.Action.StartSchematicUi), S8.ValueKinds, S8.FocusKinds);

    /// <summary>
    /// The reading set (ARC-UI-049): <see cref="S9"/> plus show book (64), show picture (65) and show plaque (114), each handing one
    /// <see cref="ScrReadingRequest"/> (its number and its viewer object) to the host (ledger WR rows).
    /// </summary>
    public static ScrAdmissionSet S10 { get; } = new("S10", S9.Conditions,
        S9.Actions.Concat([ScrCatalogue.Action.ShowBook, ScrCatalogue.Action.ShowPicture, ScrCatalogue.Action.ShowPlaque]), S9.ValueKinds, S9.FocusKinds);

    /// <summary>
    /// The skill set (ARC-SKILLS-009, ledger SX1–SX4): <see cref="S10"/> plus get skill (47), which stores an object's effective skill
    /// level and needs a host that implements <see cref="IScrSkillQueries"/>, and use item with skill (79), which hands one request to
    /// the host.
    /// </summary>
    public static ScrAdmissionSet S11 { get; } = new("S11", S10.Conditions,
        S10.Actions.Concat([ScrCatalogue.Action.GetSkill, ScrCatalogue.Action.UseItemWithSkill]), S10.ValueKinds, S10.FocusKinds);

    /// <summary>
    /// The object-variable set (ARC-SCRIPT-018): <see cref="S11"/> plus call script (7), assign object (15) and get object type
    /// (34), and the run's ten object variables as a focus kind (11). Call script runs another script at once with the same
    /// set (ledger OV1); assign object writes an object into the run's triggerer, attachee, extra object or an object variable
    /// (OV2); get object type writes an object's type into a number store, which the host answers through
    /// <see cref="IScrObjectQueries.ObjectType"/> (OV3).
    /// </summary>
    public static ScrAdmissionSet S12 { get; } = new("S12", S11.Conditions,
        S11.Actions.Concat([ScrCatalogue.Action.CallScript, ScrCatalogue.Action.AssignObject, ScrCatalogue.Action.GetObjectType]), S11.ValueKinds,
        S11.FocusKinds.Append(ScrFocusKind.LocalObject));

    /// <summary>
    /// The story set (ARC-SCRIPT-019): <see cref="S12"/> plus set PC quest state (16), mark map location (49), set rumor (50), add
    /// reputation (72) and remove reputation (73), which hand <see cref="ScrStateRequest"/> effects to the host (ledger SS2–SS5).
    /// </summary>
    public static ScrAdmissionSet S13 { get; } = new("S13", S12.Conditions,
        S12.Actions.Concat([ScrCatalogue.Action.SetPcQuestState, ScrCatalogue.Action.MarkMapLocation, ScrCatalogue.Action.SetRumor,
            ScrCatalogue.Action.AddReputation, ScrCatalogue.Action.RemoveReputation]), S12.ValueKinds, S12.FocusKinds);

    /// <summary>
    /// The item-effect set (ARC-SCRIPT-042, ledger WE1–WE6): <see cref="S13"/> plus the conditions identified (47) and items being
    /// rewielded (50), which the host answers through <see cref="IScrItemQueries"/>, and add effect (77) and remove effect (78), which hand
    /// <see cref="ScrEffectRequest"/> effects to the host, call-ex (81), which runs an object's attachment at once (its slot change comes
    /// back in <see cref="ScrEffects.SlotWrites"/>), the magick-tech adjustment (91), which stores a value scaled by an item's power ratio
    /// for its owner, and play script eye candy (120), a presentation effect.
    /// </summary>
    public static ScrAdmissionSet S14 { get; } = new("S14", S13.Conditions.Concat([ScrCatalogue.Condition.Identified, ScrCatalogue.Condition.ItemsBeingRewielded]),
        S13.Actions.Concat([ScrCatalogue.Action.AddEffect, ScrCatalogue.Action.RemoveEffect, ScrCatalogue.Action.CallScriptEx,
            ScrCatalogue.Action.MagickTechAdjustment, ScrCatalogue.Action.PlayScriptEyeCandy]), S13.ValueKinds, S13.FocusKinds);

    /// <summary>
    /// The world-object set (ARC-WORLD-081, the plant generator of template sectors, docs/research/template-sector-plants.md):
    /// <see cref="S14"/> plus random (37, PG5), loop for (18) and loop end (19, PG7), and create object (52, PG1), which hands one
    /// <see cref="ScrCreateRequest"/> to the host; the focus kinds "current looped object" (10) and "every scenery in vicinity" (15, PG3),
    /// which the host answers through <see cref="IScrWorldQueries"/>; and a counter operand with any index (<see cref="CounterIndexWraps"/>,
    /// PG6).
    /// </summary>
    public static ScrAdmissionSet S15 { get; } = new("S15", S14.Conditions,
        S14.Actions.Concat([ScrCatalogue.Action.Random, ScrCatalogue.Action.LoopFor, ScrCatalogue.Action.LoopEnd, ScrCatalogue.Action.CreateObject]), S14.ValueKinds,
        S14.FocusKinds.Concat([ScrFocusKind.CurrentLoopedObject, ScrFocusKind.EverySceneryInVicinity]), counterIndexWraps: true);

    /// <summary>
    /// The object state set (ARC-WORLD-114): <see cref="S15"/> plus toggle state (56) and kill (58), which hand one
    /// <see cref="ScrObjectStateRequest"/> per object of their object operand to the host (ledger SU6, PM1).
    /// </summary>
    public static ScrAdmissionSet S16 { get; } = new("S16", S15.Conditions, S15.Actions.Concat([ScrCatalogue.Action.ToggleState, ScrCatalogue.Action.Kill]),
        S15.ValueKinds, S15.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The critter vicinity set (ARC-SCRIPT-047): <see cref="S16"/> plus the focus kinds everyone (8) and anyone (9) in the vicinity, the
    /// PCs and NPCs around the attachee (<see cref="IScrWorldQueries.CrittersInVicinity"/>, ledger TG4), in actions only (a condition
    /// over a list is not modelled). With call-ex of an NPC's dialog point (TG5) it runs the heartbeat that opens a talk (1100).
    /// </summary>
    public static ScrAdmissionSet S17 { get; } = new("S17", S16.Conditions, S16.Actions, S16.ValueKinds,
        S16.FocusKinds.Concat([ScrFocusKind.EveryoneInVicinity, ScrFocusKind.AnyoneInVicinity]), counterIndexWraps: true);

    /// <summary>The actions of <see cref="S15"/> a host answers through <see cref="IScrWorldQueries"/>.</summary>
    public static IReadOnlySet<int> WorldQueryActions { get; } = new HashSet<int> { ScrCatalogue.Action.Random, ScrCatalogue.Action.LoopFor };

    /// <summary>The focus kinds of <see cref="S15"/> a host answers through <see cref="IScrWorldQueries"/>.</summary>
    public static IReadOnlySet<ScrFocusKind> WorldQueryFocusKinds { get; } = new HashSet<ScrFocusKind>
        { ScrFocusKind.EverySceneryInVicinity, ScrFocusKind.EveryoneInVicinity, ScrFocusKind.AnyoneInVicinity };

    /// <summary>The conditions of <see cref="S14"/> a host answers through <see cref="IScrItemQueries"/>.</summary>
    public static IReadOnlySet<int> ItemQueryConditions { get; } = new HashSet<int> { ScrCatalogue.Condition.Identified, ScrCatalogue.Condition.ItemsBeingRewielded };

    /// <summary>The conditions of <see cref="S4"/> a host answers through <see cref="IScrObjectQueries"/>.</summary>
    public static IReadOnlySet<int> ObjectQueryConditions { get; } = new HashSet<int> { ScrCatalogue.Condition.PcQuestState, ScrCatalogue.Condition.HasItemNamed,
        ScrCatalogue.Condition.IsNamed, ScrCatalogue.Condition.IsWieldingItem, ScrCatalogue.Condition.HasReputation };

    /// <summary>The actions of <see cref="S11"/> a host answers through <see cref="IScrObjectQueries"/>.</summary>
    public static IReadOnlySet<int> ObjectQueryActions { get; } = new HashSet<int> { ScrCatalogue.Action.GetObjectType };

    /// <summary>Whether every type and kind of this set is also in <paramref name="other"/>.</summary>
    public bool IsSubsetOf(ScrAdmissionSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Conditions.All(other.Conditions.Contains) && Actions.All(other.Actions.Contains) && ValueKinds.All(other.ValueKinds.Contains) && FocusKinds.All(other.FocusKinds.Contains)
            && (!CounterIndexWraps || other.CounterIndexWraps);
    }
}

/// <summary>A script admitted as a whole for one set: every clause it could run is inside it.
/// Only <see cref="ScrAdmission.Admit"/> creates one, so holding it is the proof of admission.</summary>
public sealed class ScrAdmittedProgram
{
    internal ScrAdmittedProgram(ScrProgram program, ScrAdmissionSet set, int? entryLine = null, IReadOnlyList<int>? region = null)
    {
        Program = program; Set = set; EntryLine = entryLine; Region = region ?? Array.AsReadOnly(Enumerable.Range(0, program.Count).ToArray());
    }

    public ScrProgram Program { get; }
    public ScrAdmissionSet Set { get; }
    /// <summary>The line a region admission started from, or null for the whole script.</summary>
    public int? EntryLine { get; }
    /// <summary>The admitted lines in ascending order: every line, or the lines a run from <see cref="EntryLine"/> can reach.</summary>
    public IReadOnlyList<int> Region { get; }
}

/// <summary>Exactly one of <see cref="Admitted"/> and <see cref="Refusal"/>.</summary>
public sealed class ScrAdmissionResult
{
    private ScrAdmissionResult(ScrAdmittedProgram? admitted, ScrRefusal? refusal) { Admitted = admitted; Refusal = refusal; }
    internal static ScrAdmissionResult Of(ScrAdmittedProgram admitted) => new(admitted, null);
    internal static ScrAdmissionResult Of(ScrRefusal refusal) => new(null, refusal);
    public ScrAdmittedProgram? Admitted { get; }
    public ScrRefusal? Refusal { get; }
    public bool IsAdmitted => Admitted is not null;
}

/// <summary>Static whole-script admission: a script runs only if every line can run under the
/// set, so no run ever stops half-way on something unsupported. It checks every line, not only
/// those an entry reaches, and reports the first refusal in line order: the condition, then the
/// true action, then the false action; within a clause its type, then its operands left to right
/// (slot 0's index, write and jump rules right after its kind).</summary>
public static class ScrAdmission
{
    public static ScrAdmissionResult Admit(ScrProgram program, ScrAdmissionSet set)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(set);
        foreach (var line in program.Lines)
        {
            var refusal = Check(line, ScrClauseSide.Condition, line.Condition, set, program.Count)
                ?? Check(line, ScrClauseSide.OnTrue, line.OnTrue, set, program.Count)
                // The always-true condition never runs its false action, so its bytes are not read.
                ?? (line.Condition.Type == ScrCatalogue.Condition.True ? null : Check(line, ScrClauseSide.OnFalse, line.OnFalse, set, program.Count));
            if (refusal is not null) return ScrAdmissionResult.Of(refusal);
        }
        return ScrAdmissionResult.Of(new ScrAdmittedProgram(program, set));
    }

    /// <summary>
    /// Region admission (ARC-SCRIPT-009): the lines a run that starts at <paramref name="entryLine"/> can reach, found
    /// by following each admitted action (a literal goto, the next line, or the end of the run for a return or a
    /// dialog action), all run under the set. The run of an admitted region can only reach lines inside it, so it still
    /// never stops half-way on something unsupported; lines outside it are never read. The refusal reported is the
    /// lowest-numbered refused line among those reached through admitted lines (a refused line's successors are not
    /// explored), with the clause order of <see cref="Admit"/>. An entry outside the script admits an empty region
    /// (such a run ends on an unreadable line).
    /// </summary>
    public static ScrAdmissionResult AdmitFrom(ScrProgram program, ScrAdmissionSet set, int entryLine)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(set);
        var count = program.Count;
        var region = new SortedSet<int>();
        var work = new Stack<int>();
        if (entryLine >= 0 && entryLine < count) { region.Add(entryLine); work.Push(entryLine); }
        var refusals = new List<ScrRefusal>();
        while (work.TryPop(out var at))
        {
            var line = program.Lines[at];
            var always = line.Condition.Type == ScrCatalogue.Condition.True;
            var refusal = Check(line, ScrClauseSide.Condition, line.Condition, set, count)
                ?? Check(line, ScrClauseSide.OnTrue, line.OnTrue, set, count)
                ?? (always ? null : Check(line, ScrClauseSide.OnFalse, line.OnFalse, set, count));
            if (refusal is not null) { refusals.Add(refusal); continue; }
            foreach (var action in always ? new[] { line.OnTrue } : [line.OnTrue, line.OnFalse])
                foreach (var next in Successors(program, action, at))
                    if (region.Add(next)) work.Push(next);
        }
        if (refusals.Count > 0) return ScrAdmissionResult.Of(refusals.OrderBy(r => r.Line).First());
        return ScrAdmissionResult.Of(new ScrAdmittedProgram(program, set, entryLine, Array.AsReadOnly(region.ToArray())));
    }

    // Successor lines of an admitted action; a line at the count or past it is the end of the run, not a line.
    private static IEnumerable<int> Successors(ScrProgram program, ScrClause action, int line)
    {
        var count = program.Count;
        IEnumerable<int> lines = action.Type switch
        {
            ScrCatalogue.Action.ReturnSkipDefault or ScrCatalogue.Action.ReturnRunDefault or ScrCatalogue.Action.Dialog => [],
            ScrCatalogue.Action.Goto => [action.Value(0)],
            // PG7: a loop for goes on with the next line, or with an empty list after its loop end.
            ScrCatalogue.Action.LoopFor => [line + 1, AfterLoopEnd(program, line)],
            // A loop end goes on with the next line, or jumps back to the line after the loop for that filled the list: any loop for's.
            ScrCatalogue.Action.LoopEnd => program.Lines.Where(l => IsLoopFor(l.OnTrue) || l.Condition.Type != ScrCatalogue.Condition.True && IsLoopFor(l.OnFalse))
                .Select(l => l.Number + 1).Prepend(line + 1),
            _ => [line + 1],
        };
        return lines.Where(next => next < count);

        static bool IsLoopFor(ScrClause clause) => clause.Type == ScrCatalogue.Action.LoopFor;
    }

    /// <summary>
    /// PG7 (0x44BC60): where a loop for with an empty list goes on: the line after the first line after it whose condition is "true" and
    /// whose true action is loop end; without one, the line after the line count (the original's scan stops at the first line it cannot
    /// read and returns the line after it), which ends the run as a line outside the script.
    /// </summary>
    [OriginalFunction(0x44BC60, OriginalFunctionScope.Full)]
    internal static int AfterLoopEnd(ScrProgram program, int loopFor)
    {
        for (var at = loopFor + 1; at < program.Count; at++)
        {
            var row = program.Lines[at];
            if (row.Condition.Type == ScrCatalogue.Condition.True && row.OnTrue.Type == ScrCatalogue.Action.LoopEnd) return at + 1;
        }
        return program.Count + 1;
    }

    private static ScrRefusal? Check(ScrLine line, ScrClauseSide side, ScrClause clause, ScrAdmissionSet set, int count)
    {
        var condition = side == ScrClauseSide.Condition;
        var type = clause.Type;
        if (condition ? !ScrCatalogue.IsKnownCondition(type) : !ScrCatalogue.IsKnownAction(type))
            return new(line.Number, side, condition ? ScrRefusalCode.UnknownCondition : ScrRefusalCode.UnknownAction, type);
        if (!(condition ? set.Conditions : set.Actions).Contains(type))
            return new(line.Number, side, condition ? ScrRefusalCode.ConditionNotAdmitted : ScrRefusalCode.ActionNotAdmitted, type);
        var signature = ScrCatalogue.Signature(side, type)!;
        for (var slot = 0; slot < ScrClause.Operands; slot++)
        {
            var kind = clause.Kind(slot); var value = clause.Value(slot);
            ScrRefusal Refuse(ScrRefusalCode code, bool withValue = false) => new(line.Number, side, code, type, slot, kind, withValue ? value : null);
            switch (signature[slot])
            {
                case ScrOperandRole.Number:
                    if (!ScrCatalogue.IsKnownValueKind(kind)) return Refuse(ScrRefusalCode.UnknownValueKind);
                    if (!set.ValueKinds.Contains((ScrValueKind)kind)) return Refuse(ScrRefusalCode.ValueKindNotAdmitted);
                    if (!(set.CounterIndexWraps && (ScrValueKind)kind == ScrValueKind.Counter) && !InStore((ScrValueKind)kind, value))
                        return Refuse(ScrRefusalCode.IndexOutOfRange, true);
                    break;
                case ScrOperandRole.Object:
                    if (!ScrCatalogue.IsKnownFocusKind(kind)) return Refuse(ScrRefusalCode.UnknownFocusKind);
                    if (!set.FocusKinds.Contains((ScrFocusKind)kind)) return Refuse(ScrRefusalCode.FocusKindNotAdmitted);
                    if ((ScrFocusKind)kind == ScrFocusKind.LocalObject && value is < 0 or >= ScrCatalogue.LocalSlots) return Refuse(ScrRefusalCode.IndexOutOfRange, true);
                    // ARC-SCRIPT-047: a condition tests every object of a list focus kind (the "any" kinds some of them, SG6); the
                    // interpreter reads a condition's object operand as one object, so the critter vicinity kinds are admitted in
                    // actions only (an action's single object is the list's first, 0x44B960; loop for takes the list).
                    if (condition && (ScrFocusKind)kind is ScrFocusKind.EveryoneInVicinity or ScrFocusKind.AnyoneInVicinity) return Refuse(ScrRefusalCode.FocusKindNotAdmitted);
                    break;
            }
            // Get skill stores into its third operand (SX1).
            if (!condition && type == ScrCatalogue.Action.GetSkill && slot == 2 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // Get object type stores into its number operand 1 (ARC-SCRIPT-018).
            if (!condition && type == ScrCatalogue.Action.GetObjectType && slot == 1 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // Random stores into its number operand 2 (ARC-WORLD-081, PG5).
            if (!condition && type == ScrCatalogue.Action.Random && slot == 2 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // The magick-tech adjustment stores into its number operand 3 (ARC-SCRIPT-042, WE2).
            if (!condition && type == ScrCatalogue.Action.MagickTechAdjustment && slot == 3 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // Slot 0 of these types is itself an index, a store or a jump target.
            if (slot != 0) continue;
            var literal = (ScrValueKind)kind == ScrValueKind.Number;
            if (FlagIndexLimit(condition, type) is { } limit && literal && (value < 0 || value >= limit)) return Refuse(ScrRefusalCode.IndexOutOfRange, true);
            if (!condition && Writes(type) && literal) return Refuse(ScrRefusalCode.WriteToNumber);
            if (!condition && type == ScrCatalogue.Action.ChangeThisScript && literal && value is < 0 or > ScrCatalogue.MaxScriptNumber) return Refuse(ScrRefusalCode.ScriptNumberOutOfRange, true);
            if (!condition && type == ScrCatalogue.Action.Goto)
            {
                if (!literal) return Refuse(ScrRefusalCode.ComputedGoto);
                // A goto to exactly the line count is the end of the script: skip default, without auto-removal.
                if (value < 0) return Refuse(ScrRefusalCode.NegativeGoto, true);
                if (value > count) return Refuse(ScrRefusalCode.GotoBeyondEnd, true);
            }
        }
        return null;
    }

    private static bool InStore(ScrValueKind kind, int index) => kind switch
    {
        ScrValueKind.Counter => index is >= 0 and < ScrCatalogue.Counters,
        ScrValueKind.GlobalVariable => index is >= 0 and < ScrCatalogue.GlobalVariables,
        ScrValueKind.LocalVariable => index is >= 0 and < ScrCatalogue.LocalSlots,
        ScrValueKind.GlobalFlag => index is >= 0 and < ScrCatalogue.GlobalFlags,
        _ => true,
    };

    private static int? FlagIndexLimit(bool condition, int type) => condition
        ? type switch { ScrCatalogue.Condition.LocalFlag => ScrCatalogue.LocalFlags, ScrCatalogue.Condition.GlobalFlag => ScrCatalogue.GlobalFlags, _ => null }
        : type switch { ScrCatalogue.Action.SetLocalFlag or ScrCatalogue.Action.ClearLocalFlag => ScrCatalogue.LocalFlags, ScrCatalogue.Action.SetGlobalFlag or ScrCatalogue.Action.ClearGlobalFlag => ScrCatalogue.GlobalFlags, _ => null };

    // Actions whose operand 0 is the store they write.
    private static bool Writes(int type) => type is ScrCatalogue.Action.Assign or ScrCatalogue.Action.Add or ScrCatalogue.Action.Subtract
        or ScrCatalogue.Action.Multiply or ScrCatalogue.Action.Divide or ScrCatalogue.Action.GetStoryState;
}
