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
    /// <summary>A literal goto below line 0 (CE reads −1..−4 as control codes, anomaly X6).</summary>
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
    public ScrAdmissionSet(string id, IEnumerable<int> conditions, IEnumerable<int> actions, IEnumerable<ScrValueKind> valueKinds, IEnumerable<ScrFocusKind> focusKinds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
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
}

/// <summary>A script admitted as a whole for one set: every clause it could run is inside it.
/// Only <see cref="ScrAdmission.Admit"/> creates one, so holding it is the proof of admission.</summary>
public sealed class ScrAdmittedProgram
{
    internal ScrAdmittedProgram(ScrProgram program, ScrAdmissionSet set) { Program = program; Set = set; }
    public ScrProgram Program { get; }
    public ScrAdmissionSet Set { get; }
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
                    if (!InStore((ScrValueKind)kind, value)) return Refuse(ScrRefusalCode.IndexOutOfRange, true);
                    break;
                case ScrOperandRole.Object:
                    if (!ScrCatalogue.IsKnownFocusKind(kind)) return Refuse(ScrRefusalCode.UnknownFocusKind);
                    if (!set.FocusKinds.Contains((ScrFocusKind)kind)) return Refuse(ScrRefusalCode.FocusKindNotAdmitted);
                    if ((ScrFocusKind)kind == ScrFocusKind.LocalObject && value is < 0 or >= ScrCatalogue.LocalSlots) return Refuse(ScrRefusalCode.IndexOutOfRange, true);
                    break;
            }
            // Slot 0 of these types is itself an index, a store or a jump target.
            if (slot != 0) continue;
            var literal = (ScrValueKind)kind == ScrValueKind.Number;
            if (FlagIndexLimit(condition, type) is { } limit && literal && (value < 0 || value >= limit)) return Refuse(ScrRefusalCode.IndexOutOfRange, true);
            if (!condition && Writes(type) && literal) return Refuse(ScrRefusalCode.WriteToNumber);
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
