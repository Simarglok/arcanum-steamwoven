using Arcanum.Data.Scripts;

namespace Arcanum.Scripting.Scr;

/// <summary>
/// One run of an admitted program (ARC-RES-042 E7–E11): fresh local variables, the attachment's local flags and
/// counters in a private copy, a line loop with the original's budget, and every global change kept in an overlay that
/// the run itself reads back and the result hands to the caller. It supports exactly the commands of
/// <see cref="ScrAdmissionSet.S31"/>.
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
        public readonly List<ScrSkillUse> SkillUses = new();
        public readonly List<ScrStateRequest> States = new();
        public readonly List<ScrEffectRequest> EffectChanges = new();
        public readonly List<ScrEyeCandy> EyeCandies = new();
        // ARC-WORLD-081: the create-object requests (PG1).
        public readonly List<ScrCreateRequest> Creations = new();
        // ARC-WORLD-114: the toggle-state and kill requests (SU6, PM1).
        public readonly List<ScrObjectStateRequest> ObjectStates = new();
        // ARC-SCRIPT-049: the objects the dispatch's toggle states left flipped (odd count) and the ones it killed, which its later dead and
        // switched-off tests read (the original flips the flag and kills at once).
        public readonly HashSet<ScrObjectRef> Toggled = new(), Killed = new();
        // ARC-SCRIPT-050: the float lines (FL4).
        public readonly List<ScrFloatLine> Floats = new();
        // ARC-DEV-205: the town maps unfog town map asked to reveal (UF1).
        public readonly List<int> TownMaps = new();
        // ARC-SCRIPT-042: the slots call-ex runs changed, in order, and the latest of each, which a later call-ex run of the same slot reads.
        public readonly List<ScrWriteback> SlotWrites = new();
        public readonly Dictionary<(ScrObjectRef, int), ScrSlot> Slots = new();
        public readonly SortedDictionary<int, bool> Flags = new();
        // ARC-DEV-100 (GV1–GV4): the run's writes to the global variables and to the PCs' variables and flags (the flags already spread over
        // their 32-flag word), read back by the run itself and handed to the caller with the global flags. ARC-DEV-265: a PC cell is keyed by
        // its PC, so two PCs keep apart arrays (fields 272 and 273 of each PC object, SA14).
        public readonly SortedDictionary<int, int> GlobalVariables = new();
        public readonly SortedDictionary<(ScrObjectRef Pc, int Index), int> PcVariables = new(ScrPcOrder.Instance);
        public readonly SortedDictionary<(ScrObjectRef Pc, int Index), bool> PcFlags = new(ScrPcOrder.Instance);
        public int? Story, StoryBefore;
        // Lines executed by every run of the dispatch (a project bound across nested calls).
        public int Executed;
    }

    private readonly Shared _shared;
    private readonly int _depth;
    // ARC-SCRIPT-046: the attachment point of the run (its event's; a called script runs at its caller's, 0x44B030), which the dialog
    // action of a called script stores its flags and counters at.
    private readonly int _point;
    private ScrDialogStart? _dialog { get => _shared.Dialog; set => _shared.Dialog = value; }
    private List<ScrCallRequest> _calls => _shared.Calls;
    private List<ScrPrintLine> _lines => _shared.Lines;
    private List<ScrSound> _sounds => _shared.Sounds;
    private List<ScrTeleport> _teleports => _shared.Teleports;
    private List<ScrSchemeRequest> _schemes => _shared.Schemes;
    private List<ScrSchematicRequest> _schematics => _shared.Schematics;
    private List<ScrReadingRequest> _readings => _shared.Readings;
    private List<ScrSkillUse> _skillUses => _shared.SkillUses;
    private List<ScrStateRequest> _states => _shared.States;
    private SortedDictionary<int, bool> _flags => _shared.Flags;
    private int? _story { get => _shared.Story; set => _shared.Story = value; }
    private int? _storyBefore { get => _shared.StoryBefore; set => _shared.StoryBefore = value; }
    private readonly ScrRunOptions _options;
    // The line being executed, which an operand read that stops the run (an absent object of a PC operand) names.
    private int _line;
    private readonly int[] _locals = new int[ScrCatalogue.LocalSlots];
    private readonly List<ScrTraceStep> _trace = new();
    private uint _localFlags, _counters;
    // The original's "no change" sentinel for remove/change this script.
    private int _changedNumber = -1;
    // PG7: the run's loop: the list loop for filled (null while no loop runs), the index of the current object, the line after the loop
    // for, and the current looped object (focus kind 10), which a finished loop leaves as it was.
    private IReadOnlyList<ScrObjectRef>? _loop;
    private int _loopIndex, _loopReturn;
    private ScrObjectRef? _looped;

    internal ScrInterpreter(ScrAdmittedProgram admitted, int number, ScrSlot slot, uint ownFileFlags, ScrDispatcher.ScrRoles roles, IScrHost host, ScrRunOptions options,
        int point)
        : this(admitted, number, slot, ownFileFlags, roles, host, options, new Shared(), 0, point) { }

    private ScrInterpreter(ScrAdmittedProgram admitted, int number, ScrSlot slot, uint ownFileFlags, ScrDispatcher.ScrRoles roles, IScrHost host, ScrRunOptions options,
        Shared shared, int depth, int point)
    {
        _admitted = admitted; _number = number; _slot = slot; _ownFileFlags = ownFileFlags; _roles = roles; _host = host; _options = options;
        _localFlags = slot.LocalFlags; _counters = slot.Counters; _shared = shared; _depth = depth; _point = point;
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
                _line = line;
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
        var stores = StoreWrites();
        var result = new ScrRunResult(outcome, stop, _number, stopLine, null, Array.AsReadOnly(_trace.ToArray()), ScrEffects.None);
        return new(result, new ScrSlot(number, _localFlags, _counters), null, flags, story, _dialog, _calls.Count == 0 ? null : Array.AsReadOnly(_calls.ToArray()),
            _lines.Count == 0 ? null : Array.AsReadOnly(_lines.ToArray()), _sounds.Count == 0 ? null : Array.AsReadOnly(_sounds.ToArray()),
            _teleports.Count == 0 ? null : Array.AsReadOnly(_teleports.ToArray()), _schemes.Count == 0 ? null : Array.AsReadOnly(_schemes.ToArray()),
            _schematics.Count == 0 ? null : Array.AsReadOnly(_schematics.ToArray()),
            _readings.Count == 0 ? null : Array.AsReadOnly(_readings.ToArray()),
            _skillUses.Count == 0 ? null : Array.AsReadOnly(_skillUses.ToArray()),
            _states.Count == 0 ? null : Array.AsReadOnly(_states.ToArray()),
            _shared.EffectChanges.Count == 0 ? null : Array.AsReadOnly(_shared.EffectChanges.ToArray()),
            _shared.EyeCandies.Count == 0 ? null : Array.AsReadOnly(_shared.EyeCandies.ToArray()),
            _shared.SlotWrites.Count == 0 ? null : Array.AsReadOnly(_shared.SlotWrites.ToArray()),
            _shared.Creations.Count == 0 ? null : Array.AsReadOnly(_shared.Creations.ToArray()),
            _shared.ObjectStates.Count == 0 ? null : Array.AsReadOnly(_shared.ObjectStates.ToArray()),
            _shared.Floats.Count == 0 ? null : Array.AsReadOnly(_shared.Floats.ToArray()), stores,
            _shared.TownMaps.Count == 0 ? null : Array.AsReadOnly(_shared.TownMaps.ToArray()));
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
        // WE5: the identified count over the operand's objects; none identified is false and, for one object, all is true. An absent
        // object names none, so the condition fails (the set's focus kinds name at most one object).
        ScrCatalogue.Condition.Identified => Objects(c, 0).Count(obj => Items.IsIdentified(obj) ?? throw new MissingObjectStop(line)) > 0,
        // WE6: the global the rewield (0x465530) sets around its runs.
        ScrCatalogue.Condition.ItemsBeingRewielded => Items.ItemsBeingRewielded(),
        // HW1, HW2: the dead test (0x45D8D0) and the "off" object flag over the operand's objects, true by the list rule of SG6; an object the
        // host does not know stops the run.
        ScrCatalogue.Condition.IsDead => ListHolds(c, obj => _shared.Killed.Contains(obj) || (States.IsDead(obj) ?? throw new MissingObjectStop(line))),
        ScrCatalogue.Condition.IsSwitchedOff => ListHolds(c, obj => _shared.Toggled.Contains(obj) != (States.IsSwitchedOff(obj) ?? throw new MissingObjectStop(line))),
        // SC1: the four body bits (0x3C) of the spell flags (case 0x4458D5, field 0x14 through 0x406CA0), over the operand's objects by the list rule of SG6.
        ScrCatalogue.Condition.HasBodySpell => ListHolds(c, obj => ((Spells.SpellFlags(obj) ?? throw new MissingObjectStop(line)) & BodySpellFlags) != 0),
        // SC2: influenced by (0x459380) the spell of operand 1, SC3: knows (0x4B1950) the spell of operand 1, SC4: its mastered college (0x4B1CB0) equals operand 1 (−1 matches an object with no mastery, and any object that is not a PC or NPC).
        ScrCatalogue.Condition.InfluencedBySpell => SpellHolds(c, line, (obj, spell) => Spells.IsInfluencedBy(obj, spell)),
        ScrCatalogue.Condition.KnowsSpell => SpellHolds(c, line, (obj, spell) => Spells.KnowsSpell(obj, spell)),
        ScrCatalogue.Condition.MasteredSpellCollege => SpellHolds(c, line, (obj, college) => Spells.MasteredCollege(obj) is { } mastered ? mastered == college : null),
        _ => throw new InvalidOperationException("Condition outside the interpreter's set."),
    };

    [OriginalFunction(0x45D0C0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x44B9B0, OriginalFunctionScope.Partial)]
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
                // The conversation starts only for a PC triggerer; the action ends the run with skip default either way (0x447282).
                var node = Get(a, 0);
                if (_roles.Triggerer is { } pc && _host.IsPc(pc))
                {
                    // ARC-SCRIPT-046: a called script's dialog (its caller goes on) is one more conversation of the dispatch: a project
                    // limit refuses a second one.
                    if (_dialog is not null) throw new CallRefusedStop(new(line, ScrClauseSide.OnTrue, ScrRefusalCode.ActionNotAdmitted, ScrCatalogue.Action.Dialog), _number, ScrStop.Refused);
                    if (_depth > 0) StoreDialogSlot();
                    _dialog = new(pc, _roles.Attachee, _number, line, node) { Point = _point };
                }
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
            case ScrCatalogue.Action.FloatLine: FloatLine(a); return (Step.Next, 0);
            case ScrCatalogue.Action.GetArea:
            {
                // GA1: the first object of operand 0, its area into operand 1. The original reads an empty list's uninitialised first slot; an
                // absent object stops the run (the project rule of the single-object commands). A host that cannot answer (GA3 not modelled)
                // ends the whole dispatch with nothing applied, so the caller can report it as not ready.
                var obj = Object(a, 0, line);
                Set(a, 1, Areas.AreaOf(obj) ?? throw new CallRefusedStop(null, _number, ScrStop.NotModelled), line);
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
            case ScrCatalogue.Action.SetPcQuestState:
            {
                // SS2: the object operand 0 names, then the quest and the state; the run's attachee is the quest's giver.
                var quest = Get(a, 1); var state = Get(a, 2);
                foreach (var obj in Objects(a, 0)) _states.Add(new(ScrStateKind.QuestState, obj, quest, state, _roles.Attachee));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.MarkMapLocation:
            case ScrCatalogue.Action.SetRumor:
            {
                // SS3, SS4: the number, then the first object operand 1 names; an absent one asks for nothing.
                var number = Get(a, 0);
                if (OptionalObject(a, 1) is { } obj)
                    _states.Add(new(a.Type == ScrCatalogue.Action.MarkMapLocation ? ScrStateKind.KnownArea : ScrStateKind.Rumor, obj, number));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.UnfogTownMap:
            {
                // UF1 (ARC-DEV-205): operand 0 is the town map; the original reveals every cell of it (0x4BEAB0 → 0x4BED30) and does nothing for
                // town map 0. A negative number is asked for nothing either (the project's reading: no town map has one).
                var townMap = Get(a, 0);
                if (townMap > 0) _shared.TownMaps.Add(townMap);
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.AddReputation:
            case ScrCatalogue.Action.RemoveReputation:
            {
                // SS5: every object operand 0 names, then the reputation.
                var reputation = Get(a, 1);
                foreach (var obj in Objects(a, 0))
                    _states.Add(new(a.Type == ScrCatalogue.Action.AddReputation ? ScrStateKind.AddReputation : ScrStateKind.RemoveReputation, obj, reputation));
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
            case ScrCatalogue.Action.GetSkill:
            {
                // SX1, SX2: the skill number, the object, then the store; the effective level (basic below 12, tech index skill − 12
                // from 12); a number outside 0..15 stores 0, as the original's level functions return. The original reads an absent
                // object's null handle through the object pool (undefined); the run stops here as at every other object read.
                var skill = Get(a, 0);
                var who = Object(a, 1, line);
                Set(a, 2, skill is >= 0 and < SkillNumbers ? Skills.SkillLevel(who, skill) : 0, line);
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.GetStat:
            {
                // GS1 (0x447BEE): the stat number, the object, then the store; the effective stat from the stat getter. GS2: intelligence (4)
                // above 4 reads 1 for a critter the dumb test (0x45FC00) accepts, which above 4 means a dumb background. The original reads an
                // absent object's null handle through the object pool (undefined); the run stops here as at every other object read. A host
                // that cannot answer (GS3, not modelled) ends the whole dispatch with nothing applied, so the caller reports it as not ready.
                var stat = Get(a, 0);
                var who = Object(a, 1, line);
                var value = Stats.EffectiveStat(who, stat) ?? throw new CallRefusedStop(null, _number, ScrStop.NotModelled);
                if (stat == DumbStat && value > DumbStat && Stats.IsDumbByBackground(who)) value = 1;
                Set(a, 2, value, line);
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.GetReaction:
            {
                // GR1 (0x447A5A): the first object of operand 0 and of operand 1 (0x44B960), the standard effective reaction of the first toward
                // the second (0x4C0CC0) into the number operand 2. The original reads an absent object's null handle through the object pool
                // (undefined); the run stops here as at every other object read. A host that cannot answer ends the whole dispatch with nothing
                // applied, so the caller reports it as not ready.
                var npc = Object(a, 0, line);
                var pc = Object(a, 1, line);
                Set(a, 2, Reactions.ReactionOf(npc, pc) ?? throw new CallRefusedStop(null, _number, ScrStop.NotModelled), line);
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.UseItemWithSkill:
            {
                // SX3: source, item, target, skill and modifier go unchanged to the source's use-item-with-skill goal, whose answer the
                // run ignores; an absent target queues nothing (the starter's null test), nor does an absent source (its gate refuses).
                var source = OptionalObject(a, 0); var item = OptionalObject(a, 1); var target = OptionalObject(a, 2);
                var skill = Get(a, 3); var modifier = Get(a, 4);
                if (source is { } user && target is { } on) _skillUses.Add(new(user, item, on, skill, modifier));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.AddEffect:
            case ScrCatalogue.Action.RemoveEffect:
            {
                // WE1: every object operand 0 names, then the effect number (and the cause for an add), as read.
                var effect = Get(a, 1);
                var add = a.Type == ScrCatalogue.Action.AddEffect;
                var cause = add ? Get(a, 2) : 0;
                foreach (var obj in Objects(a, 0)) _shared.EffectChanges.Add(new(add ? ScrEffectChange.Add : ScrEffectChange.Remove, obj, effect, cause));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.ToggleState:
            case ScrCatalogue.Action.Kill:
            {
                // SU6, PM1: each object of operand 0 in turn (0x44B4E0's list); toggle state flips its "off" flag, kill is a death with no
                // killer. An empty list does nothing.
                var change = a.Type == ScrCatalogue.Action.Kill ? ScrObjectStateChange.Kill : ScrObjectStateChange.ToggleOff;
                foreach (var obj in Objects(a, 0))
                {
                    _shared.ObjectStates.Add(new(change, obj));
                    if (change == ScrObjectStateChange.Kill) _shared.Killed.Add(obj);
                    else if (!_shared.Toggled.Remove(obj)) _shared.Toggled.Add(obj);
                }
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.MagickTechAdjustment:
            {
                // WE2: the value, the item and its owner, then (ratio × value + 50) / 100 into operand 3, with the original's 32-bit wrap of
                // the product and the sum and its division, which truncates toward zero. An absent object or one the host does not know
                // stops the run (the original reads a null handle's fields).
                var value = Get(a, 0);
                var item = Object(a, 1, line); var owner = Object(a, 2, line);
                var ratio = Items.PowerRatio(item, owner) ?? throw new MissingObjectStop(line);
                Set(a, 3, unchecked(ratio * value + 50) / 100, line);
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.PlayScriptEyeCandy:
            {
                // WE4: the table entry, then the object; an absent object shows nothing (the original passes a null handle on).
                var number = Get(a, 0);
                if (OptionalObject(a, 1) is { } on) _shared.EyeCandies.Add(new(number, on));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.DistanceBetween:
            {
                // HW3: the first objects of operands 0 and 1 (0x44B960), their tile distance (0x441AE0 → 0x4B96F0: the larger of the two axis
                // differences, as WorldTravelGeometry.Distance, which this assembly cannot reference) into operand 2, cut to 32 bits as the
                // original stores it. An absent object, or one without a place, stops the run (the original reads a null handle's location).
                var from = World.Location(Object(a, 0, line)) ?? throw new MissingObjectStop(line);
                var to = World.Location(Object(a, 1, line)) ?? throw new MissingObjectStop(line);
                Set(a, 2, unchecked((int)Math.Max(Math.Abs(from.X - to.X), Math.Abs(from.Y - to.Y))), line);
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.SetFaction:
            {
                // HW4: the first object of operand 0 and the faction; an absent object changes nothing (0x45D0C0 returns at a null handle).
                var faction = Get(a, 1);
                if (OptionalObject(a, 0) is { } obj) _shared.ObjectStates.Add(new(ScrObjectStateChange.SetFaction, obj, faction));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.Destroy:
            case ScrCatalogue.Action.StopAttacking:
            {
                // TD1, TD3: the first object of operand 0 (0x44B960) goes to the object destroy (0x43CCA0) or to stop attacking (0x4AA4A0), and the
                // run goes on. An absent object changes nothing (a project choice: both read the null handle's fields).
                var change = a.Type == ScrCatalogue.Action.Destroy ? ScrObjectStateChange.Destroy : ScrObjectStateChange.StopAttacking;
                if (OptionalObject(a, 0) is { } obj) _shared.ObjectStates.Add(new(change, obj));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.WalkTo:
            case ScrCatalogue.Action.RunTo:
            {
                // GT154, GT158: the first object of operand 0 and the tile of operands 1 and 2 go to the host, and the run goes on (the answer of the starter is
                // ignored). An absent object changes nothing. The original ORs X's sign extension into Y (a negative X gives Y = -1, GT158): the request keeps
                // the values as read and the host starts nothing for a negative tile; a tile past the map is the planner's failure.
                var x = Get(a, 1);
                var y = Get(a, 2);
                var change = a.Type == ScrCatalogue.Action.WalkTo ? ScrObjectStateChange.WalkTo : ScrObjectStateChange.RunTo;
                if (OptionalObject(a, 0) is { } obj) _shared.ObjectStates.Add(new(change, obj, x, y));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.GiveSpellMastery:
            {
                // SC5: the first object of operand 0 (0x44B960) and the college of operand 1 go to the mastery setter (0x4EF080, then 0x4B1CF0); the run goes on. An absent object changes nothing (a project choice: the original reads
                // the null handle's type field). The college is not range-checked.
                var college = Get(a, 1);
                if (OptionalObject(a, 0) is { } obj) _shared.ObjectStates.Add(new(ScrObjectStateChange.GiveSpellMastery, obj, college));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.Damage:
            case ScrCatalogue.Action.DamageUnresistable:
            {
                // GT194: every object of operand 0 in list order, the amount of operand 1 and the damage type of operand 2; the run goes on (GT154). The type is not range-checked in the original; the host does nothing for one outside 0..4.
                var amount = Get(a, 1);
                var type = Get(a, 2);
                var change = a.Type == ScrCatalogue.Action.Damage ? ScrObjectStateChange.Damage : ScrObjectStateChange.DamageUnresistable;
                foreach (var obj in Objects(a, 0)) _shared.ObjectStates.Add(new(change, obj, amount, type));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.HealHitPoints:
            {
                // GT196: every object of operand 0 in list order, the amount of operand 1; the run goes on (GT154).
                var amount = Get(a, 1);
                foreach (var obj in Objects(a, 0)) _shared.ObjectStates.Add(new(ScrObjectStateChange.HealHitPoints, obj, amount));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.CastSpell:
            case ScrCatalogue.Action.CastFreeSpell:
            case ScrCatalogue.Action.CastUnresistableSpellBy:
            case ScrCatalogue.Action.CastFreeUnresistableSpell:
            {
                // GT183: operand 0 the caster (the first object), operand 1 the spell, operand 2 the target (the first object); 118 adds flag 0x2, 121 flag 0x4, 122 both. The run goes on (GT154). A missing target casts nothing
                // (a project choice: the original's invocation takes a null handle); a missing caster is a cast with none (GT184).
                var spell = Get(a, 1);
                var flags = a.Type switch
                {
                    ScrCatalogue.Action.CastFreeSpell => ScrCastFlags.Free,
                    ScrCatalogue.Action.CastUnresistableSpellBy => ScrCastFlags.Unresistable,
                    ScrCatalogue.Action.CastFreeUnresistableSpell => ScrCastFlags.Free | ScrCastFlags.Unresistable,
                    _ => ScrCastFlags.None,
                };
                if (OptionalObject(a, 2) is { } target) _shared.ObjectStates.Add(new(ScrObjectStateChange.CastSpell, target, spell, (int)flags, OptionalObject(a, 0)));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.CastSpellOn:
            case ScrCatalogue.Action.CastUnresistableSpellOn:
            {
                // GT183/GT184: operand 0 the spell, operand 1 the target (the first object); no caster, so no casting goal; 136 adds flag 0x4.
                var spell = Get(a, 0);
                var flags = a.Type == ScrCatalogue.Action.CastUnresistableSpellOn ? ScrCastFlags.Unresistable : ScrCastFlags.None;
                if (OptionalObject(a, 1) is { } target) _shared.ObjectStates.Add(new(ScrObjectStateChange.CastSpell, target, spell, (int)flags));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.BecomeProne:
            {
                // GT187: every object of operand 0 in list order; the run goes on (GT154).
                foreach (var obj in Objects(a, 0)) _shared.ObjectStates.Add(new(ScrObjectStateChange.BecomeProne, obj));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.StealCoins:
            {
                // GT164: the first object of operand 0 steals from the first object of operand 1; nothing without both (the original reads null handles' fields; a project choice). The run goes on (GT154).
                if (OptionalObject(a, 0) is { } thief && OptionalObject(a, 1) is { } victim) _shared.ObjectStates.Add(new(ScrObjectStateChange.StealCoins, thief, Other: victim));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.PerformAnimation:
            {
                // GT155: the first object of operand 0 and the animation number of operand 1; the answer of the starter is ignored (GT154).
                var animation = Get(a, 1);
                if (OptionalObject(a, 0) is { } obj) _shared.ObjectStates.Add(new(ScrObjectStateChange.PerformAnimation, obj, animation));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.HealFatigue:
            {
                // GT162: every object of operand 0 in list order, the amount of operand 1; the run goes on (GT154).
                var amount = Get(a, 1);
                foreach (var obj in Objects(a, 0)) _shared.ObjectStates.Add(new(ScrObjectStateChange.HealFatigue, obj, amount));
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.CallScriptEx:
            {
                // WE3: the object whose attachment runs (operand 0), the point and the line (values), and the triggerer (operand 3).
                CallEx(OptionalObject(a, 0), Get(a, 1), Get(a, 2), OptionalObject(a, 3), line);
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.Random:
            {
                // PG5: the bounds in either order, both included, into operand 2 (the session's generator stands in for the original's 15-bit
                // one, so a span above 32,768 is covered in both modes).
                var low = Get(a, 0); var high = Get(a, 1);
                Set(a, 2, World.Random(Math.Min(low, high), Math.Max(low, high)), line);
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.LoopFor:
            {
                // PG7: a loop for inside a running loop does nothing (the original logs an error). Otherwise the list is filled once, the line
                // after the loop for remembered and the first object made the current looped object; an empty list goes on after its loop end.
                if (_loop is not null) return (Step.Next, 0);
                var list = Objects(a, 0);
                _loopReturn = line + 1;
                if (list.Count == 0) return (Step.Jump, ScrAdmission.AfterLoopEnd(_admitted.Program, line));
                _loop = list; _loopIndex = 0; _looped = list[0];
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.LoopEnd:
            {
                // PG7: without a running loop nothing happens (the original logs an error); otherwise the next object and back to the line after
                // the loop for, or, after the last, the loop is cleared and the run goes on (the current looped object stays the last one).
                if (_loop is not { } running) return (Step.Next, 0);
                if (++_loopIndex < running.Count) { _looped = running[_loopIndex]; return (Step.Jump, _loopReturn); }
                _loop = null;
                return (Step.Next, 0);
            }
            case ScrCatalogue.Action.CreateObject:
            {
                // PG1: the prototype number (as named; PG8's +20 is the host's) next to the first object operand 1 names; an absent object creates
                // nothing (the original would place it at a null handle's location). The tile and the object are the host's.
                var prototype = Get(a, 0);
                if (OptionalObject(a, 1) is { } near) _shared.Creations.Add(new(prototype, near));
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

    // ARC-SCRIPT-050 (FL2–FL4): the PC is the first PC in the game-window rectangle around the triggerer (0x440FC0 with the PC mask 0x8000);
    // without one nothing is read or floated. Without a triggerer nothing happens either (a project choice: the original passes the null handle
    // to the list, whose location read was not traced). Then operand 0 selects the DLG line of the running script (the number this run
    // executes, as print line) and each object of operand 1 in list order gets it floated, with that PC as the dialog's PC. The attachee is
    // not needed (unlike print line). The callback test and the DLG load are the host's.
    [OriginalFunction(0x44B1F0, OriginalFunctionScope.Partial)]
    private void FloatLine(ScrClause a)
    {
        if (_roles.Triggerer is not { } around || World.CrittersInVicinity(around).Where(_host.IsPc).Select(o => (ScrObjectRef?)o).FirstOrDefault() is not { } pc) return;
        var at = Get(a, 0);
        foreach (var speaker in Objects(a, 1)) _shared.Floats.Add(new(_number, at, speaker, pc));
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
        // ARC-SCRIPT-046: the callee runs at the caller's point (0x44B030 copies it), where its dialog action stores its flags.
        new ScrInterpreter(admission.Admitted!, script, new(script, program.InitialLocalFlags, program.InitialCounters), program.FileFlags,
            new(triggerer, attachee, null), _host, _options, _shared, _depth + 1, _point).Run(line);
    }

    /// <summary>
    /// ARC-SCRIPT-046: the dialog action of a called script (0x447282, ledger X11) before its conversation starts: the attachee's slot
    /// of the run's point takes the run's local flags and counters when they differ, its script number kept (no attachee, no store).
    /// The slot is read as call-ex reads one (a change earlier in the dispatch first) and kept in <see cref="ScrEffects.SlotWrites"/>.
    /// </summary>
    private void StoreDialogSlot()
    {
        if (_roles.Attachee is not { } attachee) return;
        var slot = _shared.Slots.TryGetValue((attachee, _point), out var written) ? written : _host.Slot(attachee, _point) ?? new ScrSlot(0, 0, 0);
        if (slot.LocalFlags == _localFlags && slot.Counters == _counters) return;
        var stored = slot with { LocalFlags = _localFlags, Counters = _counters };
        _shared.Slots[(attachee, _point)] = stored;
        _shared.SlotWrites.Add(new(ScrSource.Object, attachee, _point, stored));
    }

    /// <summary>
    /// ARC-SCRIPT-042, WE3: call-ex runs the object's attachment of the point as an event of its own (0x441980): the object's slot, the
    /// triggerer, no extra object, from the line; its result is ignored and the caller goes on. The run shares the dispatch's effects,
    /// global overlay and bounds; its changed slot is kept for <see cref="ScrEffects.SlotWrites"/> and read by a later call-ex of the same
    /// slot. No slot, script 0, a missing or trap script and a monster generator run nothing, as for an event. ARC-SCRIPT-047: the dialog
    /// point (a heartbeat's opening talk, ledger TG5) meets the runner's dialog gates as an event does (E6, 0x4449B0): a dead NPC without
    /// the death-speech file flag or a surrendered one without the surrender-speech flag runs nothing, a mind-controlled one runs 2188 in
    /// place of its own (its own number kept); a dialog action in the callee stores the run's flags and counters in the very slot the
    /// call-ex then writes back (X11), so the writeback is the slot it leaves; a callee that reaches call script is refused (the called
    /// script would skip the gates). Project limits: an absent object stops the caller's run; the
    /// use point (the trap spot is not modelled for a nested run) and a trap object whose script becomes 0 refuse the dispatch, and a point
    /// outside 0..35 or the dialog-override slot ends it (<see cref="ScrStop.CallExPoint"/>).
    /// </summary>
    [OriginalFunction(0x44B170, OriginalFunctionScope.Partial)]
    private void CallEx(ScrObjectRef? attachee, int point, int line, ScrObjectRef? triggerer, int at)
    {
        if (attachee is not { } obj) throw new MissingObjectStop(at);
        if (point is < 0 or >= ScrPoint.Count || point == ScrPoint.DialogOverride) throw new CallRefusedStop(null, _number, ScrStop.CallExPoint);
        if (point == ScrPoint.Use)
            throw new CallRefusedStop(new(at, ScrClauseSide.OnTrue, ScrRefusalCode.ActionNotAdmitted, ScrCatalogue.Action.CallScriptEx), _number, ScrStop.Refused);
        if (_depth + 1 > _options.MaxCallDepth) throw new CallRefusedStop(null, _number, ScrStop.CallDepth);
        var slot = _shared.Slots.TryGetValue((obj, point), out var written) ? written : _host.Slot(obj, point);
        if (slot is not { Script: not 0 } own) return;
        var lookup = _host.Script(own.Script);
        if (ScrDispatcher.Unavailable(lookup) is { } unavailable)
        {
            if (unavailable == ScrStop.MissingScript) return;
            throw new CallRefusedStop(null, own.Script, ScrStop.UnreadableScript);
        }
        var program = lookup.Program!;
        if (ScrDispatcher.IsTrapScript(own.Script, program) || _host.IsMonsterGenerator(obj)) return;
        var number = own.Script;
        // The auto-removal flag is read from the attachment's own script, as for an event (RunObject).
        var ownFlags = program.FileFlags;
        if (point == ScrPoint.Dialog)
        {
            // E6 (0x4449B0, ledger TG5): the gates read the NPC's own file flags before a mind-controlled swap.
            var state = _host.DialogState(obj);
            if ((state & ScrDialogState.Dead) != 0 && (program.FileFlags & ScrCatalogue.FileFlag.DeathSpeech) == 0) return;
            if ((state & ScrDialogState.Surrendered) != 0 && (program.FileFlags & ScrCatalogue.FileFlag.SurrenderSpeech) == 0) return;
            if ((state & ScrDialogState.MindControlled) != 0)
            {
                number = ScrDispatcher.MindControlScript;
                var swapped = _host.Script(number);
                if (ScrDispatcher.Unavailable(swapped) is { } absent)
                {
                    if (absent == ScrStop.MissingScript) return;
                    throw new CallRefusedStop(null, number, ScrStop.UnreadableScript);
                }
                program = swapped.Program!;
            }
        }
        var admission = ScrAdmission.AdmitFrom(program, _options.Admission, line);
        if (!admission.IsAdmitted) throw new CallRefusedStop(admission.Refusal, number, ScrStop.Refused);
        if (point == ScrPoint.Dialog) RefuseCallScriptIn(admission.Admitted!, number);
        var writes = _shared.SlotWrites.Count;
        var ran = new ScrInterpreter(admission.Admitted!, number, own, ownFlags, new(triggerer, obj, null), _host, _options, _shared, _depth + 1, point).Run(line);
        // A mind-controlled run keeps the NPC's own number whatever 2188 did to its own.
        var run = number == own.Script ? ran : ran with { Slot = ran.Slot with { Script = own.Script } };
        if (run.Slot == own) return;
        if (run.Slot.Script == 0 && _host.IsTrapObject(obj))
            throw new CallRefusedStop(new(at, ScrClauseSide.OnTrue, ScrRefusalCode.ActionNotAdmitted, ScrCatalogue.Action.CallScriptEx), _number, ScrStop.Refused);
        _shared.Slots[(obj, point)] = run.Slot;
        // The callee's own dialog store of this slot (StoreDialogSlot) is superseded by the writeback; earlier call-exes keep theirs.
        for (var i = _shared.SlotWrites.Count - 1; i >= writes; i--)
            if (_shared.SlotWrites[i] is { Source: ScrSource.Object, Point: var p } w && p == point && w.Attachee == obj) _shared.SlotWrites.RemoveAt(i);
        _shared.SlotWrites.Add(new(ScrSource.Object, obj, point, run.Slot));
    }

    // As RunObject refuses a dialog-point event whose set holds call script, a call-ex of the dialog point refuses a callee that
    // reaches one: the called script would run at the dialog point without the runner's dialog gates (E6), which call script skips.
    private static void RefuseCallScriptIn(ScrAdmittedProgram admitted, int script)
    {
        foreach (var at in admitted.Region)
        {
            var row = admitted.Program.Lines[at];
            var side = row.OnTrue.Type == ScrCatalogue.Action.CallScript ? ScrClauseSide.OnTrue
                : row.Condition.Type != ScrCatalogue.Condition.True && row.OnFalse.Type == ScrCatalogue.Action.CallScript ? ScrClauseSide.OnFalse : (ScrClauseSide?)null;
            if (side is { } call) throw new CallRefusedStop(new(at, call, ScrRefusalCode.ActionNotAdmitted, ScrCatalogue.Action.CallScript), script, ScrStop.Refused);
        }
    }

    [OriginalFunction(0x44BA70, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x444E30, OriginalFunctionScope.Full)]
    [OriginalFunction(0x444DB0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x444CF0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x444C80, OriginalFunctionScope.Partial)]
    private int Get(ScrClause c, int slot)
    {
        var index = c.Value(slot);
        return (ScrValueKind)c.Kind(slot) switch
        {
            // PG6: the shift by eight times the index keeps five bits, so any index reads counter (index mod 4).
            ScrValueKind.Counter => (int)((_counters >> CounterShift(index)) & 0xFF),
            ScrValueKind.LocalVariable => _locals[index],
            ScrValueKind.Number => index,
            ScrValueKind.GlobalFlag => Flag(index) ? 1 : 0,
            ScrValueKind.GlobalVariable => GlobalVariable(index),
            // GV3, GV4: the operand packs the object in its high 16 bits and the index in its low 16; any object but a PC reads 0 (0x444DB0, 0x444E30).
            ScrValueKind.PcVariable => PcStore(index) is { } variable ? PcVariable(variable) : 0,
            ScrValueKind.PcFlag => PcStore(index) is { } flag ? PcFlag(flag) ? 1 : 0 : 0,
            _ => throw new InvalidOperationException("Value kind outside the interpreter's set."),
        };
    }

    // Operand 0 is the store of every writing action in the set but get object type (operand 1), get skill (operand 2) and the
    // magick-tech adjustment (operand 3); admission refuses a number literal there.
    private void Set(ScrClause c, int value, int line) => Set(c, 0, value, line);

    [OriginalFunction(0x44BB50, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x444E90, OriginalFunctionScope.Full)]
    [OriginalFunction(0x444DF0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x444D20, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x444C90, OriginalFunctionScope.Partial)]
    private void Set(ScrClause c, int slot, int value, int line)
    {
        var index = c.Value(slot);
        switch ((ScrValueKind)c.Kind(slot))
        {
            case ScrValueKind.Counter:
                // X8: the original masks a counter write to its byte (ledger X8).
                _counters = (_counters & ~(0xFFu << CounterShift(index))) | (((uint)value & 0xFF) << CounterShift(index));
                break;
            case ScrValueKind.LocalVariable: _locals[index] = value; break;
            case ScrValueKind.GlobalFlag: WriteFlag((i, v) => _flags[i] = v, GlobalFlag(index, line), value); break;
            // GV1: a global variable keeps the whole 32-bit value, unmasked (0x444C90).
            case ScrValueKind.GlobalVariable: _shared.GlobalVariables[GlobalVariableIndex(index, line)] = value; break;
            // GV3, GV4: a write to any object but a PC is ignored (0x444DF0, 0x444E90).
            case ScrValueKind.PcVariable: if (PcStore(index) is { } variable) _shared.PcVariables[variable] = value; break;
            case ScrValueKind.PcFlag: if (PcStore(index) is { } flag) WriteFlag((i, v) => _shared.PcFlags[(flag.Pc, i)] = v, flag.Index, value); break;
            default: throw new InvalidOperationException("Store outside the interpreter's set.");
        }
    }

    private static int CounterShift(int index) => unchecked(8 * index) & 31;

    // X10, and GV4 for the hero's flags (0x444E90 is the same code over field 272): clear the flag's bit, then OR the value shifted to it into
    // the flag's 32-flag word.
    private void WriteFlag(Action<int, bool> flags, int index, int value)
    {
        if (!_options.Rules.GlobalFlagWriteSpills) { flags(index, (value & 1) != 0); return; }
        var bit = index % 32; var word = index - bit;
        var bits = unchecked((uint)(value << bit));
        flags(index, ((bits >> bit) & 1) != 0);
        for (var j = bit + 1; j < 32; j++) if (((bits >> j) & 1) != 0) flags(word + j, true);
    }

    // GV3, GV4: the PC cell the operand names, or null when its object is not a PC. The object is the first of the focus kind in the high 16
    // bits (0x44B960 with value 0, the shift keeps the sign); an absent one stops the run as for any object operand (OV3), and one that is not a
    // PC reads 0 and ignores a write. ARC-DEV-265: the cell is that PC's own (fields 272 and 273 of the object, SA14).
    private (ScrObjectRef Pc, int Index)? PcStore(int packed)
    {
        var obj = FocusObject((ScrFocusKind)(packed >> 16), 0) ?? throw new MissingObjectStop(_line);
        return _host.IsPc(obj) ? (obj, packed & 0xFFFF) : null;
    }

    private IScrStoreQueries Stores => _host as IScrStoreQueries
        ?? throw new InvalidOperationException("SCR_STORE_QUERIES_REQUIRED: the admission set holds a global variable, PC variable or PC flag operand and the host cannot answer it.");

    private int GlobalVariable(int index) =>
        _shared.GlobalVariables.TryGetValue(GlobalVariableIndex(index, _line), out var value) ? value : Stores.GlobalVariable(index);

    private int PcVariable((ScrObjectRef Pc, int Index) cell) => _shared.PcVariables.TryGetValue(cell, out var value) ? value : Stores.PcVariable(cell.Pc, cell.Index);

    private bool PcFlag((ScrObjectRef Pc, int Index) cell) => _shared.PcFlags.TryGetValue(cell, out var value) ? value : Stores.PcFlag(cell.Pc, cell.Index);

    private static int GlobalVariableIndex(int index, int line) => index is >= 0 and < ScrCatalogue.GlobalVariables ? index : throw new IndexStop(line);

    // The store changes the run leaves: a value equal to the store's own is no change.
    private ScrStoreWrites? StoreWrites()
    {
        var shared = _shared;
        if (shared.GlobalVariables.Count == 0 && shared.PcVariables.Count == 0 && shared.PcFlags.Count == 0) return null;
        var globals = shared.GlobalVariables.Where(p => p.Value != Stores.GlobalVariable(p.Key)).Select(p => new ScrVariableWrite(p.Key, p.Value)).ToArray();
        var variables = shared.PcVariables.Where(p => p.Value != Stores.PcVariable(p.Key.Pc, p.Key.Index)).Select(p => new ScrPcVariableWrite(p.Key.Pc, p.Key.Index, p.Value)).ToArray();
        var flags = shared.PcFlags.Where(p => p.Value != Stores.PcFlag(p.Key.Pc, p.Key.Index)).Select(p => new ScrPcFlagWrite(p.Key.Pc, p.Key.Index, p.Value)).ToArray();
        return globals.Length + variables.Length + flags.Length == 0 ? null : new ScrStoreWrites(globals, variables, flags);
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

    private ScrObjectRef? OptionalObject(ScrClause c, int slot) => FocusObject((ScrFocusKind)c.Kind(slot), c.Value(slot));

    [OriginalFunction(0x44B960, OriginalFunctionScope.Partial)]
    private ScrObjectRef? FocusObject(ScrFocusKind kind, int value) => kind switch
    {
        ScrFocusKind.Triggerer => _roles.Triggerer,
        ScrFocusKind.Attachee => _roles.Attachee,
        ScrFocusKind.ExtraObject => _roles.Extra,
        ScrFocusKind.Player => _host.Player(),
        // OV4: an object variable never assigned in this run names no object.
        ScrFocusKind.LocalObject => _objects[value],
        // PG7: the current looped object, none before the run's first loop.
        ScrFocusKind.CurrentLoopedObject => _looped,
        // A group's first object (0x44B960), none for an empty one.
        ScrFocusKind.EverySceneryInVicinity or ScrFocusKind.EveryoneInVicinity or ScrFocusKind.AnyoneInVicinity
            => Vicinity(kind) is { Count: > 0 } list ? list[0] : null,
        _ => throw new InvalidOperationException("Focus kind outside the interpreter's set."),
    };

    // The objects an object operand names, as the original's list (0x44B4E0): one for a single-object focus kind, none for an absent
    // one; ARC-WORLD-081: the vicinity's scenery for focus kind 15; ARC-SCRIPT-047: its PCs and NPCs for focus kinds 8 and 9.
    [OriginalFunction(0x44B4E0, OriginalFunctionScope.Partial)]
    private IReadOnlyList<ScrObjectRef> Objects(ScrClause c, int slot) => (ScrFocusKind)c.Kind(slot) switch
    {
        ScrFocusKind.EverySceneryInVicinity or ScrFocusKind.EveryoneInVicinity or ScrFocusKind.AnyoneInVicinity => Vicinity((ScrFocusKind)c.Kind(slot)),
        _ => OptionalObject(c, slot) is { } obj ? [obj] : [],
    };

    // PG3, TG4: the scenery (type mask 0x8) or the PCs and NPCs (0x18000) around the run's attachee (none without one), the first 100
    // under the original's rule.
    private IReadOnlyList<ScrObjectRef> Vicinity(ScrFocusKind kind)
    {
        if (_roles.Attachee is not { } centre) return [];
        var list = (kind == ScrFocusKind.EverySceneryInVicinity ? World.SceneryInVicinity(centre) : World.CrittersInVicinity(centre))
            ?? throw new InvalidOperationException("SCR_WORLD_VICINITY_NULL");
        return _options.Rules.VicinityListCapped && list.Count > ScrWorld.VicinityListCap ? list.Take(ScrWorld.VicinityListCap).ToArray() : list;
    }

    private IScrWorldQueries World => _host as IScrWorldQueries
        ?? throw new InvalidOperationException("SCR_WORLD_QUERIES_REQUIRED: the admission set holds random, loop for or a vicinity focus kind and the host cannot answer it.");

    private IScrAreaQueries Areas => _host as IScrAreaQueries
        ?? throw new InvalidOperationException("SCR_AREA_QUERIES_REQUIRED: the admission set holds get area and the host cannot answer it.");

    // The basic (0..11) and tech (12..15) skill numbers.
    private const int SkillNumbers = 16;

    private IScrSkillQueries Skills => _host as IScrSkillQueries
        ?? throw new InvalidOperationException("SCR_SKILL_QUERIES_REQUIRED: the admission set holds get skill and the host cannot answer it.");

    // GS2: the stat number of intelligence, the one stat get stat reads differently for a dumb critter.
    private const int DumbStat = 4;

    private IScrReactionQueries Reactions => _host as IScrReactionQueries
        ?? throw new InvalidOperationException("SCR_REACTION_QUERIES_REQUIRED: the admission set holds get reaction and the host cannot answer it.");

    private IScrStatQueries Stats => _host as IScrStatQueries
        ?? throw new InvalidOperationException("SCR_STAT_QUERIES_REQUIRED: the admission set holds get stat and the host cannot answer it.");

    private IScrItemQueries Items => _host as IScrItemQueries
        ?? throw new InvalidOperationException("SCR_ITEM_QUERIES_REQUIRED: the admission set holds an item question the host cannot answer.");

    // SG6: the objects of a condition's operand 0 that the test holds for; true when at least one does and either all do or the focus kind is
    // one of the "any" kinds (one is enough), false for an empty list.
    private bool ListHolds(ScrClause c, Func<ScrObjectRef, bool> test)
    {
        var list = Objects(c, 0);
        var held = list.Count(test);
        return held > 0 && (held == list.Count || (ScrFocusKind)c.Kind(0) is ScrFocusKind.AnyFollower or ScrFocusKind.AnyoneInParty or ScrFocusKind.AnyoneInTeam
            or ScrFocusKind.AnyoneInVicinity or ScrFocusKind.AnyoneInGroup or ScrFocusKind.AnySceneryInVicinity or ScrFocusKind.AnyContainerInVicinity
            or ScrFocusKind.AnyPortalInVicinity or ScrFocusKind.AnyItemInVicinity);
    }

    /// <summary>SC1: the four body-spell bits of the spell flags: body of air 0x4, earth 0x8, fire 0x10, water 0x20.</summary>
    private const int BodySpellFlags = 0x3C;

    // SC2–SC4: the value of operand 1 is read once before the list is walked (0x44BA70, as the original does), then the host answers per object; a null stops the run.
    private bool SpellHolds(ScrClause c, int line, Func<ScrObjectRef, int, bool?> test)
    {
        var value = Get(c, 1);
        return ListHolds(c, obj => test(obj, value) ?? throw new MissingObjectStop(line));
    }

    private IScrSpellQueries Spells => _host as IScrSpellQueries
        ?? throw new InvalidOperationException("SCR_SPELL_QUERIES_REQUIRED: the admission set holds a spell condition and the host cannot answer it.");

    private IScrObjectStateQueries States => _host as IScrObjectStateQueries
        ?? throw new InvalidOperationException("SCR_OBJECT_STATE_QUERIES_REQUIRED: the admission set holds the dead or switched-off condition and the host cannot answer it.");

    private IScrObjectQueries Queries => _host as IScrObjectQueries
        ?? throw new InvalidOperationException("SCR_OBJECT_QUERIES_REQUIRED: the admission set holds a guard condition the host cannot answer.");

    private bool Flag(int index) => _flags.TryGetValue(index, out var value) ? value : _host.GlobalFlag(index);

    private int Story() { _storyBefore ??= _host.StoryState(); return _story ??= _storyBefore.Value; }

    // The original shifts by the number with x86 shl (0x4473C2, 0x4473F5), which uses its low five bits.
    private static int LocalFlag(int index, int line) => index & (ScrCatalogue.LocalFlags - 1);
    private static int GlobalFlag(int index, int line) => index is >= 0 and < ScrCatalogue.GlobalFlags ? index : throw new IndexStop(line);
}
