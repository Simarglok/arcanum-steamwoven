using Arcanum.Data.Scripts;

namespace Arcanum.Scripting.Scr;

/// <summary>
/// One run of an admitted program (ARC-RES-042 E7–E11): fresh local variables, the attachment's local flags and
/// counters in a private copy, a line loop with the original's budget, and every global change kept in an overlay that
/// the run itself reads back and the result hands to the caller. It supports exactly the commands of
/// <see cref="ScrAdmissionSet.S11"/>.
/// </summary>
internal sealed class ScrInterpreter
{
    private readonly ScrAdmittedProgram _admitted;
    private readonly int _number;
    private readonly ScrSlot _slot;
    private readonly uint _ownFileFlags;
    private readonly IScrHost _host;
    // OV2: assign object can write the run's triggerer, attachee and extra object, so the roles are the run's own copy.
    private ScrDispatcher.ScrRoles _roles;
    // OV2: the run's ten object variables (focus kind 11), empty at the start of every run.
    private readonly ScrObjectRef?[] _objects = new ScrObjectRef?[ScrCatalogue.LocalSlots];
    // The effects and the global overlay of one dispatch; a called script (OV1) adds to its caller's, as the original's
    // called run changes the same game.
    private sealed class Shared
    {
        public ScrDialogStart? Dialog;
        public readonly List<ScrCallRequest> Calls = new();
        public readonly List<ScrPrintLine> Lines = new();
        public readonly List<ScrSound> Sounds = new();
        public readonly List<ScrTeleport> Teleports = new();
        public readonly List<ScrSchemeRequest> Schemes = new();
        public readonly List<ScrSchematicRequest> Schematics = new();
        public readonly List<ScrReadingRequest> Readings = new();
        public readonly SortedDictionary<int, bool> Flags = new();
        public int? Story, StoryBefore;
        // Lines executed by every run of the dispatch (a project bound across nested calls).
        public int Executed;
    }

    private readonly Shared _shared;
    private readonly int _depth;
    private ScrDialogStart? _dialog { get => _shared.Dialog; set => _shared.Dialog = value; }
    private List<ScrCallRequest> _calls => _shared.Calls;
    private List<ScrPrintLine> _lines => _shared.Lines;
    private List<ScrSound> _sounds => _shared.Sounds;
    private List<ScrTeleport> _teleports => _shared.Teleports;
    private List<ScrSchemeRequest> _schemes => _shared.Schemes;
    private List<ScrSchematicRequest> _schematics => _shared.Schematics;
    private List<ScrReadingRequest> _readings => _shared.Readings;
    private SortedDictionary<int, bool> _flags => _shared.Flags;
    private int? _story { get => _shared.Story; set => _shared.Story = value; }
    private int? _storyBefore { get => _shared.StoryBefore; set => _shared.StoryBefore = value; }
    private readonly ScrRunOptions _options;
    private readonly int[] _locals = new int[ScrCatalogue.LocalSlots];
    private readonly List<ScrTraceStep> _trace = new();
    private uint _localFlags, _counters;
    // The original's "no change" sentinel for remove/change this script.
    private int _changedNumber = -1;

    internal ScrInterpreter(ScrAdmittedProgram admitted, int number, ScrSlot slot, uint ownFileFlags, ScrDispatcher.ScrRoles roles, IScrHost host, ScrRunOptions options)
        : this(admitted, number, slot, ownFileFlags, roles, host, options, new Shared(), 0) { }

    private ScrInterpreter(ScrAdmittedProgram admitted, int number, ScrSlot slot, uint ownFileFlags, ScrDispatcher.ScrRoles roles, IScrHost host, ScrRunOptions options,
        Shared shared, int depth)
    {
        _admitted = admitted; _number = number; _slot = slot; _ownFileFlags = ownFileFlags; _roles = roles; _host = host; _options = options;
        _localFlags = slot.LocalFlags; _counters = slot.Counters; _shared = shared; _depth = depth;
    }

    private class IndexStop(int line) : Exception { internal int Line { get; } = line; }
    private sealed class MissingObjectStop(int line) : IndexStop(line);
    // A called script outside the set, or unreadable, refuses the whole dispatch; a nesting deeper than the bound ends it.
    private sealed class CallRefusedStop(ScrRefusal? refusal, int script, ScrStop stop) : Exception
    {
        internal ScrRefusal? Refusal { get; } = refusal;
        internal int Script { get; } = script;
        internal ScrStop Stop { get; } = stop;
    }

    private enum Step { Next, Jump, ReturnSkip, ReturnRun, Dialog }

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
                // The original bounds each run, not a chain of calls; the project bounds the dispatch as well.
                if (++_shared.Executed > _options.MaxCallDepth * _options.MaxExecutedLines) throw new CallRefusedStop(null, _number, ScrStop.CallDepth);
                if (step == Step.Next)
                {
                    if (line < count - 1) { _trace.Add(new(line, row.Condition.Type, held, action.Type, line + 1)); line++; continue; }
                    _trace.Add(new(line, row.Condition.Type, held, action.Type, null));
                    stop = ScrStop.FellOffEnd; stopLine = line; break;
                }
                if (step == Step.Jump) { _trace.Add(new(line, row.Condition.Type, held, action.Type, target)); line = target; continue; }
                _trace.Add(new(line, row.Condition.Type, held, action.Type, null));
                stop = step switch { Step.ReturnRun => ScrStop.ReturnRunDefault, Step.Dialog => ScrStop.Dialog, _ => ScrStop.ReturnSkipDefault }; stopLine = line;
                break;
            }
        }
        catch (MissingObjectStop s) { stop = ScrStop.MissingObject; stopLine = s.Line; }
        catch (IndexStop s) { stop = ScrStop.IndexOutOfRange; stopLine = s.Line; }
        catch (CallRefusedStop s) when (_depth == 0)
        {
            // Nothing of the dispatch applies: a refused callee as a refusal of the run, a too deep nesting as a project stop.
            if (s.Stop == ScrStop.Refused)
                return new(new(ScrOutcome.Refused, ScrStop.Refused, s.Script, null, s.Refusal, Array.Empty<ScrTraceStep>(), ScrEffects.None), _slot, s.Refusal);
            return new(new(ScrOutcome.SkipDefault, s.Stop, _number, null, null, Array.AsReadOnly(_trace.ToArray()), ScrEffects.None), _slot, null);
        }

        var number = _slot.Script;
        // A return, or running past the last line, clears an auto-removing script (E8); a budget, a bad line or a
        // project bound does not (E9).
        if (stop is ScrStop.ReturnSkipDefault or ScrStop.ReturnRunDefault or ScrStop.FellOffEnd or ScrStop.Dialog && (_ownFileFlags & ScrCatalogue.FileFlag.AutoRemoving) != 0) number = 0;
        // Remove or change this script takes effect after the run, over the auto-removal (E10).
        if (_changedNumber != -1) number = _changedNumber;
        var outcome = stop == ScrStop.ReturnRunDefault ? ScrOutcome.RunDefault : ScrOutcome.SkipDefault;
        var flags = _flags.Where(p => p.Value != _host.GlobalFlag(p.Key)).Select(p => new ScrFlagWrite(p.Key, p.Value)).ToArray();
        int? story = _story is { } s2 && s2 != _storyBefore ? s2 : null;
        var result = new ScrRunResult(outcome, stop, _number, stopLine, null, Array.AsReadOnly(_trace.ToArray()), ScrEffects.None);
        return new(result, new ScrSlot(number, _localFlags, _counters), null, flags, story, _dialog, _calls.Count == 0 ? null : Array.AsReadOnly(_calls.ToArray()),
            _lines.Count == 0 ? null : Array.AsReadOnly(_lines.ToArray()), _sounds.Count == 0 ? null : Array.AsReadOnly(_sounds.ToArray()),
            _teleports.Count == 0 ? null : Array.AsReadOnly(_teleports.ToArray()), _schemes.Count == 0 ? null : Array.AsReadOnly(_schemes.ToArray()),
            _schematics.Count == 0 ? null : Array.AsReadOnly(_schematics.ToArray()),
            _readings.Count == 0 ? null : Array.AsReadOnly(_readings.ToArray()));
    }

    private bool Condition(ScrClause c, int line) => c.Type switch
    {
        ScrCatalogue.Condition.LocalFlag => ((_localFlags >> LocalFlag(Get(c, 0), line)) & 1) != 0,
        ScrCatalogue.Condition.Equal => Get(c, 0) == Get(c, 1),
        ScrCatalogue.Condition.LessOrEqual => Get(c, 0) <= Get(c, 1),
        ScrCatalogue.Condition.GlobalFlag => Flag(GlobalFlag(Get(c, 0), line)),
        // Following: the first object's leader is the second object (both single objects in the set).
        ScrCatalogue.Condition.FollowingPc => _host.Leader(Object(c, 0, line)) == Object(c, 1, line),
        ScrCatalogue.Condition.Jilted => _host.IsJilted(Object(c, 0, line)),
        // SG1: the guard conditions, one object each; the quest state's second value is its third operand.
        ScrCatalogue.Condition.PcQuestState => ScrGuards.Holds(c.Type, Object(c, 0, line), Get(c, 1), Get(c, 2), Queries),
        ScrCatalogue.Condition.HasItemNamed or ScrCatalogue.Condition.IsNamed or ScrCatalogue.Condition.IsWieldingItem or ScrCatalogue.Condition.HasReputation
            => ScrGuards.Holds(c.Type, Object(c, 0, line), Get(c, 1), 0, Queries),
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
            case ScrCatalogue.Action.CallScriptIn:
            {
                // Script, line, triggerer, attachee, seconds; an absent role is stored as absent, as the original stores a
                // null handle (TE9: whether it still exists is decided when the call fires).
                var script = Get(a, 0); var at = Get(a, 1);
                var triggerer = OptionalObject(a, 2); var attachee = OptionalObject(a, 3);
                _calls.Add(new(ScrCallKind.Schedule, script, at, triggerer, attachee, CallDelay(Get(a, 4))));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.RemoveScriptCall:
                _calls.Add(new(ScrCallKind.Remove, Get(a, 0), 0, null, OptionalObject(a, 1), 0));
                return (Step.Next, 0);
            case ScrCatalogue.Action.Dialog:
            {
                // The conversation starts only for a PC triggerer; the action ends the run with skip default either way.
                var node = Get(a, 0);
                if (_roles.Triggerer is { } pc && _host.IsPc(pc)) _dialog = new(pc, _roles.Attachee, _number, line, node);
                return (Step.Dialog, 0);
            }
            case ScrCatalogue.Action.PrintLine:
            {
                // Ledger IS2: the DLG of the running attachment's script (the number this run executes, as for the dialog
                // action: 2188 during a mind-controlled run, E6), operand 0 the line, operand 1 a number shown with it;
                // without an attachee nothing is printed.
                var at = Get(a, 0); var kind = Get(a, 1);
                if (_roles.Attachee is not null) _lines.Add(new(_number, at, kind));
                return (Step.Next, 0);
            }
            // SN28: play sound plays the effect once without a position; play sound on plays it once on the object's tile;
            // play sound scheme is the session's scheme request with the two ids as given.
            case ScrCatalogue.Action.PlaySound: _sounds.Add(new(ScrSoundKind.Effect, Get(a, 0))); return (Step.Next, 0);
            case ScrCatalogue.Action.PlaySoundOn:
            {
                // The original passes an absent object's null handle on to the sound call (undefined); nothing plays here.
                var id = Get(a, 0);
                if (OptionalObject(a, 1) is { } on) _sounds.Add(new(ScrSoundKind.OnObject, id, 0, on));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.PlaySoundScheme: _sounds.Add(new(ScrSoundKind.Scheme, Get(a, 0), Get(a, 1))); return (Step.Next, 0);
            case ScrCatalogue.Action.Teleport:
            {
                // TR1: the object, then the catalogue map, x and y as values; the run goes on. An absent object asks for
                // nothing here (the original would queue a teleport of no object; NOT_VERIFIED what its processing does).
                int[] values = [Get(a, 1), Get(a, 2), Get(a, 3)];
                if (OptionalObject(a, 0) is { } who) _teleports.Add(ScrGuards.Teleport(a.Type, who, values));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.FadeAndTeleport:
            {
                // TR2: seconds, sound and movie, the object, then the map, x and y.
                int[] values = [Get(a, 0), Get(a, 1), Get(a, 2), Get(a, 4), Get(a, 5), Get(a, 6)];
                if (OptionalObject(a, 3) is { } who) _teleports.Add(ScrGuards.Teleport(a.Type, who, values));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.SetAutoLevelScheme:
            {
                // OL30: the object operand is a list and every object in it gets the number (a value); the host writes it for a
                // PC or an NPC and ignores any other object (0x4A6D90). The set's focus kinds name one object each, and an absent
                // one names none (inference: the original's list would hold a null handle, which its setter refuses; not traced).
                // The run goes on.
                var scheme = Get(a, 1);
                foreach (var obj in Objects(a, 0)) _schemes.Add(new(obj, scheme));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.ShowBook:
            case ScrCatalogue.Action.ShowPicture:
            case ScrCatalogue.Action.ShowPlaque:
            {
                // ARC-UI-049 (0x447220 cases 64, 65, 114): the number (a start line, or a picture's art) and the viewer object; an absent
                // viewer shows nothing (the original's open needs the local player). The run goes on.
                var number = Get(a, 0);
                var kind = a.Type switch
                {
                    ScrCatalogue.Action.ShowBook => ScrReadingKind.Book,
                    ScrCatalogue.Action.ShowPicture => ScrReadingKind.Picture,
                    _ => ScrReadingKind.Plaque,
                };
                if (OptionalObject(a, 1) is { } viewer) _readings.Add(new(kind, number, viewer));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.StartSchematicUi:
            {
                // SW8 (TC2, both fixed): the object operand; an absent object opens nothing (the original's null test). The host
                // resolves a PC or an NPC and its leader instead of the original's never-filled callback. The run goes on.
                if (OptionalObject(a, 0) is { } who) _schematics.Add(new(who));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.CallScript:
            {
                // OV1: the script of operand 0 from the line of operand 1, with operand 2 the triggerer and operand 3 the attachee,
                // no extra object, the callee's own header flags and counters and fresh variables; nothing it changes about
                // itself is kept and its return code is ignored, so the caller goes on.
                Call(Get(a, 0), Get(a, 1), OptionalObject(a, 2), OptionalObject(a, 3));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.AssignObject:
            {
                // OV2: the first object operand 1 names (none for an absent one) goes into the store operand 0 names: the
                // triggerer, the attachee, the extra object or an object variable; any other focus kind is not written.
                var value = OptionalObject(a, 1);
                var index = a.Value(0);
                switch ((ScrFocusKind)a.Kind(0))
                {
                    case ScrFocusKind.Triggerer: _roles = _roles with { Triggerer = value }; break;
                    case ScrFocusKind.Attachee: _roles = _roles with { Attachee = value }; break;
                    case ScrFocusKind.ExtraObject: _roles = _roles with { Extra = value }; break;
                    case ScrFocusKind.LocalObject: _objects[index] = value; break;
                }
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.GetObjectType:
            {
                // OV3: the object's type field into the number store operand 1 names; an absent object, or one the host does not
                // know, stops the run (the project rule of the single-object commands).
                var obj = Object(a, 0, line);
                var type = Queries.ObjectType(obj) ?? throw new MissingObjectStop(line);
                Set(a, 1, type, line);
                return (Step.Next, 0);
            }
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
                // X7: the original stores the signed quotient and nothing for a divisor of 0 (ledger
                // X7). int.MinValue / −1 faults the original's idiv; both modes store the wrapped quotient instead.
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

    [OriginalFunction(0x44B030, OriginalFunctionScope.Partial)]
    private void Call(int script, int line, ScrObjectRef? triggerer, ScrObjectRef? attachee)
    {
        if (_depth + 1 > _options.MaxCallDepth) throw new CallRefusedStop(null, script, ScrStop.CallDepth);
        var lookup = _host.Script(script);
        // A missing script runs nothing (the original's runner returns); an unreadable one is not the original's missing file.
        if (ScrDispatcher.Unavailable(lookup) is { } unavailable)
        {
            if (unavailable == ScrStop.MissingScript) return;
            throw new CallRefusedStop(null, script, ScrStop.UnreadableScript);
        }
        var program = lookup.Program!;
        // The runner's gates (E4, E5): a trap script and a monster generator attachee run nothing.
        if (ScrDispatcher.IsTrapScript(script, program)) return;
        if (attachee is { } generator && _host.IsMonsterGenerator(generator)) return;
        var admission = ScrAdmission.AdmitFrom(program, _options.Admission, line);
        if (!admission.IsAdmitted) throw new CallRefusedStop(admission.Refusal, script, ScrStop.Refused);
        // A dialog action inside a called script is not modelled (the original's dialog store would write the callee's flags
        // into its attachee's slot, not read in the executable): the dispatch is refused at that line.
        foreach (var at in admission.Admitted!.Region)
        {
            var row = program.Lines[at];
            var side = row.OnTrue.Type == ScrCatalogue.Action.Dialog ? ScrClauseSide.OnTrue
                : row.Condition.Type != ScrCatalogue.Condition.True && row.OnFalse.Type == ScrCatalogue.Action.Dialog ? ScrClauseSide.OnFalse : (ScrClauseSide?)null;
            if (side is { } dialog) throw new CallRefusedStop(new(at, dialog, ScrRefusalCode.ActionNotAdmitted, ScrCatalogue.Action.Dialog), script, ScrStop.Refused);
        }
        new ScrInterpreter(admission.Admitted!, script, new(script, program.InitialLocalFlags, program.InitialCounters), program.FileFlags,
            new(triggerer, attachee, null), _host, _options, _shared, _depth + 1).Run(line);
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

    // Operand 0 is the store of every writing action in the set but get object type (operand 1); admission refuses a number
    // literal there.
    private void Set(ScrClause c, int value, int line) => Set(c, 0, value, line);

    private void Set(ScrClause c, int slot, int value, int line)
    {
        var index = c.Value(slot);
        switch ((ScrValueKind)c.Kind(slot))
        {
            case ScrValueKind.Counter:
                // X8: the original masks a counter write to its byte (ledger X8).
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

    private ScrObjectRef Object(ScrClause c, int slot, int line) => OptionalObject(c, slot) ?? throw new MissingObjectStop(line);

    // The original turns the seconds into a 32-bit millisecond count, raises 0 to 1 (sub_45A950) and multiplies by 8, so
    // 0 seconds is 8 game ms. TE1: Original keeps the 32-bit wrap; Steamwoven computes without wrapping and reads a
    // negative count as 0. A product that wraps to 0 still waits 1 ms.
    private long CallDelay(int seconds)
    {
        if (_options.Rules.CallDelayWraps)
        {
            var ms = unchecked((uint)(seconds * 1000));
            return Math.Max(1L, unchecked(Math.Max(1u, ms) * 8u));
        }
        return seconds <= 0 ? 8L : seconds * 8000L;
    }

    private ScrObjectRef? OptionalObject(ScrClause c, int slot) => (ScrFocusKind)c.Kind(slot) switch
    {
        ScrFocusKind.Triggerer => _roles.Triggerer,
        ScrFocusKind.Attachee => _roles.Attachee,
        ScrFocusKind.ExtraObject => _roles.Extra,
        ScrFocusKind.Player => _host.Player(),
        // OV4: an object variable never assigned in this run names no object.
        ScrFocusKind.LocalObject => _objects[c.Value(slot)],
        _ => throw new InvalidOperationException("Focus kind outside the interpreter's set."),
    };

    // The objects an object operand names, as the original's list (0x44B4E0): one for a single-object focus kind, none for an absent
    // one. Focus groups are not admitted yet.
    private IReadOnlyList<ScrObjectRef> Objects(ScrClause c, int slot) => OptionalObject(c, slot) is { } obj ? [obj] : [];

    private IScrObjectQueries Queries => _host as IScrObjectQueries
        ?? throw new InvalidOperationException("SCR_OBJECT_QUERIES_REQUIRED: the admission set holds a guard condition the host cannot answer.");

    private bool Flag(int index) => _flags.TryGetValue(index, out var value) ? value : _host.GlobalFlag(index);

    private int Story() { _storyBefore ??= _host.StoryState(); return _story ??= _storyBefore.Value; }

    // The original shifts by the number with x86 shl (0x4473C2, 0x4473F5), which uses its low five bits.
    private static int LocalFlag(int index, int line) => index & (ScrCatalogue.LocalFlags - 1);
    private static int GlobalFlag(int index, int line) => index is >= 0 and < ScrCatalogue.GlobalFlags ? index : throw new IndexStop(line);
}
