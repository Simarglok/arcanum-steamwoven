using Arcanum.Data.Scripts;

namespace Arcanum.Scripting.Scr;

/// <summary>
/// One run of an admitted program (ARC-RES-042 E7–E11): fresh local variables, the attachment's local flags and
/// counters in a private copy, a line loop with the original's budget, and every global change kept in an overlay that
/// the run itself reads back and the result hands to the caller. It supports exactly the commands of
/// <see cref="ScrAdmissionSet.S2"/>.
/// </summary>
internal sealed class ScrInterpreter
{
    private readonly ScrAdmittedProgram _admitted;
    private readonly int _number;
    private readonly ScrSlot _slot;
    private readonly uint _ownFileFlags;
    private readonly IScrHost _host;
    private readonly ScrRunOptions _options;
    private readonly int[] _locals = new int[ScrCatalogue.LocalSlots];
    private readonly SortedDictionary<int, bool> _flags = new();
    private readonly List<ScrTraceStep> _trace = new();
    private uint _localFlags, _counters;
    private int? _story, _storyBefore;
    // The original's "no change" sentinel for remove/change this script.
    private int _changedNumber = -1;

    internal ScrInterpreter(ScrAdmittedProgram admitted, int number, ScrSlot slot, uint ownFileFlags, IScrHost host, ScrRunOptions options)
    {
        _admitted = admitted; _number = number; _slot = slot; _ownFileFlags = ownFileFlags; _host = host; _options = options;
        _localFlags = slot.LocalFlags; _counters = slot.Counters;
    }

    private sealed class IndexStop(int line) : Exception { internal int Line { get; } = line; }

    private enum Step { Next, Jump, ReturnSkip, ReturnRun }

    internal ScrDispatcher.Run Run(int line)
    {
        var lines = _admitted.Program.Lines;
        var count = lines.Count;
        ScrStop stop = ScrStop.Budget; int? stopLine = null;
        try
        {
            var executed = 0;
            while (true)
            {
                if (executed == _options.MaxExecutedLines) { stop = ScrStop.Budget; stopLine = line; break; }
                // The original's line read compares unsigned, so any line outside 0..count−1 fails it.
                if (line < 0 || line >= count) { stop = ScrStop.LineOutOfRange; stopLine = line; break; }
                var row = lines[line];
                var always = row.Condition.Type == ScrCatalogue.Condition.True;
                var held = always || Condition(row.Condition, line);
                var action = held ? row.OnTrue : row.OnFalse;
                var (step, target) = Action(action, line);
                executed++;
                if (step == Step.Next)
                {
                    if (line < count - 1) { _trace.Add(new(line, row.Condition.Type, held, action.Type, line + 1)); line++; continue; }
                    _trace.Add(new(line, row.Condition.Type, held, action.Type, null));
                    stop = ScrStop.FellOffEnd; stopLine = line; break;
                }
                if (step == Step.Jump) { _trace.Add(new(line, row.Condition.Type, held, action.Type, target)); line = target; continue; }
                _trace.Add(new(line, row.Condition.Type, held, action.Type, null));
                stop = step == Step.ReturnRun ? ScrStop.ReturnRunDefault : ScrStop.ReturnSkipDefault; stopLine = line;
                break;
            }
        }
        catch (IndexStop s) { stop = ScrStop.IndexOutOfRange; stopLine = s.Line; }

        var number = _slot.Script;
        // A return, or running past the last line, clears an auto-removing script (E8); a budget, a bad line or a
        // project bound does not (E9).
        if (stop is ScrStop.ReturnSkipDefault or ScrStop.ReturnRunDefault or ScrStop.FellOffEnd && (_ownFileFlags & ScrCatalogue.FileFlag.AutoRemoving) != 0) number = 0;
        // Remove or change this script takes effect after the run, over the auto-removal (E10).
        if (_changedNumber != -1) number = _changedNumber;
        var outcome = stop == ScrStop.ReturnRunDefault ? ScrOutcome.RunDefault : ScrOutcome.SkipDefault;
        var flags = _flags.Where(p => p.Value != _host.GlobalFlag(p.Key)).Select(p => new ScrFlagWrite(p.Key, p.Value)).ToArray();
        int? story = _story is { } s2 && s2 != _storyBefore ? s2 : null;
        var result = new ScrRunResult(outcome, stop, _number, stopLine, null, Array.AsReadOnly(_trace.ToArray()), ScrEffects.None);
        return new(result, new ScrSlot(number, _localFlags, _counters), null, flags, story);
    }

    private bool Condition(ScrClause c, int line) => c.Type switch
    {
        ScrCatalogue.Condition.LocalFlag => ((_localFlags >> LocalFlag(Get(c, 0), line)) & 1) != 0,
        ScrCatalogue.Condition.Equal => Get(c, 0) == Get(c, 1),
        ScrCatalogue.Condition.LessOrEqual => Get(c, 0) <= Get(c, 1),
        ScrCatalogue.Condition.GlobalFlag => Flag(GlobalFlag(Get(c, 0), line)),
        _ => throw new InvalidOperationException("Condition outside the interpreter's set."),
    };

    private (Step, int) Action(ScrClause a, int line)
    {
        switch (a.Type)
        {
            case ScrCatalogue.Action.DoNothing: return (Step.Next, 0);
            case ScrCatalogue.Action.ReturnSkipDefault: return (Step.ReturnSkip, 0);
            case ScrCatalogue.Action.ReturnRunDefault: return (Step.ReturnRun, 0);
            case ScrCatalogue.Action.Goto: return (Step.Jump, Get(a, 0));
            case ScrCatalogue.Action.RemoveThisScript: _changedNumber = 0; return (Step.Next, 0);
            case ScrCatalogue.Action.ChangeThisScript:
            {
                // The original stores any value; −1 is its "no change" sentinel (admission refuses a literal outside
                // 0..99999 as project strictness).
                _changedNumber = Get(a, 0);
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.SetLocalFlag: _localFlags |= 1u << LocalFlag(Get(a, 0), line); return (Step.Next, 0);
            case ScrCatalogue.Action.ClearLocalFlag: _localFlags &= ~(1u << LocalFlag(Get(a, 0), line)); return (Step.Next, 0);
            case ScrCatalogue.Action.Assign: Set(a, Get(a, 1), line); return (Step.Next, 0);
            case ScrCatalogue.Action.Add: Set(a, unchecked(Get(a, 1) + Get(a, 2)), line); return (Step.Next, 0);
            case ScrCatalogue.Action.Subtract: Set(a, unchecked(Get(a, 1) - Get(a, 2)), line); return (Step.Next, 0);
            case ScrCatalogue.Action.Multiply: Set(a, unchecked(Get(a, 1) * Get(a, 2)), line); return (Step.Next, 0);
            case ScrCatalogue.Action.Divide:
            {
                // X7: the original stores the signed quotient and nothing for a divisor of 0 (CE adds instead, a CE
                // slip). int.MinValue / −1 faults the original's idiv; both modes store the wrapped quotient instead.
                var dividend = Get(a, 1); var divisor = Get(a, 2);
                if (divisor != 0) Set(a, divisor == -1 && dividend == int.MinValue ? int.MinValue : dividend / divisor, line);
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.GetStoryState: Set(a, Story(), line); return (Step.Next, 0);
            case ScrCatalogue.Action.SetStoryState:
            {
                // The original only ever raises the story state (0x4450A0).
                var value = Get(a, 0);
                if (value > Story()) _story = value;
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.SetGlobalFlag: _flags[GlobalFlag(Get(a, 0), line)] = true; return (Step.Next, 0);
            case ScrCatalogue.Action.ClearGlobalFlag: _flags[GlobalFlag(Get(a, 0), line)] = false; return (Step.Next, 0);
            default: throw new InvalidOperationException("Action outside the interpreter's set.");
        }
    }

    private int Get(ScrClause c, int slot)
    {
        var index = c.Value(slot);
        return (ScrValueKind)c.Kind(slot) switch
        {
            ScrValueKind.Counter => (int)((_counters >> (8 * index)) & 0xFF),
            ScrValueKind.LocalVariable => _locals[index],
            ScrValueKind.Number => index,
            ScrValueKind.GlobalFlag => Flag(index) ? 1 : 0,
            _ => throw new InvalidOperationException("Value kind outside the interpreter's set."),
        };
    }

    // Operand 0 is the store of every writing action in the set; admission refuses a number literal there.
    private void Set(ScrClause c, int value, int line)
    {
        var index = c.Value(0);
        switch ((ScrValueKind)c.Kind(0))
        {
            case ScrValueKind.Counter:
                // X8: the original masks a counter write to its byte (CE spills into the next counter, a CE slip).
                _counters = (_counters & ~(0xFFu << (8 * index))) | (((uint)value & 0xFF) << (8 * index));
                break;
            case ScrValueKind.LocalVariable: _locals[index] = value; break;
            case ScrValueKind.GlobalFlag: WriteFlag(GlobalFlag(index, line), value); break;
            default: throw new InvalidOperationException("Store outside the interpreter's set.");
        }
    }

    private void WriteFlag(int index, int value)
    {
        if (!_options.Rules.GlobalFlagWriteSpills) { _flags[index] = (value & 1) != 0; return; }
        // X10, the original: clear the flag's bit, then OR the value shifted to it into the flag's 32-flag word.
        var bit = index % 32; var word = index - bit;
        var bits = unchecked((uint)(value << bit));
        _flags[index] = ((bits >> bit) & 1) != 0;
        for (var j = bit + 1; j < 32; j++) if (((bits >> j) & 1) != 0) _flags[word + j] = true;
    }

    private bool Flag(int index) => _flags.TryGetValue(index, out var value) ? value : _host.GlobalFlag(index);

    private int Story() { _storyBefore ??= _host.StoryState(); return _story ??= _storyBefore.Value; }

    // The original shifts by the number with x86 shl (0x4473C2, 0x4473F5), which uses its low five bits.
    private static int LocalFlag(int index, int line) => index & (ScrCatalogue.LocalFlags - 1);
    private static int GlobalFlag(int index, int line) => index is >= 0 and < ScrCatalogue.GlobalFlags ? index : throw new IndexStop(line);
}
